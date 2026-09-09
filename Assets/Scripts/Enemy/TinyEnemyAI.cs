using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small chase enemy that prefers to stick near other tinies in packs of ~3–4.
/// </summary>
public class TinyEnemyAI : EnemyAI
{
    [Header("Pack")]
    [SerializeField] float packSearchRadius = 10f;
    [SerializeField] float preferredPackSpacing = 2.2f;
    [SerializeField] float separationDistance = 1.1f;
    [SerializeField] float cohesionWeight = 2.4f;
    [SerializeField] float separationWeight = 1.6f;
    [SerializeField] float packPullClamp = 3.5f;
    [SerializeField, Min(1)] int minPackSize = 3;
    [SerializeField, Min(1)] int idealPackSize = 4;

    static readonly List<TinyEnemyAI> NearbyTinies = new List<TinyEnemyAI>(16);
    static readonly Collider[] OverlapHits = new Collider[24];

    protected override Vector3 GetChaseDestination(Transform chaseTarget)
    {
        Vector3 target = chaseTarget.position;
        Vector3 packOffset = ComputePackOffset();
        return target + packOffset;
    }

    Vector3 ComputePackOffset()
    {
        CollectNearbyTinies(NearbyTinies);
        if (NearbyTinies.Count == 0)
            return Vector3.zero;

        Vector3 cohesion = Vector3.zero;
        Vector3 separation = Vector3.zero;
        int cohesionCount = 0;
        int separationCount = 0;

        Vector3 myPosition = transform.position;
        for (int i = 0; i < NearbyTinies.Count; i++)
        {
            TinyEnemyAI ally = NearbyTinies[i];
            if (ally == null)
                continue;

            Vector3 offset = ally.transform.position - myPosition;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < 0.01f)
                continue;

            cohesion += offset;
            cohesionCount++;

            if (distance < separationDistance)
            {
                separation -= offset / distance;
                separationCount++;
            }
        }

        Vector3 pull = Vector3.zero;
        if (cohesionCount > 0)
        {
            Vector3 toCentroid = cohesion / cohesionCount;
            float centroidDistance = toCentroid.magnitude;

            // Pull harder when below the preferred pack size.
            int packCount = NearbyTinies.Count + 1;
            int minSize = Mathf.Max(1, minPackSize);
            int idealSize = Mathf.Max(minSize, idealPackSize);
            float lonelyBoost = packCount < minSize ? 1.6f : packCount < idealSize ? 1.35f : 1f;
            if (centroidDistance > preferredPackSpacing * 0.35f)
                pull += toCentroid.normalized * (cohesionWeight * lonelyBoost);
        }

        if (separationCount > 0)
            pull += (separation / separationCount) * separationWeight;

        pull.y = 0f;
        if (pull.sqrMagnitude > packPullClamp * packPullClamp)
            pull = pull.normalized * packPullClamp;

        return pull;
    }

    void CollectNearbyTinies(List<TinyEnemyAI> results)
    {
        results.Clear();

        if (BoundEncounter != null)
        {
            BoundEncounter.GetLivingEnemies(AllyBuffer);
            for (int i = 0; i < AllyBuffer.Count; i++)
            {
                if (AllyBuffer[i] is TinyEnemyAI tiny &&
                    tiny != this &&
                    tiny.IsAlive &&
                    IsWithinPackRange(tiny.transform.position))
                {
                    results.Add(tiny);
                }
            }

            return;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            packSearchRadius,
            OverlapHits,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null)
                continue;

            TinyEnemyAI tiny = hit.GetComponentInParent<TinyEnemyAI>();
            if (tiny == null || tiny == this || !tiny.IsAlive || results.Contains(tiny))
                continue;

            results.Add(tiny);
        }
    }

    bool IsWithinPackRange(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= packSearchRadius * packSearchRadius;
    }

    static readonly List<EnemyAI> AllyBuffer = new List<EnemyAI>(32);

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.45f, 0.95f, 0.55f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, packSearchRadius);
        Gizmos.color = new Color(0.95f, 0.85f, 0.25f, 0.55f);
        Gizmos.DrawWireSphere(transform.position, preferredPackSpacing);
    }
#endif
}
