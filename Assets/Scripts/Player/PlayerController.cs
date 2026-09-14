using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down player movement + shooting.
/// Rotation faces the mouse on the ground, or the right stick when using a gamepad.
/// Pair with Player Input (Behavior: Send Messages) using InputSystem_Actions.
/// Offline: full control. Networked: only the owning client drives input/movement.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerController : NetworkBehaviour, IDamageable
{
    public static PlayerController Instance { get; private set; }

    /// <summary>
    /// True when this instance should read input and simulate movement.
    /// Offline / unspawned objects always have control; networked remotes do not.
    /// </summary>
    public bool HasControl => !IsSpawned || IsOwner;
    [Header("Health")]
    [SerializeField] float maxHealth = 5f;

    [Header("Damage Feedback")]
    [SerializeField] float damageShakeDuration = 0.18f;
    [SerializeField] float damageShakeStrength = 0.22f;
    [SerializeField] float damageShakeRotation = 1.2f;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float acceleration = 40f;
    [SerializeField] float deceleration = 50f;

    [Header("Dash")]
    [SerializeField] float dashSpeed = 22f;
    [SerializeField] float dashDuration = 0.16f;
    [SerializeField] float dashCooldown = 0.7f;

    [Header("USB Dash Upgrade")]
    [SerializeField] bool hasUsbDash;
    [SerializeField] float usbTrailDuration = 0.4f;
    [SerializeField] float usbTrailRadius = 0.85f;
    [SerializeField] float usbTrailSpawnInterval = 0.04f;
    [Tooltip("Extra seconds after the dash ends where firewalls can still be phased.")]
    [SerializeField] float usbFirewallPhaseLinger = 0.12f;

    [Header("Goat Dash Upgrade")]
    [SerializeField] bool hasGoatDash;
    [SerializeField] float goatDashDamage = 2f;
    [SerializeField] float goatDashRamRadius = 1.1f;
    [SerializeField] float goatDashRamHeight = 1.6f;
    [SerializeField] float goatDashKnockbackSpeed = 16f;
    [SerializeField] float goatDashKnockbackDuration = 0.3f;
    [Tooltip("Extra invulnerability seconds after the Goat Dash ends.")]
    [SerializeField] float goatDashInvulnLinger = 0.12f;

    [Header("Facing")]
    [Tooltip("Leave empty to use Camera.main.")]
    [SerializeField] Camera worldCamera;
    [SerializeField] float rotationSpeed = 720f;
    [SerializeField, Range(0.05f, 0.9f)] float lookStickDeadzone = 0.25f;

    [Header("Shooting")]
    [SerializeField] Projectile projectilePrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float fireCooldown = 0.2f;
    [SerializeField] WeaponDefinition[] weapons;
    [SerializeField] int startingWeaponIndex;
    [Tooltip("Weapon indices unlocked at run start. Index 0 (Pistol) should stay unlocked.")]
    [SerializeField] int[] startingUnlockedWeapons = { 0 };

    static readonly Collider[] GoatDashHits = new Collider[24];

    Rigidbody rb;
    Vector2 moveInput;
    bool isDashing;
    float dashTimer;
    float dashCooldownTimer;
    Vector3 dashDirection;
    float cooldownTimer;
    float currentHealth;
    bool isDead;
    int currentWeaponIndex;
    InputAction attackAction;
    bool usingStickAim;
    bool controlsLocked;
    float usbTrailSpawnTimer;
    float usbFirewallPhaseTimer;
    bool usbFirewallPhaseActive;
    float goatDashInvulnTimer;
    Collider[] playerColliders;
    readonly HashSet<EnemyAI> goatDashHitEnemies = new HashSet<EnemyAI>();
    readonly HashSet<int> unlockedWeapons = new HashSet<int>();

    /// <summary>Current health, max health.</summary>
    public event Action<float, float> HealthChanged;
    public event Action Died;
    public event Action<string> WeaponChanged;
    public event Action UsbDashGranted;
    public event Action GoatDashGranted;
    public event Action<int, string> WeaponGranted;

    public Vector2 MoveInput => moveInput;
    public bool IsDashing => isDashing;
    public bool HasUsbDash => hasUsbDash;
    public bool HasGoatDash => hasGoatDash;
    public bool IsGoatDashInvulnerable =>
        hasGoatDash && (isDashing || goatDashInvulnTimer > 0f);
    public float CurrentSpeed => new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool ControlsLocked => controlsLocked;

    public void SetControlsLocked(bool locked)
    {
        controlsLocked = locked;
        if (!locked)
            return;

        moveInput = Vector2.zero;
        isDashing = false;
        dashTimer = 0f;
        goatDashInvulnTimer = 0f;
        if (rb != null)
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        EndUsbFirewallPhase();
    }

    public void TeleportTo(Vector3 worldPosition)
    {
        isDashing = false;
        dashTimer = 0f;
        goatDashInvulnTimer = 0f;
        EndUsbFirewallPhase();
        moveInput = Vector2.zero;
        if (rb == null)
        {
            transform.position = worldPosition;
            return;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = worldPosition;
        transform.position = worldPosition;
    }

    public string CurrentWeaponName
    {
        get
        {
            WeaponDefinition weapon = CurrentWeapon;
            if (weapon != null && !string.IsNullOrEmpty(weapon.displayName))
                return weapon.displayName;
            return "Pistol";
        }
    }

    WeaponDefinition CurrentWeapon
    {
        get
        {
            if (weapons == null || weapons.Length == 0)
                return null;

            return weapons[Mathf.Clamp(currentWeaponIndex, 0, weapons.Length - 1)];
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        currentHealth = maxHealth;

        if (worldCamera == null)
            worldCamera = Camera.main;

        playerColliders = GetComponentsInChildren<Collider>(true);
        InitializeUnlockedWeapons();
        SelectWeapon(startingWeaponIndex, notify: false);

        // Offline / pre-spawn: claim local player immediately.
        // Networked clones wait for OnNetworkSpawn so only the owner becomes Instance.
        if (!IsSpawned && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            Instance = this;
            ApplyControlState();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ApplyControlState();

        if (IsOwner)
        {
            Instance = this;
            name = $"Player (You #{OwnerClientId})";
            PlaceAtNetworkSpawnSlot((int)OwnerClientId);
        }
        else
        {
            name = $"Player (#{OwnerClientId})";
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }

    void ApplyControlState()
    {
        bool control = HasControl;

        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
            playerInput.enabled = control;

        if (rb == null)
            return;

        if (control)
        {
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        else
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void PlaceAtNetworkSpawnSlot(int slot)
    {
        float angle = slot * 90f;
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.5f;
        Vector3 spawnPos = new Vector3(offset.x, transform.position.y, offset.z);
        TeleportTo(spawnPos);
        transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        if (rb != null)
            rb.rotation = transform.rotation;
    }

    void InitializeUnlockedWeapons()
    {
        unlockedWeapons.Clear();
        if (startingUnlockedWeapons != null)
        {
            for (int i = 0; i < startingUnlockedWeapons.Length; i++)
            {
                int index = startingUnlockedWeapons[i];
                if (IsValidWeaponIndex(index))
                    unlockedWeapons.Add(index);
            }
        }

        if (unlockedWeapons.Count == 0 && IsValidWeaponIndex(0))
            unlockedWeapons.Add(0);

        if (!IsWeaponUnlocked(startingWeaponIndex))
            startingWeaponIndex = GetFirstUnlockedWeaponIndex();
    }

    bool IsValidWeaponIndex(int index)
    {
        return weapons != null && index >= 0 && index < weapons.Length;
    }

    int GetFirstUnlockedWeaponIndex()
    {
        if (weapons == null)
            return 0;

        for (int i = 0; i < weapons.Length; i++)
        {
            if (unlockedWeapons.Contains(i))
                return i;
        }

        return 0;
    }

    public bool HasWeapon(int weaponIndex)
    {
        return IsWeaponUnlocked(weaponIndex);
    }

    public bool IsWeaponUnlocked(int weaponIndex)
    {
        return IsValidWeaponIndex(weaponIndex) && unlockedWeapons.Contains(weaponIndex);
    }

    public string GetWeaponDisplayName(int weaponIndex)
    {
        if (!IsValidWeaponIndex(weaponIndex))
            return "Weapon";

        WeaponDefinition weapon = weapons[weaponIndex];
        if (weapon != null && !string.IsNullOrEmpty(weapon.displayName))
            return weapon.displayName;

        return "Weapon";
    }

    /// <summary>Unlocks a weapon by loadout index and equips it. Returns false if already owned.</summary>
    public bool GrantWeapon(int weaponIndex)
    {
        if (!HasControl || isDead || !IsValidWeaponIndex(weaponIndex))
            return false;

        if (!unlockedWeapons.Add(weaponIndex))
            return false;

        SelectWeapon(weaponIndex);
        WeaponGranted?.Invoke(weaponIndex, CurrentWeaponName);
        InfectionReport.RecordUpgrade(CurrentWeaponName);
        return true;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        CacheAttackAction();
    }

    void Update()
    {
        if (!HasControl)
            return;

        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        TickUsbFirewallPhase();
        TickGoatDashInvulnerability();

        if (controlsLocked)
            return;

        PollWeaponHotkeys();

        if (IsAttackHeld() && CurrentWeapon != null && CurrentWeapon.fireMode == WeaponFireMode.Automatic)
            TryFire();
    }

    void FixedUpdate()
    {
        if (!HasControl)
            return;

        if (isDead || controlsLocked)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        Move();
        FaceAim();
    }

    public void OnMove(InputValue value)
    {
        if (!HasControl)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = (isDead || controlsLocked) ? Vector2.zero : value.Get<Vector2>();
    }

    public void OnDash(InputValue value)
    {
        if (!HasControl || !value.isPressed || isDead || controlsLocked)
            return;

        TryDash();
    }

    public void OnAttack(InputValue value)
    {
        if (!HasControl || !value.isPressed || isDead || controlsLocked)
            return;

        if (CurrentWeapon == null || CurrentWeapon.fireMode == WeaponFireMode.SemiAutomatic)
            TryFire();
    }

    public void OnNext(InputValue value)
    {
        if (!HasControl || !value.isPressed || isDead || controlsLocked)
            return;

        CycleWeapon(1);
    }

    public void OnPrevious(InputValue value)
    {
        if (!HasControl || !value.isPressed || isDead || controlsLocked)
            return;

        CycleWeapon(-1);
    }

    void Move()
    {
        if (isDashing)
        {
            dashTimer -= Time.fixedDeltaTime;
            rb.linearVelocity = new Vector3(
                dashDirection.x * dashSpeed,
                rb.linearVelocity.y,
                dashDirection.z * dashSpeed);

            TickUsbDashTrail();
            TickGoatDashRam();

            if (dashTimer <= 0f)
                EndDash();

            return;
        }

        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 desiredHorizontal = inputDirection * moveSpeed;

        float rate = desiredHorizontal.sqrMagnitude > currentHorizontal.sqrMagnitude
            ? acceleration
            : deceleration;

        Vector3 newHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            desiredHorizontal,
            rate * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector3(newHorizontal.x, rb.linearVelocity.y, newHorizontal.z);
    }

    void FaceAim()
    {
        Vector2 stick = ReadLookStick();
        if (stick.sqrMagnitude >= lookStickDeadzone * lookStickDeadzone)
        {
            usingStickAim = true;
            FaceDirection(StickToWorldDirection(stick));
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.25f)
            usingStickAim = false;

        if (usingStickAim)
            return;

        FaceMouse();
    }

    Vector2 ReadLookStick()
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
            return gamepad.rightStick.ReadValue();

        return Vector2.zero;
    }

    Vector3 StickToWorldDirection(Vector2 stick)
    {
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (worldCamera == null)
            worldCamera = Camera.main;

        if (worldCamera != null)
        {
            forward = worldCamera.transform.forward;
            right = worldCamera.transform.right;
            forward.y = 0f;
            right.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;

            forward.Normalize();
            right.Normalize();
        }

        Vector3 direction = right * stick.x + forward * stick.y;
        direction.y = 0f;
        return direction;
    }

    void FaceMouse()
    {
        if (worldCamera == null)
        {
            worldCamera = Camera.main;
            if (worldCamera == null)
                return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, rb.position.y, 0f));

        if (!groundPlane.Raycast(ray, out float enter))
            return;

        FaceDirection(ray.GetPoint(enter) - rb.position);
    }

    void FaceDirection(Vector3 lookDirection)
    {
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        Quaternion nextRotation = Quaternion.RotateTowards(
            rb.rotation,
            targetRotation,
            rotationSpeed * Time.fixedDeltaTime);

        rb.MoveRotation(nextRotation);
    }

    void TryDash()
    {
        if (isDashing || dashCooldownTimer > 0f)
            return;

        Vector3 direction = new Vector3(moveInput.x, 0f, moveInput.y);
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude < 0.001f)
            direction = Vector3.forward;

        dashDirection = direction.normalized;
        isDashing = true;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        usbTrailSpawnTimer = 0f;
        goatDashHitEnemies.Clear();

        if (hasGoatDash)
            goatDashInvulnTimer = Mathf.Max(goatDashInvulnTimer, goatDashInvulnLinger);

        if (hasUsbDash)
        {
            BeginUsbFirewallPhase();
            SpawnUsbDashTrail();
        }
    }

    void EndDash()
    {
        isDashing = false;
        if (hasUsbDash)
            usbFirewallPhaseTimer = Mathf.Max(usbFirewallPhaseTimer, usbFirewallPhaseLinger);
        if (hasGoatDash)
            goatDashInvulnTimer = Mathf.Max(goatDashInvulnTimer, goatDashInvulnLinger);
    }

    public bool GrantUsbDash()
    {
        if (!HasControl || hasUsbDash || isDead)
            return false;

        hasUsbDash = true;
        UsbDashGranted?.Invoke();
        InfectionReport.RecordUpgrade("USB Dash");
        return true;
    }

    public bool GrantGoatDash()
    {
        if (!HasControl || hasGoatDash || isDead)
            return false;

        hasGoatDash = true;
        GoatDashGranted?.Invoke();
        InfectionReport.RecordUpgrade("Goat Dash");
        return true;
    }

    void TickUsbDashTrail()
    {
        if (!hasUsbDash)
            return;

        usbTrailSpawnTimer -= Time.fixedDeltaTime;
        if (usbTrailSpawnTimer > 0f)
            return;

        usbTrailSpawnTimer = Mathf.Max(0.01f, usbTrailSpawnInterval);
        SpawnUsbDashTrail();
    }

    void SpawnUsbDashTrail()
    {
        UsbDashTrail.Spawn(
            rb.position,
            dashDirection,
            usbTrailDuration,
            usbTrailRadius);
    }

    void TickGoatDashRam()
    {
        if (!hasGoatDash || goatDashDamage <= 0f)
            return;

        Vector3 point1 = rb.position + Vector3.up * 0.2f;
        Vector3 point2 = rb.position + Vector3.up * Mathf.Max(0.4f, goatDashRamHeight);
        float radius = Mathf.Max(0.2f, goatDashRamRadius);

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            point1,
            point2,
            radius,
            GoatDashHits,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = GoatDashHits[i];
            if (hit == null)
                continue;

            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            EnemyAI enemy = hit.GetComponentInParent<EnemyAI>();
            if (enemy == null || !enemy.IsAlive || !goatDashHitEnemies.Add(enemy))
                continue;

            enemy.TakeDamage(goatDashDamage);

            Vector3 launchDirection = dashDirection;
            launchDirection.y = 0f;
            if (launchDirection.sqrMagnitude < 0.001f)
            {
                launchDirection = enemy.transform.position - rb.position;
                launchDirection.y = 0f;
            }

            enemy.ApplyKnockback(
                launchDirection,
                goatDashKnockbackSpeed,
                goatDashKnockbackDuration);
        }
    }

    void TickGoatDashInvulnerability()
    {
        if (goatDashInvulnTimer <= 0f)
            return;

        // Stay invulnerable for the whole dash; linger ticks only after it ends.
        if (isDashing && hasGoatDash)
            return;

        goatDashInvulnTimer -= Time.deltaTime;
        if (goatDashInvulnTimer < 0f)
            goatDashInvulnTimer = 0f;
    }

    void TickUsbFirewallPhase()
    {
        if (!usbFirewallPhaseActive)
            return;

        if (isDashing)
            return;

        usbFirewallPhaseTimer -= Time.deltaTime;
        if (usbFirewallPhaseTimer > 0f)
            return;

        EndUsbFirewallPhase();
    }

    void BeginUsbFirewallPhase()
    {
        SetUsbFirewallIgnore(true);
        usbFirewallPhaseActive = true;
        usbFirewallPhaseTimer = Mathf.Max(0f, usbFirewallPhaseLinger);
    }

    void EndUsbFirewallPhase()
    {
        SetUsbFirewallIgnore(false);
        usbFirewallPhaseActive = false;
        usbFirewallPhaseTimer = 0f;
    }

    void SetUsbFirewallIgnore(bool ignore)
    {
        if (playerColliders == null || playerColliders.Length == 0)
            playerColliders = GetComponentsInChildren<Collider>(true);

        IReadOnlyList<FirewallTrap> firewalls = FirewallTrap.Active;
        for (int i = 0; i < firewalls.Count; i++)
        {
            FirewallTrap firewall = firewalls[i];
            if (firewall == null || firewall.BlockingCollider == null)
                continue;

            for (int c = 0; c < playerColliders.Length; c++)
            {
                Collider playerCollider = playerColliders[c];
                if (playerCollider == null)
                    continue;

                Physics.IgnoreCollision(playerCollider, firewall.BlockingCollider, ignore);
            }
        }
    }

    void TryFire()
    {
        if (cooldownTimer > 0f)
            return;

        WeaponDefinition weapon = CurrentWeapon;
        Projectile prefab = weapon != null && weapon.projectilePrefab != null
            ? weapon.projectilePrefab
            : projectilePrefab;
        if (prefab == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position + transform.forward * 0.5f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        int pelletCount = weapon != null ? Mathf.Max(1, weapon.pelletCount) : 1;
        float spread = weapon != null ? weapon.spreadAngle : 0f;
        float jitter = weapon != null ? weapon.spreadJitter : 0f;

        for (int i = 0; i < pelletCount; i++)
        {
            float t = pelletCount == 1 ? 0.5f : i / (float)(pelletCount - 1);
            float yaw = Mathf.Lerp(-spread, spread, t) + UnityEngine.Random.Range(-jitter, jitter);
            Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                direction = forward;
            direction.Normalize();

            Projectile projectile = Instantiate(prefab, spawnPos, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Launch(direction, transform);
        }

        cooldownTimer = weapon != null ? weapon.fireCooldown : fireCooldown;
    }

    bool IsAttackHeld()
    {
        if (isDead)
            return false;

        CacheAttackAction();
        if (attackAction != null)
            return attackAction.IsPressed();

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonWest.isPressed;
    }

    void CacheAttackAction()
    {
        if (attackAction != null)
            return;

        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
            attackAction = playerInput.actions.FindAction("Attack");
    }

    void PollWeaponHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || isDead)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
            TrySelectUnlockedWeapon(0);
        else if (keyboard.digit2Key.wasPressedThisFrame)
            TrySelectUnlockedWeapon(1);
        else if (keyboard.digit3Key.wasPressedThisFrame)
            TrySelectUnlockedWeapon(2);
        else if (keyboard.digit4Key.wasPressedThisFrame)
            TrySelectUnlockedWeapon(3);

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll > 0.1f)
            CycleWeapon(1);
        else if (scroll < -0.1f)
            CycleWeapon(-1);
    }

    void TrySelectUnlockedWeapon(int index)
    {
        if (!IsWeaponUnlocked(index))
            return;

        SelectWeapon(index);
    }

    void CycleWeapon(int step)
    {
        if (weapons == null || weapons.Length == 0 || unlockedWeapons.Count == 0)
            return;

        int count = weapons.Length;
        int nextIndex = currentWeaponIndex;
        for (int i = 0; i < count; i++)
        {
            nextIndex = (nextIndex + step) % count;
            if (nextIndex < 0)
                nextIndex += count;

            if (unlockedWeapons.Contains(nextIndex))
            {
                SelectWeapon(nextIndex);
                return;
            }
        }
    }

    void SelectWeapon(int index, bool notify = true)
    {
        if (weapons == null || weapons.Length == 0)
        {
            currentWeaponIndex = 0;
            return;
        }

        if (!IsWeaponUnlocked(index))
            index = GetFirstUnlockedWeaponIndex();

        currentWeaponIndex = Mathf.Clamp(index, 0, weapons.Length - 1);
        if (notify)
            WeaponChanged?.Invoke(CurrentWeaponName);
    }

    public void TakeDamage(float amount)
    {
        // Owner-authoritative for now: only the controlling client applies damage locally.
        if (!HasControl || amount <= 0f || isDead || IsGoatDashInvulnerable)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        InfectionReport.RecordDamageTaken(amount, currentHealth, maxHealth);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        ScreenShake.Shake(damageShakeDuration, damageShakeStrength, damageShakeRotation);

        if (currentHealth <= 0f)
            Die();
    }

    public bool TryHeal(float amount)
    {
        if (!HasControl || amount <= 0f || isDead || currentHealth >= maxHealth)
            return false;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        return true;
    }

    void Die()
    {
        isDead = true;
        moveInput = Vector2.zero;
        isDashing = false;
        dashTimer = 0f;
        goatDashInvulnTimer = 0f;
        EndUsbFirewallPhase();
        rb.linearVelocity = Vector3.zero;
        Died?.Invoke();
    }
}
