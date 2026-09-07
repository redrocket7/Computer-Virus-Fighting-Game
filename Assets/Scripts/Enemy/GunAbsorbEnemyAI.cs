using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>
/// Absorbs player bullet damage, can spend that storage to fire charged shots,
/// and detonates when storage is full (or on proximity fuse).
/// </summary>
public class GunAbsorbEnemyAI : EnemyAI
{
    [Header("Absorb Storage")]
    [Tooltip("Total absorbed damage before the enemy detonates.")]
    [SerializeField] float maxStoredDamage = 12f;
    [SerializeField] ParticleSystem absorbEffect;

    [Header("Ranged Movement")]
    [SerializeField] float preferredRange = 12f;
    [SerializeField] float retreatRange = 7f;
    [SerializeField] float retreatDistance = 5f;
    [SerializeField] float repathInterval = 0.2f;

    [Header("Shooting")]
    [FormerlySerializedAs("projectilePrefab")]
    [SerializeField] AbsorbEnemyBullet absorbProjectilePrefab;
    [Tooltip("Used when the enemy has no absorbed damage to spend.")]
    [SerializeField] Projectile normalProjectilePrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float projectileHeight = 0.75f;
    [SerializeField] float shootingRange = 18f;
    [SerializeField] float fireInterval = 1.35f;
    [Tooltip("Minimum stored damage required before a charged absorb shot can be fired.")]
    [SerializeField] float minStoredToFire = 1f;
    [Tooltip("Maximum absorbed damage spent into a single absorb bullet.")]
    [SerializeField] float maxDamagePerShot = 4f;
    [SerializeField] LayerMask lineOfSightMask = ~0;

    [Header("Muzzle Recoil")]
    [SerializeField] float recoilDistance = 0.35f;
    [SerializeField] float recoilRecoverTime = 0.12f;

    [Header("Explosion")]
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] float triggerDistance = 2f;
    [SerializeField] float fuseDuration = 0.85f;
    [SerializeField] float explosionRadius = 6f;
    [SerializeField] float baseExplosionDamage = 2f;
    [SerializeField] float storedDamageToExplosion = 0.35f;
    [SerializeField] LayerMask damageMask = ~0;
    [SerializeField] float explosionShakeDuration = 0.45f;
    [SerializeField] float explosionShakeStrength = 0.65f;
    [SerializeField] float explosionShakeRotation = 3.5f;

    [Header("Growth Visuals")]
    [SerializeField] Transform bodyMesh;
    [SerializeField] Vector3 maxBodyScale = new Vector3(2.2f, 2.2f, 2.2f);
    [SerializeField] float maxBodyLift = 0.6f;

    static readonly Collider[] OverlapHits = new Collider[48];
    static readonly HashSet<IDamageable> DamagedBuffer = new HashSet<IDamageable>();
    static readonly List<EnemyAI> NearbyEnemies = new List<EnemyAI>();

    Vector3 baseBodyLocalScale;
    Vector3 baseBodyLocalPosition;

    float storedDamage;
    float fireTimer;
    float repathTimer;
    bool hasExploded;
    bool isFused;
    float fuseRemaining;
    bool visualsInitialized;
    int maxStoredDamageBuffStacks;
    readonly EnemyMuzzleRecoil muzzleRecoil = new EnemyMuzzleRecoil();

    public float StoredDamage => storedDamage;
    public float MaxStoredDamage => maxStoredDamage;
    public bool IsFull => storedDamage >= Mathf.Max(0.01f, maxStoredDamage) - 0.001f;

    public override bool CanReceiveMaxStoredDamageBuff(int maxStacks) =>
        maxStoredDamageBuffStacks < Mathf.Max(1, maxStacks);

    public override bool TryApplyMaxStoredDamageBuff(float multiplier, int maxStacks)
    {
        if (multiplier <= 0f || maxStoredDamageBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        maxStoredDamageBuffStacks++;
        maxStoredDamage *= multiplier;
        ApplyGrowthVisuals();
        return true;
    }

    public override bool TryRemoveMaxStoredDamageBuff(float multiplier)
    {
        if (maxStoredDamageBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        maxStoredDamageBuffStacks--;
        maxStoredDamage /= multiplier;
        if (storedDamage > maxStoredDamage)
            storedDamage = maxStoredDamage;
        ApplyGrowthVisuals();
        return true;
    }

    protected override void Awake()
    {
        base.Awake();
        CacheVisualBaselines();
    }

    protected override void Start()
    {
        base.Start();
        muzzleRecoil.Bind(firePoint);
    }

    protected override bool SupportsFireRateBuff => true;

    void CacheVisualBaselines()
    {
        if (bodyMesh != null)
        {
            baseBodyLocalScale = bodyMesh.localScale;
            baseBodyLocalPosition = bodyMesh.localPosition;
        }

        visualsInitialized = bodyMesh != null;
    }

    protected virtual void LateUpdate()
    {
        muzzleRecoil.Tick(Time.deltaTime);
    }

    protected override void Update()
    {
        if (hasExploded)
            return;

        if (isFused)
        {
            fuseRemaining -= Time.deltaTime;
            if (fuseRemaining <= 0f)
                Explode();
            return;
        }

        TickHoldoff();
        if (!CanAct)
            return;

        if (Player == null)
        {
            base.Update();
            return;
        }

        if (fireTimer > 0f)
            fireTimer -= Time.deltaTime;
        if (repathTimer > 0f)
            repathTimer -= Time.deltaTime;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        UpdateRangedMovement(toPlayer, distance);
        FaceDirection(toPlayer);

        if (distance <= triggerDistance)
        {
            ArmFuse();
            return;
        }

        if (fireTimer <= 0f &&
            distance <= shootingRange &&
            CanFireProjectile() &&
            HasLineOfSight())
        {
            Shoot(toPlayer);
        }
    }

    bool CanFireProjectile()
    {
        if (storedDamage >= minStoredToFire && absorbProjectilePrefab != null)
            return true;

        return normalProjectilePrefab != null;
    }

    void UpdateRangedMovement(Vector3 toPlayer, float distance)
    {
        if (!Agent.isOnNavMesh)
            return;

        if (distance > preferredRange)
        {
            SetDestination(Player.position);
            return;
        }

        if (distance < retreatRange && toPlayer.sqrMagnitude > 0.001f)
        {
            Vector3 desired = transform.position - toPlayer.normalized * retreatDistance;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, retreatDistance, NavMesh.AllAreas))
                SetDestination(hit.position);
            else
                StopAgentPath();
            return;
        }

        StopAgentPath();
    }

    void SetDestination(Vector3 destination)
    {
        if (repathTimer > 0f)
            return;

        repathTimer = repathInterval;
        Agent.isStopped = false;
        Agent.SetDestination(destination);
    }

    bool HasLineOfSight()
    {
        Vector3 origin = GetMuzzlePosition();
        Vector3 target = Player.position + Vector3.up * 0.5f;
        Vector3 ray = target - origin;

        if (!Physics.Raycast(
                origin,
                ray.normalized,
                out RaycastHit hit,
                ray.magnitude,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return hit.collider.GetComponentInParent<PlayerController>() != null;
    }

    void Shoot(Vector3 flatDirection)
    {
        if (flatDirection.sqrMagnitude < 0.001f)
            return;

        flatDirection.Normalize();
        Vector3 spawnPosition = GetMuzzlePosition();
        Quaternion rotation = Quaternion.LookRotation(flatDirection, Vector3.up);

        if (storedDamage >= minStoredToFire && absorbProjectilePrefab != null)
        {
            float spent = Mathf.Min(storedDamage, Mathf.Max(minStoredToFire, maxDamagePerShot));
            if (spent >= minStoredToFire - 0.001f)
            {
                storedDamage = Mathf.Max(0f, storedDamage - spent);
                ApplyGrowthVisuals();

                AbsorbEnemyBullet absorbShot = Instantiate(absorbProjectilePrefab, spawnPosition, rotation);
                absorbShot.LaunchAbsorbed(flatDirection, transform, spent);
                fireTimer = ScaleFireInterval(fireInterval);
                muzzleRecoil.Play(recoilDistance, recoilRecoverTime);
                return;
            }
        }

        if (normalProjectilePrefab == null)
            return;

        Projectile normalShot = Instantiate(normalProjectilePrefab, spawnPosition, rotation);
        normalShot.LaunchAsEnemy(flatDirection, transform);
        fireTimer = ScaleFireInterval(fireInterval);
        muzzleRecoil.Play(recoilDistance, recoilRecoverTime);
    }

    void ArmFuse()
    {
        if (hasExploded || isFused)
            return;

        isFused = true;
        fuseRemaining = Mathf.Max(0.05f, fuseDuration);

        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.isStopped = true;
            if (Agent.hasPath)
                Agent.ResetPath();
        }
    }

    public override void TakeDamage(float amount)
    {
        if (amount <= 0f || !IsAlive || hasExploded)
            return;

        if (HasActiveShield)
        {
            base.TakeDamage(amount);
            return;
        }

        storedDamage += amount;
        ApplyGrowthVisuals();

        if (absorbEffect != null)
        {
            absorbEffect.transform.position = bodyMesh != null
                ? bodyMesh.position
                : transform.position + Vector3.up * 0.75f;
            absorbEffect.Play();
        }

        if (storedDamage >= Mathf.Max(0.01f, maxStoredDamage))
            Explode();
    }

    void ApplyGrowthVisuals()
    {
        if (!visualsInitialized)
            CacheVisualBaselines();
        if (!visualsInitialized)
            return;

        float fill = Mathf.Clamp01(storedDamage / Mathf.Max(0.01f, maxStoredDamage));
        bodyMesh.localScale = Vector3.Lerp(baseBodyLocalScale, maxBodyScale, fill);
        bodyMesh.localPosition = baseBodyLocalPosition + Vector3.up * (maxBodyLift * fill);
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;

        if (Agent != null && Agent.isOnNavMesh)
            Agent.isStopped = true;

        float totalDamage = ScaleOutgoingDamage(
            baseExplosionDamage + storedDamage * storedDamageToExplosion);

        ScreenShake.Shake(
            explosionShakeDuration,
            explosionShakeStrength,
            explosionShakeRotation);

        if (explosionPrefab != null)
        {
            GameObject effect = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(effect, ExplosionEffect.GetPoolLifetime(effect));
        }

        ApplyExplosionDamage(totalDamage);
        Destroy(gameObject);
    }

    void ApplyExplosionDamage(float damage)
    {
        DamagedBuffer.Clear();
        Vector3 center = transform.position;
        float radiusSq = explosionRadius * explosionRadius;

        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            explosionRadius,
            OverlapHits,
            damageMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null)
                continue;

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null || ReferenceEquals(target, this) || !DamagedBuffer.Add(target))
                continue;

            target.TakeDamage(damage);
        }

        if (boundEncounter == null)
            return;

        NearbyEnemies.Clear();
        boundEncounter.GetLivingEnemies(NearbyEnemies);

        for (int i = 0; i < NearbyEnemies.Count; i++)
        {
            EnemyAI enemy = NearbyEnemies[i];
            if (enemy == null || ReferenceEquals(enemy, this))
                continue;

            Vector3 offset = enemy.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude > radiusSq)
                continue;

            if (!DamagedBuffer.Add(enemy))
                continue;

            enemy.TakeDamage(damage);
        }
    }

    Vector3 GetMuzzlePosition()
    {
        if (firePoint != null)
            return firePoint.position;

        Vector3 forward = Player != null ? Player.position - transform.position : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = transform.forward;

        return transform.position +
               Vector3.up * projectileHeight +
               forward.normalized * (Agent.radius + 0.6f);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, preferredRange);
        Gizmos.color = new Color(1f, 0.45f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, retreatRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, shootingRange);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, triggerDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            CacheVisualBaselines();
    }
#endif
}
