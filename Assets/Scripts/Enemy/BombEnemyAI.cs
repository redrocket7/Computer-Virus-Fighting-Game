using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chases normally, then arms a short fuse when close to the player or when damaged.
/// While fused it grows, then detonates.
/// </summary>
public class BombEnemyAI : EnemyAI
{
    [Header("Bomb")]
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] float triggerDistance = 2f;
    [SerializeField] float explosionRadius = 4f;
    [SerializeField] float explosionDamage = 2f;
    [SerializeField] LayerMask damageMask = ~0;

    [Header("Fuse Telegraph")]
    [SerializeField] float fuseDuration = 0.85f;
    [SerializeField] float damagedFuseDuration = 0.35f;
    [SerializeField] float growScaleMultiplier = 1.45f;

    [Header("Explosion Feedback")]
    [SerializeField] float explosionShakeDuration = 0.4f;
    [SerializeField] float explosionShakeStrength = 0.55f;
    [SerializeField] float explosionShakeRotation = 3f;

    static readonly Collider[] overlapHits = new Collider[48];
    static readonly HashSet<IDamageable> damagedBuffer = new HashSet<IDamageable>();

    bool hasExploded;
    bool isFused;
    float fuseRemaining;
    float fuseTotal;
    Vector3 baseScale;

    protected override void Awake()
    {
        base.Awake();
        baseScale = transform.localScale;
    }

    protected override void Update()
    {
        if (hasExploded)
            return;

        if (isFused)
        {
            TickFuse();
            return;
        }

        base.Update();

        if (Player == null || !CanAct)
            return;

        Vector3 offset = Player.position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude <= triggerDistance * triggerDistance)
            Arm(fuseDuration);
    }

    public override void TakeDamage(float amount)
    {
        if (amount <= 0f || hasExploded)
            return;

        // Shield must absorb hits before the fuse is armed.
        if (HasActiveShield)
        {
            base.TakeDamage(amount);
            return;
        }

        Arm(damagedFuseDuration);
    }

    void Arm(float duration)
    {
        if (hasExploded || isFused)
            return;

        isFused = true;
        fuseTotal = Mathf.Max(0.05f, duration);
        fuseRemaining = fuseTotal;

        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.isStopped = true;
            if (Agent.hasPath)
                Agent.ResetPath();
        }
    }

    void TickFuse()
    {
        fuseRemaining -= Time.deltaTime;
        float progress = 1f - Mathf.Clamp01(fuseRemaining / fuseTotal);

        float scale = Mathf.Lerp(1f, growScaleMultiplier, progress * progress);
        transform.localScale = baseScale * scale;

        if (fuseRemaining <= 0f)
            Explode();
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        isFused = false;
        transform.localScale = baseScale;

        if (Agent != null && Agent.isOnNavMesh)
            Agent.isStopped = true;

        ScreenShake.Shake(
            explosionShakeDuration,
            explosionShakeStrength,
            explosionShakeRotation);

        if (explosionPrefab != null)
        {
            GameObject effect = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            float lifetime = ExplosionEffect.GetPoolLifetime(effect);
            Destroy(effect, lifetime);
        }

        damagedBuffer.Clear();
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            explosionRadius,
            overlapHits,
            damageMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null)
                continue;

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null || ReferenceEquals(target, this) || !damagedBuffer.Add(target))
                continue;

            DelayedExplosionDamage.Apply(
                target,
                ScaleOutgoingDamage(explosionDamage),
                transform.position,
                explosionRadius);
        }

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, triggerDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
#endif
}
