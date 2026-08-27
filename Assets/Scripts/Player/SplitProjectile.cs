using UnityEngine;

/// <summary>
/// High-damage projectile that breaks into weaker fragments on wall impact.
/// Hits on damageables (player/enemies) deal damage and destroy without splitting.
/// </summary>
public class SplitProjectile : Projectile
{
    [Header("Split")]
    [SerializeField] Projectile fragmentPrefab;
    [SerializeField] int fragmentCount = 3;
    [SerializeField] float spreadAngle = 55f;
    [SerializeField] float spawnOffset = 0.28f;
    [Tooltip("If false, this shot never splits (use on fragment prefabs that reuse this script).")]
    [SerializeField] bool splitOnWallHit = true;

    bool hasSplit;

    protected override void HitObstacle(Collider other)
    {
        if (!splitOnWallHit || hasSplit || fragmentPrefab == null || fragmentCount <= 0)
        {
            Destroy(gameObject);
            return;
        }

        hasSplit = true;

        Vector3 normal = GetImpactNormal(other);
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.001f)
            normal = -direction;
        normal.Normalize();

        Vector3 reflected = Vector3.Reflect(direction, normal);
        reflected.y = 0f;
        if (reflected.sqrMagnitude < 0.001f)
            reflected = normal;
        reflected.Normalize();

        Vector3 spawnPos = transform.position + normal * spawnOffset;
        float halfSpread = spreadAngle * 0.5f;

        for (int i = 0; i < fragmentCount; i++)
        {
            float t = fragmentCount == 1 ? 0.5f : i / (float)(fragmentCount - 1);
            float angle = Mathf.Lerp(-halfSpread, halfSpread, t);
            Vector3 fragmentDir = Quaternion.AngleAxis(angle, Vector3.up) * reflected;
            fragmentDir.y = 0f;
            if (fragmentDir.sqrMagnitude < 0.001f)
                fragmentDir = reflected;
            fragmentDir.Normalize();

            Projectile fragment = Instantiate(
                fragmentPrefab,
                spawnPos,
                Quaternion.LookRotation(fragmentDir, Vector3.up));

            if (FiredByEnemy)
                fragment.LaunchAsEnemy(fragmentDir, Owner);
            else
                fragment.Launch(fragmentDir, Owner);
        }

        Destroy(gameObject);
    }

    Vector3 GetImpactNormal(Collider other)
    {
        Vector3 origin = transform.position - direction * 0.75f;
        if (Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                1.75f,
                HitMask,
                QueryTriggerInteraction.Ignore) &&
            hit.collider == other)
        {
            return hit.normal;
        }

        Vector3 closest = other.ClosestPoint(transform.position);
        Vector3 fallback = transform.position - closest;
        fallback.y = 0f;
        if (fallback.sqrMagnitude > 0.0001f)
            return fallback;

        return -direction;
    }
}
