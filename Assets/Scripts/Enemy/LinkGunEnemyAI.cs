using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Ranged Transfer variant: fires predicted three-round bursts.
/// Redirects incoming damage to the weakest ally until overload, like Transfer.
/// </summary>
public class LinkGunEnemyAI : EnemyAI
{
    [Header("Ranged Movement")]
    [SerializeField] float preferredRange = 13f;
    [SerializeField] float retreatRange = 8f;
    [SerializeField] float retreatDistance = 5f;
    [SerializeField] float repathInterval = 0.22f;

    [Header("Shooting")]
    [SerializeField] Projectile projectilePrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float projectileHeight = 0.75f;
    [SerializeField] float shootingRange = 20f;
    [SerializeField] int burstSize = 3;
    [SerializeField] float shotInterval = 0.12f;
    [SerializeField] float burstCooldown = 1.55f;
    [SerializeField] float aimLead = 0.32f;
    [SerializeField] LayerMask lineOfSightMask = ~0;

    [Header("Muzzle Recoil")]
    [SerializeField] float recoilDistance = 0.28f;
    [SerializeField] float recoilRecoverTime = 0.1f;

    [Header("Damage Transfer")]
    [Tooltip("Optional VFX played on the ally that receives redirected damage.")]
    [SerializeField] ParticleSystem transferEffect;
    [Tooltip("Total damage that can be redirected before this enemy overloads and takes damage itself.")]
    [SerializeField] float maxDamageBeforeOverload = 10f;
    [Tooltip("How long overload lasts before transfers work again.")]
    [SerializeField] float overloadDuration = 2.5f;

    float fireTimer;
    float repathTimer;
    int shotsFiredInBurst;
    float damageRedirected;
    float overloadTimer;
    Rigidbody playerBody;
    readonly List<EnemyAI> roomEnemies = new List<EnemyAI>();
    readonly EnemyMuzzleRecoil muzzleRecoil = new EnemyMuzzleRecoil();

    bool IsOverloaded => overloadTimer > 0f;

    /// <summary>Transfer enemies must not dump redirected damage onto Link Guns.</summary>
    public override bool CanBeTransferDamageTarget => false;

    protected override void Start()
    {
        base.Start();
        muzzleRecoil.Bind(firePoint);
    }

    protected override void Update()
    {
        if (overloadTimer > 0f)
            overloadTimer -= Time.deltaTime;

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
            projectilePrefab != null &&
            HasLineOfSight())
        {
            Shoot(AimDirection(toPlayer));
        }
    }

    public override void TakeDamage(float amount)
    {
        if (amount <= 0f || !IsAlive)
            return;

        // Shield absorbs first; only transfer once it is gone.
        if (HasActiveShield)
        {
            base.TakeDamage(amount);
            return;
        }

        if (IsOverloaded)
        {
            base.TakeDamage(amount);
            return;
        }

        EnemyAI target = FindTransferTarget();
        if (target == null)
        {
            base.TakeDamage(amount);
            return;
        }

        if (transferEffect != null)
        {
            transferEffect.transform.position = target.transform.position + Vector3.up * 1.2f;
            transferEffect.Play();
        }

        target.TakeDamage(amount);
        damageRedirected += amount;

        if (damageRedirected >= Mathf.Max(0.01f, maxDamageBeforeOverload))
        {
            damageRedirected = 0f;
            overloadTimer = Mathf.Max(0.1f, overloadDuration);
        }
    }

    EnemyAI FindTransferTarget()
    {
        RoomEncounter encounter = BoundEncounter;
        if (encounter == null)
            return null;

        encounter.GetLivingEnemies(roomEnemies);
        EnemyAI weakest = null;
        float lowestHealth = float.MaxValue;

        for (int i = 0; i < roomEnemies.Count; i++)
        {
            EnemyAI ally = roomEnemies[i];
            if (ally == null || ally == this || !ally.IsAlive)
                continue;

            // Avoid bounce loops with Transfers / other Link Guns.
            if (ally is TransferEnemyAI || ally is LinkGunEnemyAI || ally is MegaTransferEnemyAI || !ally.CanBeTransferDamageTarget)
                continue;

            if (ally.CurrentHealth < lowestHealth)
            {
                lowestHealth = ally.CurrentHealth;
                weakest = ally;
            }
        }

        return weakest;
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
            projectilePrefab,
            spawnPosition,
            Quaternion.LookRotation(flatDirection, Vector3.up));

        projectile.LaunchAsEnemy(flatDirection, transform);
        muzzleRecoil.Play(recoilDistance, recoilRecoverTime);

        int size = Mathf.Max(1, burstSize);
        shotsFiredInBurst++;
        if (shotsFiredInBurst >= size)
        {
            shotsFiredInBurst = 0;
            fireTimer = ScaleFireInterval(burstCooldown);
        }
        else
        {
            fireTimer = ScaleFireInterval(shotInterval);
        }
    }

    protected override bool SupportsFireRateBuff => true;

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
               forward.normalized * (Agent.radius + 0.6f);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
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
