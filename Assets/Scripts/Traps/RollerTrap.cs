using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Giant rolling cylinder that moves back and forth and reverses when it hits a wall.
/// Attach to the trap root. The mesh child is visual-only.
/// Body may be a CapsuleCollider or MeshCollider (or any other Collider).
/// </summary>
public class RollerTrap : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] Vector3 localMoveDirection = Vector3.right;
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float wallBounceCooldown = 0.08f;
    [SerializeField] LayerMask wallMask = ~0;
    [SerializeField] float castSkin = 0.05f;

    [Header("Body")]
    [SerializeField] Transform rollVisual;
    [Tooltip("Capsule, mesh, or any other collider used for wall casts and contact.")]
    [SerializeField] Collider bodyCollider;

    [Header("Roll Visual")]
    [SerializeField] float visualRollMultiplier = 1f;

    [Header("Contact Damage")]
    [SerializeField] float contactDamage = 1f;
    [SerializeField] float damageCooldown = 0.6f;

    [Header("Enemy Push")]
    [SerializeField] bool pushEnemies = true;
    [SerializeField] float enemyPushSampleDistance = 2f;

    [Header("Room Activation")]
    [Tooltip("When enabled, the roller stays locked in place until the parent room encounter begins.")]
    [SerializeField] bool waitForRoomEncounter = true;

    static readonly Collider[] OverlapHits = new Collider[12];
    static readonly HashSet<EnemyAI> PushedEnemies = new HashSet<EnemyAI>();

    RoomEncounter roomEncounter;
    CapsuleCollider bodyCapsule;
    Vector3 homeLocalPosition;
    Vector3 worldDirection;
    float bounceCooldownTimer;
    float damageTimer;
    float rollRadius = 1f;
    bool isActive;
    bool subscribedToEncounter;

    void Awake()
    {
        ResolveBodyCollider();

        if (rollVisual == null)
        {
            MeshRenderer renderer = GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
                rollVisual = renderer.transform;
        }

        homeLocalPosition = transform.localPosition;
        CacheRollRadius();
        worldDirection = GetFlattenedWorldDirection(localMoveDirection);
        if (worldDirection.sqrMagnitude < 0.0001f)
            worldDirection = Vector3.right;

        roomEncounter = GetComponentInParent<RoomEncounter>();
    }

    void ResolveBodyCollider()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider>();

        if (bodyCollider == null)
            bodyCollider = GetComponent<MeshCollider>();

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider>();

        if (bodyCollider == null)
            bodyCollider = GetComponentInChildren<CapsuleCollider>();

        if (bodyCollider == null)
            bodyCollider = GetComponentInChildren<MeshCollider>();

        if (bodyCollider == null)
            bodyCollider = GetComponentInChildren<Collider>();

        bodyCapsule = bodyCollider as CapsuleCollider;
    }

    void OnEnable()
    {
        TrySubscribeToEncounter();
        ApplyActivationState(ShouldStartActive());
    }

    void Start()
    {
        TrySubscribeToEncounter();
        ApplyActivationState(ShouldStartActive());
    }

    void OnDisable()
    {
        UnsubscribeFromEncounter();
    }

    bool ShouldStartActive()
    {
        if (!waitForRoomEncounter || roomEncounter == null)
            return true;

        return roomEncounter.IsInProgress;
    }

    void TrySubscribeToEncounter()
    {
        if (!waitForRoomEncounter || roomEncounter == null || subscribedToEncounter)
            return;

        roomEncounter.Started += Activate;
        subscribedToEncounter = true;
    }

    void UnsubscribeFromEncounter()
    {
        if (!subscribedToEncounter || roomEncounter == null)
            return;

        roomEncounter.Started -= Activate;
        subscribedToEncounter = false;
    }

    public void Activate()
    {
        if (isActive)
            return;

        isActive = true;
    }

    public void Deactivate()
    {
        isActive = false;
        bounceCooldownTimer = 0f;

        transform.localPosition = homeLocalPosition;
        Physics.SyncTransforms();
    }

    void ApplyActivationState(bool active)
    {
        if (active)
            Activate();
        else
            Deactivate();
    }

    void FixedUpdate()
    {
        if (!isActive)
            return;

        if (bounceCooldownTimer > 0f)
            bounceCooldownTimer -= Time.fixedDeltaTime;

        Vector3 delta = worldDirection * (moveSpeed * Time.fixedDeltaTime);
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        float castDistance = delta.magnitude + castSkin;
        if (TryCastForWall(castDistance, out RaycastHit hit) &&
            IsWallHit(hit.collider, hit.normal) &&
            Vector3.Dot(worldDirection, FlattenNormal(hit.normal)) < 0f)
        {
            FlipDirection();
            delta = worldDirection * (moveSpeed * Time.fixedDeltaTime);
        }

        transform.position += delta;
        Physics.SyncTransforms();
        ApplyRollVisual(delta.magnitude);
        PushOverlappingEnemies(delta);
        TryDamageNearbyPlayer();
    }

    bool IsWallHit(Collider other, Vector3 normal)
    {
        if (!IsBlockingCollider(other))
            return false;

        return FlattenNormal(normal).sqrMagnitude >= 0.05f;
    }

    static Vector3 FlattenNormal(Vector3 normal)
    {
        normal.y = 0f;
        return normal;
    }

    /// <summary>
    /// Cylinder rollers stay on their travel axis: wall contact simply reverses movement.
    /// </summary>
    void FlipDirection()
    {
        if (bounceCooldownTimer > 0f)
            return;

        worldDirection = -worldDirection;
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
            worldDirection = -GetFlattenedWorldDirection(localMoveDirection);

        if (worldDirection.sqrMagnitude < 0.0001f)
            worldDirection = Vector3.right;
        else
            worldDirection.Normalize();

        bounceCooldownTimer = wallBounceCooldown;
    }

    bool IsBlockingCollider(Collider other)
    {
        if (other == null || other.isTrigger)
            return false;

        if (IsOwnCollider(other))
            return false;

        if (other.GetComponentInParent<PlayerController>() != null)
            return false;

        if (other.GetComponentInParent<EnemyAI>() != null)
            return false;

        if (other.GetComponentInParent<Projectile>() != null)
            return false;

        return ((1 << other.gameObject.layer) & wallMask) != 0;
    }

    bool IsOwnCollider(Collider other)
    {
        if (other == null)
            return false;

        if (bodyCollider != null && (other == bodyCollider || other.transform.IsChildOf(bodyCollider.transform) ||
                                     bodyCollider.transform.IsChildOf(other.transform)))
            return true;

        return other.transform == transform || other.transform.IsChildOf(transform);
    }

    void PushOverlappingEnemies(Vector3 delta)
    {
        if (!pushEnemies || !isActive)
            return;

        Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
        if (flatDelta.sqrMagnitude <= 0.000001f)
            return;

        int hitCount = OverlapBody(~0, QueryTriggerInteraction.Ignore);
        if (hitCount <= 0)
            return;

        PushedEnemies.Clear();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null || IsOwnCollider(hit))
                continue;

            EnemyAI enemy = hit.GetComponentInParent<EnemyAI>();
            if (enemy == null || !enemy.IsAlive || !PushedEnemies.Add(enemy))
                continue;

            PushEnemy(enemy, flatDelta);
        }
    }

    void PushEnemy(EnemyAI enemy, Vector3 flatDelta)
    {
        Transform enemyTransform = enemy.transform;
        Vector3 targetPosition = enemyTransform.position + flatDelta;

        if (!enemy.TryGetComponent(out NavMeshAgent agent) || !agent.enabled)
        {
            enemyTransform.position = targetPosition;
            return;
        }

        if (agent.isOnNavMesh &&
            NavMesh.SamplePosition(
                targetPosition,
                out NavMeshHit navHit,
                enemyPushSampleDistance,
                NavMesh.AllAreas))
        {
            agent.Warp(navHit.position);
            return;
        }

        enemyTransform.position = targetPosition;
    }

    void TryDamageNearbyPlayer()
    {
        if (!isActive || contactDamage <= 0f || damageTimer > 0f)
            return;

        int hitCount = OverlapBody(~0, QueryTriggerInteraction.Collide);
        if (hitCount <= 0)
            return;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null || IsOwnCollider(hit))
                continue;

            TryDamagePlayer(hit);
            if (damageTimer > 0f)
                return;
        }
    }

    void TryDamagePlayer(Collider other)
    {
        if (contactDamage <= 0f || damageTimer > 0f || other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        player.TakeDamage(contactDamage);
        damageTimer = damageCooldown;
    }

    void Update()
    {
        if (damageTimer > 0f)
            damageTimer -= Time.deltaTime;
    }

    void ApplyRollVisual(float movedDistance)
    {
        if (rollVisual == null || movedDistance <= 0f || rollRadius <= 0.01f)
            return;

        float degrees = movedDistance / rollRadius * Mathf.Rad2Deg * visualRollMultiplier;
        Vector3 axis = Vector3.Cross(Vector3.up, worldDirection);
        if (axis.sqrMagnitude < 0.0001f)
            return;

        rollVisual.Rotate(axis.normalized, degrees, Space.World);
    }

    bool TryCastForWall(float distance, out RaycastHit hit)
    {
        hit = default;
        if (bodyCollider == null || distance <= 0f)
            return false;

        if (bodyCapsule != null && TryGetCapsuleCastPoints(bodyCapsule, out Vector3 point1, out Vector3 point2, out float radius))
        {
            return Physics.CapsuleCast(
                point1,
                point2,
                radius,
                worldDirection,
                out hit,
                distance,
                wallMask,
                QueryTriggerInteraction.Ignore);
        }

        Bounds bounds = bodyCollider.bounds;
        Vector3 halfExtents = bounds.extents * 0.98f;
        halfExtents.x = Mathf.Max(0.05f, halfExtents.x);
        halfExtents.y = Mathf.Max(0.05f, halfExtents.y);
        halfExtents.z = Mathf.Max(0.05f, halfExtents.z);

        return Physics.BoxCast(
            bounds.center,
            halfExtents,
            worldDirection,
            out hit,
            Quaternion.identity,
            distance,
            wallMask,
            QueryTriggerInteraction.Ignore);
    }

    int OverlapBody(LayerMask mask, QueryTriggerInteraction triggerInteraction)
    {
        if (bodyCollider == null)
            return 0;

        if (bodyCapsule != null && TryGetCapsuleCastPoints(bodyCapsule, out Vector3 point1, out Vector3 point2, out float radius))
        {
            return Physics.OverlapCapsuleNonAlloc(
                point1,
                point2,
                radius,
                OverlapHits,
                mask,
                triggerInteraction);
        }

        Bounds bounds = bodyCollider.bounds;
        Vector3 halfExtents = bounds.extents;
        halfExtents.x = Mathf.Max(0.05f, halfExtents.x);
        halfExtents.y = Mathf.Max(0.05f, halfExtents.y);
        halfExtents.z = Mathf.Max(0.05f, halfExtents.z);

        return Physics.OverlapBoxNonAlloc(
            bounds.center,
            halfExtents,
            OverlapHits,
            Quaternion.identity,
            mask,
            triggerInteraction);
    }

    bool TryGetCapsuleCastPoints(
        CapsuleCollider capsule,
        out Vector3 point1,
        out Vector3 point2,
        out float radius)
    {
        point1 = point2 = Vector3.zero;
        radius = 0.5f;

        if (capsule == null)
            return false;

        Transform capsuleTransform = capsule.transform;
        Vector3 center = capsuleTransform.TransformPoint(capsule.center);
        Vector3 localAxis = GetCapsuleAxis(capsule.direction);
        float heightScale = GetAxisScale(capsuleTransform, localAxis);
        float radiusScale = GetRadiusScale(capsuleTransform, localAxis);

        float scaledRadius = Mathf.Max(0.1f, capsule.radius * radiusScale);
        float scaledHeight = Mathf.Max(
            scaledRadius * 2f,
            capsule.height * heightScale);
        float halfSegment = Mathf.Max(0f, scaledHeight * 0.5f - scaledRadius);

        Vector3 axis = capsuleTransform.TransformDirection(localAxis);
        if (axis.sqrMagnitude < 0.0001f)
            return false;

        axis.Normalize();
        point1 = center - axis * halfSegment;
        point2 = center + axis * halfSegment;
        radius = scaledRadius;
        return true;
    }

    static Vector3 GetCapsuleAxis(int direction)
    {
        return direction switch
        {
            0 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.up
        };
    }

    static float GetAxisScale(Transform source, Vector3 localAxis)
    {
        Vector3 scale = source.lossyScale;
        return Mathf.Abs(localAxis.x) * scale.x +
               Mathf.Abs(localAxis.y) * scale.y +
               Mathf.Abs(localAxis.z) * scale.z;
    }

    static float GetRadiusScale(Transform source, Vector3 localAxis)
    {
        Vector3 scale = source.lossyScale;
        if (Mathf.Abs(localAxis.x) > 0.5f)
            return Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        if (Mathf.Abs(localAxis.z) > 0.5f)
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
    }

    Vector3 GetFlattenedWorldDirection(Vector3 localDirection)
    {
        Vector3 direction = transform.TransformDirection(localDirection);
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    void CacheRollRadius()
    {
        if (bodyCapsule != null)
        {
            Transform capsuleTransform = bodyCapsule.transform;
            Vector3 localAxis = GetCapsuleAxis(bodyCapsule.direction);
            rollRadius = Mathf.Max(0.25f, bodyCapsule.radius * GetRadiusScale(capsuleTransform, localAxis));
            return;
        }

        if (bodyCollider != null)
        {
            Bounds bounds = bodyCollider.bounds;
            rollRadius = Mathf.Max(0.25f, Mathf.Min(bounds.extents.x, bounds.extents.z));
            return;
        }

        rollRadius = Mathf.Max(0.5f, Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            ResolveBodyCollider();

        Vector3 direction = Application.isPlaying
            ? worldDirection
            : GetFlattenedWorldDirection(localMoveDirection);
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.right;

        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawRay(transform.position, direction * 3f);
        Gizmos.DrawRay(transform.position, -direction * 3f);

        if (bodyCapsule != null && TryGetCapsuleCastPoints(bodyCapsule, out Vector3 point1, out Vector3 point2, out float radius))
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireSphere(point1, radius);
            Gizmos.DrawWireSphere(point2, radius);
            return;
        }

        if (bodyCollider != null)
        {
            Bounds bounds = bodyCollider.bounds;
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
#endif
}
