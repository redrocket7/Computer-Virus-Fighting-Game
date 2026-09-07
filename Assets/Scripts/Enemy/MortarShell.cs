using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lobbed explosive shell that travels in an arc and detonates on landing or impact.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MortarShell : MonoBehaviour
{
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] float explosionRadius = 4f;
    [SerializeField] float explosionDamage = 2f;
    [SerializeField] LayerMask damageMask = ~0;
    [SerializeField] LayerMask impactMask = ~0;
    [SerializeField] float groundSnapHeight = 0.15f;
    [SerializeField] float shakeDuration = 0.35f;
    [SerializeField] float shakeStrength = 0.45f;
    [SerializeField] float shakeRotation = 2.5f;

    static readonly Collider[] overlapHits = new Collider[48];
    static readonly HashSet<IDamageable> damagedBuffer = new HashSet<IDamageable>();

    Transform owner;
    Vector3 start;
    Vector3 end;
    float arcHeight;
    float duration;
    float elapsed;
    bool launched;
    bool detonated;

    public void Launch(Vector3 target, Transform source, float height, float flightTime)
    {
        owner = source;
        start = transform.position;
        end = target;
        arcHeight = Mathf.Max(0.5f, height);
        duration = Mathf.Max(0.15f, flightTime);
        elapsed = 0f;
        launched = true;
        detonated = false;
    }

    void Update()
    {
        if (!launched || detonated)
            return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        Vector3 next = Vector3.Lerp(start, end, t);
        next.y += 4f * arcHeight * t * (1f - t);

        Vector3 delta = next - transform.position;
        if (delta.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

        transform.position = next;

        if (t >= 1f)
            Detonate(end);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!launched || detonated)
            return;

        if (((1 << other.gameObject.layer) & impactMask) == 0)
            return;

        if (owner != null &&
            (other.transform == owner || other.transform.IsChildOf(owner)))
            return;

        if (other.GetComponent<MortarShell>() != null || other.GetComponent<Projectile>() != null)
            return;

        // Pass through room triggers and open doors.
        if (other.isTrigger && other.GetComponentInParent<PlayerController>() == null)
            return;

        Detonate(transform.position);
    }

    void Detonate(Vector3 position)
    {
        if (detonated)
            return;

        detonated = true;
        launched = false;

        Vector3 blastPoint = position;
        if (Physics.Raycast(
                position + Vector3.up,
                Vector3.down,
                out RaycastHit groundHit,
                4f,
                impactMask,
                QueryTriggerInteraction.Ignore))
        {
            blastPoint = groundHit.point + Vector3.up * groundSnapHeight;
        }

        ScreenShake.Shake(shakeDuration, shakeStrength, shakeRotation);

        if (explosionPrefab != null)
        {
            GameObject effect = Instantiate(explosionPrefab, blastPoint, Quaternion.identity);
            Destroy(effect, ExplosionEffect.GetPoolLifetime(effect));
        }

        damagedBuffer.Clear();
        int hitCount = Physics.OverlapSphereNonAlloc(
            blastPoint,
            explosionRadius,
            overlapHits,
            damageMask,
            QueryTriggerInteraction.Collide);

        EnemyAI ownerAi = owner != null ? owner.GetComponentInParent<EnemyAI>() : null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null)
                continue;

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null || !damagedBuffer.Add(target))
                continue;

            // Enemy mortar shells only hurt the player.
            if (target is EnemyAI)
                continue;

            float scaledDamage = explosionDamage;
            if (ownerAi != null)
                scaledDamage = ownerAi.ScaleOutgoingDamage(explosionDamage);

            DelayedExplosionDamage.Apply(target, scaledDamage, blastPoint, explosionRadius);
        }

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
#endif
}
