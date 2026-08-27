using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down player movement + shooting.
/// Rotation faces the mouse on the ground, or the right stick when using a gamepad.
/// Pair with Player Input (Behavior: Send Messages) using InputSystem_Actions.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerController : MonoBehaviour, IDamageable
{
    public static PlayerController Instance { get; private set; }
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

    /// <summary>Current health, max health.</summary>
    public event Action<float, float> HealthChanged;
    public event Action Died;
    public event Action<string> WeaponChanged;

    public Vector2 MoveInput => moveInput;
    public bool IsDashing => isDashing;
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
        if (rb != null)
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    public void TeleportTo(Vector3 worldPosition)
    {
        isDashing = false;
        dashTimer = 0f;
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
        Instance = this;
        rb = GetComponent<Rigidbody>();
        rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        currentHealth = maxHealth;

        if (worldCamera == null)
            worldCamera = Camera.main;

        SelectWeapon(startingWeaponIndex, notify: false);
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
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        if (controlsLocked)
            return;

        PollWeaponHotkeys();

        if (IsAttackHeld() && CurrentWeapon != null && CurrentWeapon.fireMode == WeaponFireMode.Automatic)
            TryFire();
    }

    void FixedUpdate()
    {
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
        moveInput = (isDead || controlsLocked) ? Vector2.zero : value.Get<Vector2>();
    }

    public void OnDash(InputValue value)
    {
        if (!value.isPressed || isDead || controlsLocked)
            return;

        TryDash();
    }

    public void OnAttack(InputValue value)
    {
        if (!value.isPressed || isDead || controlsLocked)
            return;

        if (CurrentWeapon == null || CurrentWeapon.fireMode == WeaponFireMode.SemiAutomatic)
            TryFire();
    }

    public void OnNext(InputValue value)
    {
        if (!value.isPressed || isDead || controlsLocked)
            return;

        CycleWeapon(1);
    }

    public void OnPrevious(InputValue value)
    {
        if (!value.isPressed || isDead || controlsLocked)
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

            if (dashTimer <= 0f)
                isDashing = false;

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
            SelectWeapon(0);
        else if (keyboard.digit2Key.wasPressedThisFrame)
            SelectWeapon(1);
        else if (keyboard.digit3Key.wasPressedThisFrame)
            SelectWeapon(2);
        else if (keyboard.digit4Key.wasPressedThisFrame)
            SelectWeapon(3);

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll > 0.1f)
            CycleWeapon(1);
        else if (scroll < -0.1f)
            CycleWeapon(-1);
    }

    void CycleWeapon(int step)
    {
        if (weapons == null || weapons.Length == 0)
            return;

        int count = weapons.Length;
        int nextIndex = (currentWeaponIndex + step) % count;
        if (nextIndex < 0)
            nextIndex += count;

        SelectWeapon(nextIndex);
    }

    void SelectWeapon(int index, bool notify = true)
    {
        if (weapons == null || weapons.Length == 0)
        {
            currentWeaponIndex = 0;
            return;
        }

        currentWeaponIndex = Mathf.Clamp(index, 0, weapons.Length - 1);
        if (notify)
            WeaponChanged?.Invoke(CurrentWeaponName);
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || isDead)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        ScreenShake.Shake(damageShakeDuration, damageShakeStrength, damageShakeRotation);

        if (currentHealth <= 0f)
            Die();
    }

    void Die()
    {
        isDead = true;
        moveInput = Vector2.zero;
        isDashing = false;
        dashTimer = 0f;
        rb.linearVelocity = Vector3.zero;
        Died?.Invoke();
    }
}
