using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Slow ranged enemy that keeps its distance and fires large cannonballs.
/// </summary>
public class CannonEnemyAI : EnemyAI
{
    [Header("Ranged Movement")]
    [SerializeField] float preferredRange = 16f;
    [SerializeField] float retreatRange = 10f;
    [SerializeField] float retreatDistance = 6f;
    [SerializeField] float repathInterval = 0.25f;

    [Header("Cannon")]
    [SerializeField] Projectile cannonballPrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float muzzleOffset = 1.6f;
    [SerializeField] float projectileHeight = 1f;
    [SerializeField] float shootingRange = 28f;
    [SerializeField] float fireInterval = 2.8f;
    [SerializeField] float aimLead = 0.45f;
    [SerializeField] LayerMask lineOfSightMask = ~0;

    [Header("Muzzle Recoil")]
    [SerializeField] float recoilDistance = 0.55f;
    [SerializeField] float recoilRecoverTime = 0.18f;

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
        FaceDirection(AimDirection(toPlayer));

        if (fireTimer <= 0f &&
            distance <= shootingRange &&
            cannonballPrefab != null &&
            HasLineOfSight())
        {
            Shoot(AimDirection(toPlayer));
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

    Vector3 AimDirection(Vector3 toPlayer)
    {
        Vector3 aim = toPlayer;
        if (aimLead > 0f && Player != null)
        {
            if (playerBody == null)
                playerBody = Player.GetComponent<Rigidbody>();

            if (playerBody != null)
            {
                Vector3 velocity = playerBody.linearVelocity;
                velocity.y = 0f;
                aim += velocity * aimLead;
            }
        }

        aim.y = 0f;
        return aim.sqrMagnitude > 0.001f ? aim : toPlayer;
    }

    bool HasLineOfSight()
    {
        Vector3 origin = GetMuzzlePosition();
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

    void Shoot(Vector3 flatDirection)
    {
        if (flatDirection.sqrMagnitude < 0.001f)
            return;

        flatDirection.Normalize();
        Vector3 spawnPosition = GetMuzzlePosition();
        Projectile projectile = Instantiate(
            cannonballPrefab,
            spawnPosition,
            Quaternion.LookRotation(flatDirection, Vector3.up));

        projectile.LaunchAsEnemy(flatDirection, transform);
        fireTimer = ScaleFireInterval(fireInterval);
        muzzleRecoil.Play(recoilDistance, recoilRecoverTime);
    }

    protected override bool SupportsFireRateBuff => true;

    Vector3 GetMuzzlePosition()
    {
        if (firePoint != null)
            return firePoint.position + firePoint.up * muzzleOffset;

        Vector3 forward = Player != null ? Player.position - transform.position : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = transform.forward;

        return transform.position +
               Vector3.up * projectileHeight +
               forward.normalized * (Agent.radius + 0.8f);
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, preferredRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, retreatRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, shootingRange);
    }
#endif
}
