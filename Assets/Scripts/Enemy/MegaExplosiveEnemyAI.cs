using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Mega mortar enemy that fires shells in a short burst, then explodes
/// after a countdown when killed.
/// </summary>
public class MegaExplosiveEnemyAI : EnemyAI
{
    [Header("Ranged Movement")]
    [SerializeField] float preferredRange = 18f;
    [SerializeField] float retreatRange = 10f;
    [SerializeField] float retreatDistance = 7f;
    [SerializeField] float repathInterval = 0.25f;

    [Header("Mortar Burst")]
    [SerializeField] MortarShell shellPrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float projectileHeight = 2.5f;
    [SerializeField] float minFireRange = 5f;
    [SerializeField] float maxFireRange = 26f;
    [SerializeField] int shellsPerBurst = 3;
    [SerializeField] float shellInterval = 0.5f;
    [SerializeField] float burstInterval = 3.2f;
    [SerializeField] float arcHeight = 10f;
    [SerializeField] float flightTime = 1.5f;
    [SerializeField] float aimLead = 0.4f;
    [SerializeField] float impactHeight = 0.1f;

    [Header("Death Explosion")]
    [SerializeField] GameObject deathExplosionPrefab;
    [SerializeField] float deathCountdown = 2.5f;
    [SerializeField] float deathChaseSpeedMultiplier = 2f;
    [SerializeField] float deathChaseRepathInterval = 0.1f;
    [Tooltip("Leg look-ahead while sprinting during the death countdown.")]
    [SerializeField] float deathSprintLookAheadTime = 0.15f;
    [SerializeField] float deathExplosionRadius = 8f;
    [SerializeField] float deathExplosionDamage = 4f;
    [SerializeField] LayerMask deathDamageMask = ~0;
    [SerializeField] float countdownShakeStrength = 0.12f;
    [SerializeField] float countdownShakeRotation = 1.2f;
    [SerializeField] float countdownShakeStrengthMax = 0.35f;
    [SerializeField] float countdownShakeRotationMax = 3f;
    [SerializeField] float deathShakeDuration = 0.55f;
    [SerializeField] float deathShakeStrength = 0.7f;
    [SerializeField] float deathShakeRotation = 4f;

    static readonly Collider[] overlapHits = new Collider[64];
    static readonly HashSet<IDamageable> damagedBuffer = new HashSet<IDamageable>();

    float burstCooldownTimer;
    float repathTimer;
    bool isDying;
    bool isFiringBurst;
    Coroutine burstRoutine;
    Coroutine deathRoutine;
    Rigidbody playerBody;
    ProceduralSpiderBody spiderBody;

    protected override void Awake()
    {
        base.Awake();
        spiderBody = GetComponent<ProceduralSpiderBody>();
        shellsPerBurst = Mathf.Max(1, shellsPerBurst);
    }

    protected override void Update()
    {
        if (isDying)
        {
            UpdateDeathChase();
            return;
        }

        TickHoldoff();
        if (!CanAct)
            return;

        if (Player == null)
        {
            base.Update();
            return;
        }

        if (!isFiringBurst)
            burstCooldownTimer -= Time.deltaTime;
        if (repathTimer > 0f)
            repathTimer -= Time.deltaTime;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (!isFiringBurst)
            UpdateRangedMovement(toPlayer, distance);

        FaceDirection(toPlayer);

        if (!isFiringBurst &&
            burstCooldownTimer <= 0f &&
            shellPrefab != null &&
            distance >= minFireRange &&
            distance <= maxFireRange)
        {
            burstRoutine = StartCoroutine(FireBurst());
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

    void UpdateDeathChase()
    {
        if (Player == null)
            return;

        repathTimer -= Time.deltaTime;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        FaceDirection(toPlayer);

        if (Agent.isOnNavMesh)
        {
            if (repathTimer <= 0f)
            {
                repathTimer = deathChaseRepathInterval;
                Agent.isStopped = false;
                Agent.SetDestination(Player.position);
            }
        }

        UpdateContactDamage();
    }

    IEnumerator FireBurst()
    {
        isFiringBurst = true;
        StopMoving();

        for (int i = 0; i < shellsPerBurst; i++)
        {
            if (isDying || !IsAlive)
                yield break;

            if (Player != null)
                FaceDirection(Player.position - transform.position);

            FireShell();

            if (i < shellsPerBurst - 1)
                yield return new WaitForSeconds(Mathf.Max(0.05f, shellInterval));
        }

        isFiringBurst = false;
        burstCooldownTimer = burstInterval;
        burstRoutine = null;
    }

    void FireShell()
    {
        Vector3 spawnPosition = GetMuzzlePosition();
        Vector3 target = PredictImpactPoint();

        MortarShell shell = Instantiate(shellPrefab, spawnPosition, Quaternion.identity);
        shell.Launch(target, transform, arcHeight, flightTime);
    }

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

    public override void TakeDamage(float amount)
    {
        if (isDying)
            return;

        base.TakeDamage(amount);
    }

    protected override void Die()
    {
        if (isDying)
            return;

        isDying = true;
        InfectionReport.RecordEnemyKill();

        if (burstRoutine != null)
        {
            StopCoroutine(burstRoutine);
            burstRoutine = null;
            isFiringBurst = false;
        }

        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.isStopped = false;
            Agent.speed = GetCurrentMoveSpeed() * Mathf.Max(1f, deathChaseSpeedMultiplier);
        }

        spiderBody?.SetLookAheadTime(deathSprintLookAheadTime);
        repathTimer = 0f;

        boundEncounter?.NotifyEnemyDestroyed(this);
        boundEncounter = null;

        deathRoutine = StartCoroutine(DeathExplosionRoutine());
    }

    IEnumerator DeathExplosionRoutine()
    {
        float total = Mathf.Max(0.1f, deathCountdown);
        float remaining = total;
        ScreenShake.BeginSustain(countdownShakeStrength, countdownShakeRotation);

        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(remaining / total);
            float ramp = progress * progress;
            ScreenShake.SetSustain(
                Mathf.Lerp(countdownShakeStrength, countdownShakeStrengthMax, ramp),
                Mathf.Lerp(countdownShakeRotation, countdownShakeRotationMax, ramp));
            yield return null;
        }

        ScreenShake.EndSustain();
        DetonateDeathExplosion();
        Destroy(gameObject);
    }

    void DetonateDeathExplosion()
    {
        ScreenShake.Shake(deathShakeDuration, deathShakeStrength, deathShakeRotation);

        if (deathExplosionPrefab != null)
        {
            GameObject effect = Instantiate(deathExplosionPrefab, transform.position, Quaternion.identity);
            Destroy(effect, ExplosionEffect.GetPoolLifetime(effect));
        }

        damagedBuffer.Clear();
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            deathExplosionRadius,
            overlapHits,
            deathDamageMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null)
                continue;

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null || ReferenceEquals(target, this) || !damagedBuffer.Add(target))
                continue;

            DelayedExplosionDamage.Apply(
                target,
                ScaleOutgoingDamage(deathExplosionDamage),
                transform.position,
                deathExplosionRadius);
        }
    }

    protected override void OnDestroy()
    {
        if (isDying)
            ScreenShake.EndSustain();

        if (deathRoutine != null)
            StopCoroutine(deathRoutine);

        boundEncounter = null;
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, preferredRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, retreatRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, maxFireRange);
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, minFireRange);
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, deathExplosionRadius);
    }
#endif
}
