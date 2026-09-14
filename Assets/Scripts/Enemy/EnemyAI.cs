using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy chase AI (NavMesh) + health.
/// Tune movement on the NavMeshAgent. Bake a NavMesh before Play.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour, IDamageable
{
    [Header("Target")]
    [Tooltip("Leave empty to auto-find the player (PlayerController, then tag \"Player\").")]
    [SerializeField] Transform player;
    [SerializeField] float pathRefreshInterval = 0.2f;

    [Header("Health")]
    [SerializeField] float maxHealth = 1f;

    [Header("Spawn Difficulty")]
    [Tooltip("1–5 difficulty used for depth-weighted dungeon spawns. 0 = treat as difficulty 1.")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("threatRating")]
    int difficulty;

    [Header("Contact Damage")]
    [Tooltip("Damage dealt to the player on touch. Set to 0 for enemies that attack another way.")]
    [SerializeField] float contactDamage = 1f;
    [Tooltip("Flat XZ distance to the player required to land a hit.")]
    [SerializeField] float attackRange = 2f;
    [Tooltip("Seconds between repeated hits while touching the player.")]
    [SerializeField] float attackInterval = 1f;

    [Header("Death")]
    [SerializeField] GameObject deathEffectPrefab;
    [SerializeField] float deathEffectHeight = 0.35f;

    protected NavMeshAgent Agent { get; private set; }
    protected Transform Player { get; private set; }

    float pathRefreshTimer;
    float attackTimer;
    float currentHealth;
    IDamageable playerDamageable;
    float combatHoldoff;
    float playerFindRetryTimer;

    float baseMoveSpeed = -1f;
    float moveSpeedMultiplier = 1f;
    float damageMultiplier = 1f;
    float fireRateMultiplier = 1f;
    int damageBuffStacks;
    int healthBuffStacks;
    int speedBuffStacks;
    int fireRateBuffStacks;
    EnemyShield equippedShield;

    Vector3 knockbackVelocity;
    float knockbackTimer;

    public bool IsChasing { get; protected set; }
    public bool IsKnockedBack => knockbackTimer > 0f;
    public float DamageMultiplier => damageMultiplier;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public int Difficulty => difficulty > 0 ? difficulty : 1;
    /// <summary>Alias used by depth-weighted spawn mix (same as Difficulty).</summary>
    public int ThreatRating => Difficulty;
    public bool IsDamaged => currentHealth > 0f && currentHealth < maxHealth - 0.001f;
    public bool IsAlive => currentHealth > 0f;
    public RoomEncounter BoundEncounter => boundEncounter;

    protected bool CanAct => combatHoldoff <= 0f;

    protected RoomEncounter boundEncounter;

    protected virtual void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        Agent.updateRotation = false;
        currentHealth = maxHealth;
        CacheBaseMoveSpeed();
    }

    protected virtual void Start()
    {
        if (player == null)
            FindPlayer();

        Player = player;
        CacheBaseMoveSpeed();

        if (ShowSupportPriorityMarker)
            SupportPriorityMarker.EnsureOn(this);
    }

    /// <summary>When true, shows a kill-first priority marker (Repair / Overclock).</summary>
    protected virtual bool ShowSupportPriorityMarker => false;

    /// <summary>Repair / Overclock-style supports. Other supports won't cling to these.</summary>
    public virtual bool IsSupportEnemy => false;

    /// <summary>
    /// When false, Transfer enemies will not redirect incoming damage onto this unit.
    /// </summary>
    public virtual bool CanBeTransferDamageTarget => true;

    /// <summary>
    /// When true, RoomEncounter places this enemy near the player instead of far away.
    /// </summary>
    public virtual bool PrefersNearPlayerSpawn => false;

    protected virtual void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        UpdateChase();
        UpdateContactDamage();
    }

    public void SetCombatHoldoff(float seconds)
    {
        combatHoldoff = Mathf.Max(0f, seconds);
        if (combatHoldoff <= 0f || Agent == null || !Agent.isOnNavMesh)
            return;

        StopAgentPath();
        IsChasing = false;
    }

    /// <summary>
    /// Stops NavMesh motion without resetting the path every frame.
    /// </summary>
    protected void StopAgentPath()
    {
        if (Agent == null || !Agent.isOnNavMesh)
            return;

        if (Agent.isStopped && !Agent.hasPath)
            return;

        Agent.isStopped = true;
        if (Agent.hasPath)
            Agent.ResetPath();
    }

    protected void TickHoldoff()
    {
        if (combatHoldoff <= 0f)
            return;

        combatHoldoff -= Time.deltaTime;
        if (combatHoldoff > 0f)
            return;

        combatHoldoff = 0f;
        if (Agent != null && Agent.isOnNavMesh)
            Agent.isStopped = false;
    }

    protected void UpdateContactDamage()
    {
        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;

        if (contactDamage <= 0f || Player == null || attackTimer > 0f)
            return;

        Vector3 offset = Player.position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > attackRange * attackRange)
            return;

        if (playerDamageable == null)
            playerDamageable = Player.GetComponentInParent<IDamageable>();

        if (playerDamageable == null)
            return;

        playerDamageable.TakeDamage(ScaleOutgoingDamage(contactDamage));
        attackTimer = attackInterval;
    }

    /// <summary>Who this enemy paths toward. Override for support units that stick to allies.</summary>
    protected virtual Transform GetChaseTarget()
    {
        return Player;
    }

    /// <summary>World-space point to path toward for the current chase target.</summary>
    protected virtual Vector3 GetChaseDestination(Transform chaseTarget)
    {
        return chaseTarget.position;
    }

    /// <summary>How close is “close enough” to the chase target.</summary>
    protected virtual float GetChaseStopDistance()
    {
        return Agent != null ? Agent.stoppingDistance : 0.5f;
    }

    protected void UpdateChase()
    {
        if (knockbackTimer > 0f)
        {
            IsChasing = false;
            return;
        }

        playerFindRetryTimer -= Time.deltaTime;
        if (playerFindRetryTimer <= 0f || player == null)
        {
            playerFindRetryTimer = 0.5f;
            FindPlayer();
        }

        if (player == null)
        {
            Player = null;
            StopAgentPath();
            IsChasing = false;
            return;
        }

        Player = player;
        Transform chaseTarget = GetChaseTarget();
        if (chaseTarget == null)
            chaseTarget = Player;

        if (!Agent.isOnNavMesh)
        {
            IsChasing = false;
            return;
        }

        float stopDistance = Mathf.Max(0.1f, GetChaseStopDistance());
        pathRefreshTimer -= Time.deltaTime;
        bool refreshPath = pathRefreshTimer <= 0f;
        if (refreshPath)
        {
            pathRefreshTimer = pathRefreshInterval;
            Agent.isStopped = false;
            Agent.SetDestination(GetChaseDestination(chaseTarget));
        }

        // Sample remainingDistance only on repath frames — it forces path length work.
        bool arrived = !Agent.pathPending &&
                       (refreshPath
                           ? Agent.remainingDistance <= stopDistance
                           : Agent.desiredVelocity.sqrMagnitude < 0.01f &&
                             (chaseTarget.position - transform.position).sqrMagnitude <=
                             stopDistance * stopDistance);

        if (arrived)
        {
            StopAgentPath();
            FaceDirection(chaseTarget.position - transform.position);
            IsChasing = false;
            return;
        }

        Agent.isStopped = false;
        IsChasing = Agent.desiredVelocity.sqrMagnitude > 0.01f;

        if (IsChasing)
            FaceDirection(Agent.desiredVelocity);
    }

    /// <summary>
    /// Launches this enemy along a flat direction (e.g. Goat Dash ram).
    /// </summary>
    public void ApplyKnockback(Vector3 direction, float speed, float duration)
    {
        if (!IsAlive || speed <= 0f || duration <= 0f)
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            direction = -transform.forward;
        direction.Normalize();

        knockbackVelocity = direction * speed;
        knockbackTimer = duration;
        IsChasing = false;
        StopAgentPath();

        if (Agent != null && Agent.isOnNavMesh)
            Agent.velocity = Vector3.zero;
    }

    protected virtual void LateUpdate()
    {
        TickKnockback();
    }

    void TickKnockback()
    {
        if (knockbackTimer <= 0f)
            return;

        float dt = Time.deltaTime;
        float previous = knockbackTimer;
        knockbackTimer -= dt;

        if (knockbackVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 step = knockbackVelocity * dt;
            if (Agent != null && Agent.isOnNavMesh)
                Agent.Move(step);
            else
                transform.position += step;
        }

        if (knockbackTimer > 0f && previous > 0.0001f)
        {
            // Ease out so the launch reads as a shove, not a constant slide.
            float retain = Mathf.Clamp01(knockbackTimer / previous);
            knockbackVelocity *= retain;
            return;
        }

        knockbackTimer = 0f;
        knockbackVelocity = Vector3.zero;

        if (Agent == null)
            return;

        if (!Agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, Agent.areaMask))
                Agent.Warp(hit.position);
            return;
        }

        Agent.isStopped = false;
    }

    public virtual void TakeDamage(float amount)
    {
        if (amount <= 0f || currentHealth <= 0f)
            return;

        if (HasActiveShield)
        {
            equippedShield.AbsorbDamage(amount);
            return;
        }

        currentHealth -= amount;
        if (currentHealth <= 0f)
            Die();
    }

    public bool HasActiveShield => equippedShield != null && equippedShield.IsActive;

    /// <summary>
    /// Spawns a see-through dome shield around this enemy.
    /// </summary>
    public void GrantShield(float shieldHealth, float radiusMultiplier = 1f)
    {
        if (!IsAlive || HasActiveShield || shieldHealth <= 0f)
            return;

        float radius = 2.2f;
        if (Agent != null)
            radius = Mathf.Max(1.4f, Agent.radius * 1.85f + 0.75f);
        radius *= Mathf.Max(0.25f, radiusMultiplier);

        var shieldObject = new GameObject("Hex Shield Dome");
        shieldObject.transform.SetParent(transform, false);
        shieldObject.transform.localPosition = Vector3.up * (radius * 0.15f);
        shieldObject.layer = gameObject.layer;

        equippedShield = shieldObject.AddComponent<EnemyShield>();
        equippedShield.Initialize(this, shieldHealth, radius);
    }

    public void NotifyShieldBroken()
    {
        equippedShield = null;
    }

    public float ScaleOutgoingDamage(float amount)
    {
        return amount * damageMultiplier;
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || currentHealth <= 0f)
            return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public bool TryApplyDamageBuff(float multiplier, int maxStacks)
    {
        if (multiplier <= 0f || damageBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        damageBuffStacks++;
        damageMultiplier *= multiplier;
        return true;
    }

    public bool TryApplyMaxHealthBuff(float multiplier, int maxStacks)
    {
        if (multiplier <= 0f || healthBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        healthBuffStacks++;
        float previousMax = maxHealth;
        maxHealth *= multiplier;
        currentHealth += maxHealth - previousMax;
        return true;
    }

    public bool TryApplySpeedBuff(float multiplier, int maxStacks)
    {
        if (multiplier <= 0f || speedBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        speedBuffStacks++;
        CacheBaseMoveSpeed();
        moveSpeedMultiplier *= multiplier;
        RefreshMoveSpeed();
        return true;
    }

    /// <summary>
    /// One-shot Corrupted Save mutation: lower max health, higher move speed.
    /// </summary>
    public void ApplyCorruptedSaveMutation(float healthMultiplier, float speedMultiplier)
    {
        healthMultiplier = Mathf.Clamp(healthMultiplier, 0.05f, 1f);
        speedMultiplier = Mathf.Max(0.05f, speedMultiplier);

        maxHealth = Mathf.Max(0.1f, maxHealth * healthMultiplier);
        currentHealth = maxHealth;

        CacheBaseMoveSpeed();
        moveSpeedMultiplier *= speedMultiplier;
        RefreshMoveSpeed();
    }

    public bool TryApplyFireRateBuff(float multiplier, int maxStacks)
    {
        if (!SupportsFireRateBuff || multiplier <= 0f || fireRateBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        fireRateBuffStacks++;
        fireRateMultiplier *= multiplier;
        return true;
    }

    public bool TryRemoveDamageBuff(float multiplier)
    {
        if (damageBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        damageBuffStacks--;
        damageMultiplier /= multiplier;
        return true;
    }

    public bool TryRemoveMaxHealthBuff(float multiplier)
    {
        if (healthBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        healthBuffStacks--;
        float previousMax = maxHealth;
        maxHealth /= multiplier;
        currentHealth -= previousMax - maxHealth;
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
        if (currentHealth > 0f && currentHealth < 0.01f)
            currentHealth = Mathf.Min(0.01f, maxHealth);
        return true;
    }

    public bool TryRemoveSpeedBuff(float multiplier)
    {
        if (speedBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        speedBuffStacks--;
        CacheBaseMoveSpeed();
        moveSpeedMultiplier /= multiplier;
        RefreshMoveSpeed();
        return true;
    }

    public bool TryRemoveFireRateBuff(float multiplier)
    {
        if (fireRateBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        fireRateBuffStacks--;
        fireRateMultiplier /= multiplier;
        return true;
    }

    public bool CanReceiveDamageBuff(int maxStacks) =>
        damageBuffStacks < Mathf.Max(1, maxStacks);

    public bool CanReceiveMaxHealthBuff(int maxStacks) =>
        healthBuffStacks < Mathf.Max(1, maxStacks);

    public bool CanReceiveSpeedBuff(int maxStacks) =>
        speedBuffStacks < Mathf.Max(1, maxStacks);

    public bool CanReceiveFireRateBuff(int maxStacks) =>
        SupportsFireRateBuff && fireRateBuffStacks < Mathf.Max(1, maxStacks);

    public virtual bool CanReceiveForkSpawnIntervalBuff(int maxStacks) => false;
    public virtual bool CanReceiveMaxStoredDamageBuff(int maxStacks) => false;
    public virtual bool CanReceiveDodgeDistanceBuff(int maxStacks) => false;
    public virtual bool CanReceiveDodgeCooldownBuff(int maxStacks) => false;

    public virtual bool TryApplyForkSpawnIntervalBuff(float multiplier, int maxStacks) => false;
    public virtual bool TryApplyMaxStoredDamageBuff(float multiplier, int maxStacks) => false;
    public virtual bool TryApplyDodgeDistanceBuff(float multiplier, int maxStacks) => false;
    public virtual bool TryApplyDodgeCooldownBuff(float multiplier, int maxStacks) => false;

    public virtual bool TryRemoveForkSpawnIntervalBuff(float multiplier) => false;
    public virtual bool TryRemoveMaxStoredDamageBuff(float multiplier) => false;
    public virtual bool TryRemoveDodgeDistanceBuff(float multiplier) => false;
    public virtual bool TryRemoveDodgeCooldownBuff(float multiplier) => false;

    /// <summary>
    /// Shooting enemies override this so Overclock can buff their fire rate.
    /// </summary>
    protected virtual bool SupportsFireRateBuff => false;

    /// <summary>
    /// Converts a base fire interval into the current buffed interval (higher multiplier = faster shots).
    /// </summary>
    protected float ScaleFireInterval(float baseInterval)
    {
        return baseInterval / Mathf.Max(0.01f, fireRateMultiplier);
    }

    public float GetBaseMoveSpeed()
    {
        CacheBaseMoveSpeed();
        return baseMoveSpeed;
    }

    public float GetCurrentMoveSpeed()
    {
        CacheBaseMoveSpeed();
        return baseMoveSpeed * moveSpeedMultiplier;
    }

    public void RefreshMoveSpeed()
    {
        if (Agent == null)
            return;

        CacheBaseMoveSpeed();
        Agent.speed = baseMoveSpeed * moveSpeedMultiplier;
    }

    public bool IsAlliedWith(EnemyAI other)
    {
        if (other == null || other == this || !other.IsAlive)
            return false;

        if (boundEncounter == null || other.boundEncounter == null)
            return true;

        return boundEncounter == other.boundEncounter;
    }

    public void BindEncounter(RoomEncounter encounter)
    {
        boundEncounter = encounter;
    }

    protected virtual void Die()
    {
        InfectionReport.RecordEnemyKill();
        SpawnDeathEffect();
        Destroy(gameObject);
    }

    protected void SpawnDeathEffect()
    {
        if (deathEffectPrefab == null)
            return;

        Vector3 spawnPoint = transform.position + Vector3.up * deathEffectHeight;
        GameObject effect = Instantiate(deathEffectPrefab, spawnPoint, Quaternion.identity);
        Destroy(effect, DeathEffect.GetPoolLifetime(effect));
    }

    protected virtual void OnDestroy()
    {
        boundEncounter?.NotifyEnemyDestroyed(this);
        boundEncounter = null;
    }

    protected void FindPlayer()
    {
        PlayerController nearest = PlayerRegistry.GetNearestLiving(transform.position);
        if (nearest != null)
        {
            player = nearest.transform;
            return;
        }

        player = null;
        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null)
        {
            PlayerController taggedPlayer = tagged.GetComponentInParent<PlayerController>();
            if (taggedPlayer == null || !taggedPlayer.IsDead)
                player = tagged.transform;
        }
    }

    protected void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            Agent.angularSpeed * Time.deltaTime);
    }

    void CacheBaseMoveSpeed()
    {
        if (baseMoveSpeed >= 0f || Agent == null)
            return;

        baseMoveSpeed = Agent.speed;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (Agent == null)
            Agent = GetComponent<NavMeshAgent>();

        if (Agent != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, Agent.stoppingDistance);

            if (Agent.hasPath)
            {
                Gizmos.color = Color.cyan;
                var path = Agent.path.corners;
                for (int i = 0; i < path.Length - 1; i++)
                    Gizmos.DrawLine(path[i], path[i + 1]);
            }
        }
    }
#endif
}
