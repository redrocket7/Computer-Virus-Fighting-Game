using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases normally up close. At a long range it winds up, then rams the player.
/// </summary>
public class RamEnemyAI : EnemyAI
{
    enum State
    {
        Chase,
        Windup,
        Ramming,
        Recover
    }

    [Header("Charge Range")]
    [Tooltip("Minimum flat distance to the player required to start a charge.")]
    [SerializeField] float chargeTriggerDistance = 12f;
    [Tooltip("Won't start a charge if farther than this (keeps rams readable).")]
    [SerializeField] float chargeMaxDistance = 22f;
    [SerializeField] LayerMask lineOfSightMask = ~0;

    [Header("Windup")]
    [SerializeField] float windupDuration = 0.75f;
    [SerializeField] float windupScaleMultiplier = 1.2f;

    [Header("Ram")]
    [SerializeField] float ramSpeed = 22f;
    [SerializeField] float ramDuration = 0.85f;
    [SerializeField] float ramDamage = 2f;
    [SerializeField] float ramHitRange = 2.4f;
    [SerializeField] float ramStopDistance = 0.75f;
    [SerializeField] float wallCheckDistance = 1.1f;
    [SerializeField] LayerMask wallMask = ~0;

    [Header("Recovery")]
    [SerializeField] float recoverDuration = 0.9f;
    [SerializeField] float chargeCooldown = 2.5f;

    State state = State.Chase;
    float stateTimer;
    float cooldownTimer;
    Vector3 ramDirection;
    Vector3 baseScale;
    IDamageable playerDamageable;
    bool landedRamHit;

    protected override void Awake()
    {
        base.Awake();
        baseScale = transform.localScale;
    }

    protected override void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        switch (state)
        {
            case State.Chase:
                TickChase();
                break;
            case State.Windup:
                TickWindup();
                break;
            case State.Ramming:
                TickRam();
                break;
            case State.Recover:
                TickRecover();
                break;
        }
    }

    void TickChase()
    {
        UpdateChase();
        UpdateContactDamage();

        if (Player == null || cooldownTimer > 0f)
            return;

        float distance = FlatDistanceTo(Player.position);
        if (distance < chargeTriggerDistance || distance > chargeMaxDistance)
            return;

        if (!HasLineOfSight())
            return;

        BeginWindup();
    }

    void BeginWindup()
    {
        state = State.Windup;
        stateTimer = Mathf.Max(0.05f, windupDuration);
        transform.localScale = baseScale;
        StopAgentPath();
        IsChasing = false;

        if (Player != null)
            FaceDirection(Player.position - transform.position);
    }

    void TickWindup()
    {
        if (Player != null)
            FaceDirection(Player.position - transform.position);

        stateTimer -= Time.deltaTime;
        float progress = 1f - Mathf.Clamp01(stateTimer / Mathf.Max(0.05f, windupDuration));
        float scale = Mathf.Lerp(1f, windupScaleMultiplier, progress * progress);
        transform.localScale = baseScale * scale;

        if (stateTimer > 0f)
            return;

        BeginRam();
    }

    void BeginRam()
    {
        transform.localScale = baseScale;
        state = State.Ramming;
        stateTimer = Mathf.Max(0.05f, ramDuration);
        landedRamHit = false;

        ramDirection = Player != null ? Player.position - transform.position : transform.forward;
        ramDirection.y = 0f;
        if (ramDirection.sqrMagnitude < 0.001f)
            ramDirection = transform.forward;
        ramDirection.Normalize();

        FaceDirection(ramDirection);
        StopAgentPath();

        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.isStopped = true;
            Agent.velocity = Vector3.zero;
        }
    }

    void TickRam()
    {
        stateTimer -= Time.deltaTime;
        FaceDirection(ramDirection);

        if (HitWallAhead())
        {
            EndRam();
            return;
        }

        float step = ramSpeed * Time.deltaTime;
        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.Move(ramDirection * step);
        }
        else
        {
            transform.position += ramDirection * step;
        }

        if (!landedRamHit && TryRamHit())
        {
            landedRamHit = true;
            EndRam();
            return;
        }

        if (Player != null && FlatDistanceTo(Player.position) <= ramStopDistance)
        {
            EndRam();
            return;
        }

        if (stateTimer <= 0f)
            EndRam();
    }

    bool TryRamHit()
    {
        if (Player == null || ramDamage <= 0f)
            return false;

        if (FlatDistanceTo(Player.position) > ramHitRange)
            return false;

        if (playerDamageable == null)
            playerDamageable = Player.GetComponentInParent<IDamageable>();

        if (playerDamageable == null)
            return false;

        playerDamageable.TakeDamage(ScaleOutgoingDamage(ramDamage));
        return true;
    }

    bool HitWallAhead()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (!Physics.Raycast(
                origin,
                ramDirection,
                out RaycastHit hit,
                wallCheckDistance,
                wallMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (Player != null && hit.collider.GetComponentInParent<PlayerController>() != null)
            return false;

        if (hit.collider.GetComponentInParent<EnemyAI>() != null)
            return false;

        return !hit.collider.isTrigger;
    }

    void EndRam()
    {
        transform.localScale = baseScale;
        state = State.Recover;
        stateTimer = Mathf.Max(0.05f, recoverDuration);
        cooldownTimer = Mathf.Max(0f, chargeCooldown);
        StopAgentPath();
        RefreshMoveSpeed();
    }

    void TickRecover()
    {
        stateTimer -= Time.deltaTime;
        if (Player != null)
            FaceDirection(Player.position - transform.position);

        if (stateTimer > 0f)
            return;

        state = State.Chase;
        if (Agent != null && Agent.isOnNavMesh)
            Agent.isStopped = false;
    }

    float FlatDistanceTo(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    bool HasLineOfSight()
    {
        if (Player == null)
            return false;

        Vector3 origin = transform.position + Vector3.up * 0.5f;
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

    protected override void Die()
    {
        transform.localScale = baseScale;
        base.Die();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, chargeTriggerDistance);
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, chargeMaxDistance);
    }
#endif
}
