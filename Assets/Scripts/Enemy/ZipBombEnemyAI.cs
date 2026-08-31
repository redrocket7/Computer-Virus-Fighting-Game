using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Absorbs player damage into a growing bomb body. When storage is full, the next hit detonates.
/// </summary>
public class ZipBombEnemyAI : EnemyAI
{
    [Header("Zip Bomb Storage")]
    [Tooltip("Total absorbed damage before the bomb is full.")]
    [SerializeField] float maxStoredDamage = 12f;
    [Tooltip("Optional VFX when damage is absorbed.")]
    [SerializeField] ParticleSystem absorbEffect;

    [Header("Explosion")]
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] float triggerDistance = 2f;
    [SerializeField] float fuseDuration = 0.85f;
    [SerializeField] float explosionRadius = 6f;
    [SerializeField] float baseExplosionDamage = 2f;
    [Tooltip("Adds this much explosion damage per point of stored damage.")]
    [SerializeField] float storedDamageToExplosion = 0.35f;
    [SerializeField] LayerMask damageMask = ~0;
    [SerializeField] float explosionShakeDuration = 0.45f;
    [SerializeField] float explosionShakeStrength = 0.65f;
    [SerializeField] float explosionShakeRotation = 3.5f;

    [Header("Growth Visuals")]
    [SerializeField] Transform bodyMesh;
    [SerializeField] Vector3 maxBodyScale = new Vector3(2.4f, 2.4f, 2.4f);
    [SerializeField] float maxBodyLift = 0.75f;

    static readonly Collider[] OverlapHits = new Collider[48];
    static readonly HashSet<IDamageable> DamagedBuffer = new HashSet<IDamageable>();
    static readonly List<EnemyAI> NearbyEnemies = new List<EnemyAI>();

    Vector3 baseBodyLocalScale;
    Vector3 baseBodyLocalPosition;

    float storedDamage;
    bool hasExploded;
    bool isFused;
    float fuseRemaining;
    bool visualsInitialized;

    public float StoredDamage => storedDamage;
    public float MaxStoredDamage => maxStoredDamage;
    public bool IsFull => storedDamage >= Mathf.Max(0.01f, maxStoredDamage) - 0.001f;

    protected override void Awake()
    {
        base.Awake();
        CacheVisualBaselines();
    }

    void CacheVisualBaselines()
    {
        if (bodyMesh != null)
        {
            baseBodyLocalScale = bodyMesh.localScale;
            baseBodyLocalPosition = bodyMesh.localPosition;
        }

        visualsInitialized = bodyMesh != null;
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

        base.Update();

        if (Player == null || !CanAct)
            return;

        Vector3 offset = Player.position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude <= triggerDistance * triggerDistance)
            ArmFuse();
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

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
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
