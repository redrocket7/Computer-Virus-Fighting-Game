using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Giant rolling cylinder that moves back and forth and reverses when it hits a wall.
/// Attach to the trap root. The mesh child is visual-only.
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
    [SerializeField] CapsuleCollider bodyCollider;

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
    Vector3 homeLocalPosition;
    Vector3 worldDirection;
    float bounceCooldownTimer;
    float damageTimer;
    float rollRadius = 1f;
    bool isActive;
    bool subscribedToEncounter;

    void Awake()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider>();

        if (bodyCollider == null)
            bodyCollider = GetComponentInChildren<CapsuleCollider>();

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

        if (TryGetCastPoints(out Vector3 point1, out Vector3 point2, out float radius) &&
            Physics.CapsuleCast(
                point1,
                point2,
                radius,
                worldDirection,
                out RaycastHit hit,
                delta.magnitude + castSkin,
                wallMask,
                QueryTriggerInteraction.Ignore) &&
            IsWallHit(hit.collider, hit.normal))
        {
            BounceOffNormal(hit.normal);
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

        normal.y = 0f;
        return normal.sqrMagnitude >= 0.05f;
    }

    void BounceOffNormal(Vector3 normal)
    {
        if (bounceCooldownTimer > 0f)
            return;

        normal.y = 0f;
        if (normal.sqrMagnitude < 0.05f)
            return;

        normal.Normalize();
        worldDirection = Vector3.Reflect(worldDirection, normal);
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
            worldDirection = -GetFlattenedWorldDirection(localMoveDirection);
        worldDirection.Normalize();

        bounceCooldownTimer = wallBounceCooldown;
    }

    bool IsBlockingCollider(Collider other)
    {
        if (other == null || other.isTrigger)
            return false;

        if (other.GetComponentInParent<PlayerController>() != null)
            return false;

        if (other.GetComponentInParent<EnemyAI>() != null)
            return false;

        if (other.GetComponentInParent<Projectile>() != null)
            return false;

        return ((1 << other.gameObject.layer) & wallMask) != 0;
    }

    void PushOverlappingEnemies(Vector3 delta)
    {
        if (!pushEnemies || !isActive)
            return;

        Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
        if (flatDelta.sqrMagnitude <= 0.000001f)
            return;

        if (!TryGetCastPoints(out Vector3 point1, out Vector3 point2, out float radius))
            return;

        PushedEnemies.Clear();
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            point1,
            point2,
            radius,
            OverlapHits,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null)
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

        if (!TryGetCastPoints(out Vector3 point1, out Vector3 point2, out float radius))
            return;

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            point1,
            point2,
            radius,
            OverlapHits,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null)
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

    bool TryGetCastPoints(out Vector3 point1, out Vector3 point2, out float radius)
    {
        point1 = point2 = Vector3.zero;
        radius = 0.5f;

        if (bodyCollider == null)
            return false;

        Vector3 center = transform.TransformPoint(bodyCollider.center);
        float scaledRadius = Mathf.Max(
            0.1f,
            bodyCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));
        float scaledHeight = Mathf.Max(
            scaledRadius * 2f,
            bodyCollider.height * transform.lossyScale.y);
        float halfSegment = Mathf.Max(0f, scaledHeight * 0.5f - scaledRadius);

        Vector3 axis = GetCapsuleAxis(bodyCollider.direction);
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

    Vector3 GetFlattenedWorldDirection(Vector3 localDirection)
    {
        Vector3 direction = transform.TransformDirection(localDirection);
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    void CacheRollRadius()
    {
        if (bodyCollider != null)
        {
            rollRadius = Mathf.Max(
                0.25f,
                bodyCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));
            return;
        }

        rollRadius = Mathf.Max(0.5f, Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 direction = Application.isPlaying
            ? worldDirection
            : GetFlattenedWorldDirection(localMoveDirection);
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.right;

        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawRay(transform.position, direction * 3f);
        Gizmos.DrawRay(transform.position, -direction * 3f);

        if (TryGetCastPoints(out Vector3 point1, out Vector3 point2, out float radius))
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireSphere(point1, radius);
            Gizmos.DrawWireSphere(point2, radius);
        }
    }
#endif
}
