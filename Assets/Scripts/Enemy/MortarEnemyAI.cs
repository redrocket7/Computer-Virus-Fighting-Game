using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps distance from the player and lobbs explosive shells in an arc.
/// </summary>
public class MortarEnemyAI : EnemyAI
{
    [Header("Ranged Movement")]
    [SerializeField] float preferredRange = 16f;
    [SerializeField] float retreatRange = 9f;
    [SerializeField] float retreatDistance = 6f;
    [SerializeField] float repathInterval = 0.25f;

    [Header("Mortar")]
    [SerializeField] MortarShell shellPrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float projectileHeight = 1.2f;
    [SerializeField] float minFireRange = 5f;
    [SerializeField] float maxFireRange = 24f;
    [SerializeField] float fireInterval = 2.2f;
    [SerializeField] float arcHeight = 7f;
    [SerializeField] float flightTime = 1.15f;
    [SerializeField] float aimLead = 0.35f;
    [SerializeField] float impactHeight = 0.1f;

    [Header("Muzzle Recoil")]
    [SerializeField] float recoilDistance = 0.45f;
    [SerializeField] float recoilRecoverTime = 0.16f;

    float fireTimer;
    float repathTimer;
    Rigidbody playerBody;
    readonly EnemyMuzzleRecoil muzzleRecoil = new EnemyMuzzleRecoil();

    protected override void Start()
    {
        base.Start();
        muzzleRecoil.Bind(firePoint);
    }

    protected override void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        muzzleRecoil.Tick(Time.deltaTime);

        if (Player == null)
        {
            base.Update();
            return;
        }

        if (fireTimer > 0f)
            fireTimer -= Time.deltaTime;
        if (repathTimer > 0f)
            repathTimer -= Time.deltaTime;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        UpdateRangedMovement(toPlayer, distance);
        FaceDirection(toPlayer);

        if (fireTimer <= 0f &&
            shellPrefab != null &&
            distance >= minFireRange &&
            distance <= maxFireRange)
        {
            FireShell();
        }
    }

    void UpdateRangedMovement(Vector3 toPlayer, float distance)
    {
        if (!Agent.isOnNavMesh)
            return;

        if (distance > preferredRange)
        {
            SetDestination(Player.position);
            return;
        }

        if (distance < retreatRange && toPlayer.sqrMagnitude > 0.001f)
        {
            Vector3 desired = transform.position - toPlayer.normalized * retreatDistance;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, retreatDistance, NavMesh.AllAreas))
                SetDestination(hit.position);
            else
                StopMoving();
            return;
        }

        StopMoving();
    }

    void SetDestination(Vector3 destination)
    {
        if (repathTimer > 0f)
            return;

        repathTimer = repathInterval;
        Agent.isStopped = false;
        Agent.SetDestination(destination);
    }

    void StopMoving()
    {
        StopAgentPath();
    }

    void FireShell()
    {
        Vector3 spawnPosition = GetMuzzlePosition();
        Vector3 target = PredictImpactPoint();

        MortarShell shell = Instantiate(shellPrefab, spawnPosition, Quaternion.identity);
        shell.Launch(target, transform, arcHeight, flightTime);
        fireTimer = ScaleFireInterval(fireInterval);
        muzzleRecoil.Play(recoilDistance, recoilRecoverTime, Vector3.down);
    }

    protected override bool SupportsFireRateBuff => true;

    Vector3 PredictImpactPoint()
    {
        Vector3 target = Player.position;
        if (playerBody == null && Player != null)
            playerBody = Player.GetComponent<Rigidbody>();

        if (playerBody != null)
        {
            Vector3 velocity = playerBody.linearVelocity;
            velocity.y = 0f;
            target += velocity * aimLead;
        }

        target.y = impactHeight;
        return target;
    }

    Vector3 GetMuzzlePosition()
    {
        if (firePoint != null)
            return firePoint.position;

        Vector3 forward = Player != null ? Player.position - transform.position : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = transform.forward;

        return transform.position +
               Vector3.up * projectileHeight +
               forward.normalized * (Agent.radius + 0.4f);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, preferredRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, retreatRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, maxFireRange);
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, minFireRange);
    }
#endif
}
