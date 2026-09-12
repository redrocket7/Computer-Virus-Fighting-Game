using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Zip-bomb-like sponge with unlimited damage storage.
/// Grows as it absorbs hits and can still proximity-fuse into an explosion.
/// Tries to flee the player instead of chasing.
/// </summary>
public class DamageContainerEnemyAI : EnemyAI
{
    [Header("Damage Storage")]
    [Tooltip("Stored damage used for growth visuals (asymptotic toward max scale).")]
    [SerializeField] float growthReferenceDamage = 12f;
    [SerializeField] ParticleSystem absorbEffect;

    [Header("Flee")]
    [SerializeField] float fleeDistance = 7f;
    [SerializeField] float fleeSampleRadius = 8f;

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

    protected override void Awake()
    {
        base.Awake();
        CacheVisualBaselines();
    }

    /// <summary>Path away from the player so containers are harder to proximity-fuse.</summary>
    protected override Vector3 GetChaseDestination(Transform chaseTarget)
    {
        if (chaseTarget == null)
            return transform.position;

        Vector3 away = transform.position - chaseTarget.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = Random.insideUnitSphere;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = Vector3.forward;

        Vector3 desired = transform.position + away.normalized * Mathf.Max(1f, fleeDistance);
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(1f, fleeSampleRadius), NavMesh.AllAreas))
            return hit.position;

        return transform.position;
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
    }

    void ApplyGrowthVisuals()
    {
        if (!visualsInitialized)
            CacheVisualBaselines();
        if (!visualsInitialized)
            return;

        float reference = Mathf.Max(0.01f, growthReferenceDamage);
        float fill = 1f - (1f / (1f + storedDamage / reference));

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

            DelayedExplosionDamage.Apply(target, damage, center, explosionRadius);
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

            DelayedExplosionDamage.Apply(enemy, damage, center, explosionRadius);
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
