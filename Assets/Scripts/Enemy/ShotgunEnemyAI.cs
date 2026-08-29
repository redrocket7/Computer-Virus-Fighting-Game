using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Ranged enemy that fires a short spread of small pellets.
/// Fire rate sits between Gun Enemy (slower) and Bounce Gun Enemy (faster).
/// </summary>
public class ShotgunEnemyAI : EnemyAI
{
    [Header("Ranged Movement")]
    [SerializeField] float preferredRange = 10f;
    [SerializeField] float retreatRange = 6f;
    [SerializeField] float retreatDistance = 5f;
    [SerializeField] float repathInterval = 0.2f;

    [Header("Shooting")]
    [SerializeField] Projectile projectilePrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float projectileHeight = 0.75f;
    [SerializeField] float shootingRange = 14f;
    [Tooltip("Seconds between blasts. Gun Enemy prefab is ~2s; Bounce Gun is ~1.25s.")]
    [SerializeField] float fireInterval = 1.55f;
    [SerializeField] int minimumPellets = 3;
    [SerializeField] int maximumPellets = 4;
    [Tooltip("Total horizontal spread of the pellet fan, in degrees.")]
    [SerializeField] float spreadAngle = 28f;
    [SerializeField] LayerMask lineOfSightMask = ~0;

    [Header("Muzzle Recoil")]
    [SerializeField] float recoilDistance = 0.4f;
    [SerializeField] float recoilRecoverTime = 0.14f;

    [Header("Death Split")]
    [SerializeField] GameObject tinyEnemyPrefab;
    [SerializeField] float tinySpawnRadius = 2.5f;

    float fireTimer;
    float repathTimer;
    bool hasSplit;
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
            distance <= shootingRange &&
            projectilePrefab != null &&
            HasLineOfSight())
        {
            Shoot(toPlayer);
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

        int minPellets = Mathf.Max(1, minimumPellets);
        int maxPellets = Mathf.Max(minPellets, maximumPellets);
        int pelletCount = Random.Range(minPellets, maxPellets + 1);
        float halfSpread = spreadAngle * 0.5f;

        for (int i = 0; i < pelletCount; i++)
        {
            float t = pelletCount == 1 ? 0.5f : i / (float)(pelletCount - 1);
            float yaw = Mathf.Lerp(-halfSpread, halfSpread, t);
            Vector3 shotDirection = Quaternion.Euler(0f, yaw, 0f) * flatDirection;
            // Nudge along the shot so stacked pellets do not overlap on spawn.
            Vector3 pelletSpawn = spawnPosition + shotDirection * 0.15f;

            Projectile projectile = Instantiate(
                projectilePrefab,
                pelletSpawn,
                Quaternion.LookRotation(shotDirection, Vector3.up));
            projectile.LaunchAsEnemy(shotDirection, transform);
        }

        fireTimer = ScaleFireInterval(fireInterval);
        muzzleRecoil.Play(recoilDistance, recoilRecoverTime);
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

    protected override void Die()
    {
        if (!hasSplit)
        {
            hasSplit = true;
            SpawnDeathRing(ChooseSpawnCount());
        }

        base.Die();
    }

    void SpawnDeathRing(int count)
    {
        if (tinyEnemyPrefab == null || count <= 0)
            return;

        float angleOffset = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float angle = angleOffset + (360f / count) * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 desiredPosition = transform.position + direction * tinySpawnRadius;
            Vector3 spawnPosition = desiredPosition;

            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, tinySpawnRadius * 2f, NavMesh.AllAreas))
                spawnPosition = hit.position;

            GameObject instance = Instantiate(
                tinyEnemyPrefab,
                spawnPosition,
                Quaternion.LookRotation(direction, Vector3.up));

            EnemyAI child = instance.GetComponent<EnemyAI>();
            if (child != null && boundEncounter != null)
                boundEncounter.RegisterEnemy(child);
        }
    }

    static int ChooseSpawnCount()
    {
        float roll = Random.value;
        if (roll < 0.2f)
            return 2;
        if (roll < 0.8f)
            return 3;
        return 4;
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
