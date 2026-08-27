using UnityEngine;

/// <summary>
/// Projectile that ricochets off walls a limited number of times before vanishing.
/// </summary>
public class BounceProjectile : Projectile
{
    [Header("Bounce")]
    [SerializeField] int maxBounces = 3;
    [SerializeField] float bounceOffset = 0.2f;

    int bouncesRemaining;
    Collider ignoredWall;

    protected override void OnEnable()
    {
        base.OnEnable();
        bouncesRemaining = Mathf.Max(0, maxBounces);
        ignoredWall = null;
    }

    protected override void HitObstacle(Collider other)
    {
        if (other == ignoredWall)
            return;

        if (bouncesRemaining <= 0)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 normal = GetBounceNormal(other);
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.001f)
        {
            Destroy(gameObject);
            return;
        }

        normal.Normalize();

        Vector3 reflected = Vector3.Reflect(direction, normal);
        reflected.y = 0f;
        if (reflected.sqrMagnitude < 0.001f)
            reflected = Vector3.Cross(normal, Vector3.up);

        if (reflected.sqrMagnitude < 0.001f)
        {
            Destroy(gameObject);
            return;
        }

        direction = reflected.normalized;
        transform.position += normal * bounceOffset;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        ignoredWall = other;
        bouncesRemaining--;
    }

    void OnTriggerExit(Collider other)
    {
        if (other == ignoredWall)
            ignoredWall = null;
    }

    Vector3 GetBounceNormal(Collider other)
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
