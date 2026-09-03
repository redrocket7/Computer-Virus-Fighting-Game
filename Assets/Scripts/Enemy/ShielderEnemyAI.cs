using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Support enemy that starts with a shield and periodically grants shields to random shieldless allies.
/// If no allies remain, it attacks the player like a normal enemy.
/// </summary>
public class ShielderEnemyAI : EnemyAI
{
    [Header("Support Movement")]
    [SerializeField] float preferredAllyRange = 4.5f;
    [SerializeField] float allySearchRadius = 40f;

    [Header("Shielding")]
    [SerializeField] float grantShieldInterval = 4f;
    [SerializeField] float grantShieldRadius = 14f;
    [SerializeField] float selfShieldHealth = 5f;
    [SerializeField] float grantedShieldHealth = 4f;
    [SerializeField] float grantedShieldRadiusMultiplier = 1f;
    [SerializeField] LayerMask allyMask = ~0;
    [SerializeField] ParticleSystem shieldEffect;

    readonly List<EnemyAI> shieldlessAllies = new List<EnemyAI>();
    readonly List<EnemyAI> allySearchBuffer = new List<EnemyAI>();
    readonly Collider[] overlapHits = new Collider[32];

    EnemyAI supportTarget;
    float shieldTimer;

    protected override void Start()
    {
        base.Start();
        GrantShield(selfShieldHealth);
        shieldTimer = grantShieldInterval * 0.35f;
    }

    protected override void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        supportTarget = FindClosestAlly();
        UpdateChase();
        UpdateContactDamage();

        shieldTimer -= Time.deltaTime;
        if (shieldTimer > 0f)
            return;

        shieldTimer = Mathf.Max(0.5f, grantShieldInterval);
        TryGrantAllyShield();
    }

    protected override Transform GetChaseTarget()
    {
        return supportTarget != null ? supportTarget.transform : Player;
    }

    protected override bool ShowSupportPriorityMarker => true;

    public override bool IsSupportEnemy => true;

    protected override float GetChaseStopDistance()
    {
        if (supportTarget != null)
            return preferredAllyRange;
        return base.GetChaseStopDistance();
    }

    EnemyAI FindClosestAlly()
    {
        CollectAllies(allySearchBuffer, allySearchRadius, shieldlessOnly: false, roomWide: false);
        EnemyAI closest = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < allySearchBuffer.Count; i++)
        {
            EnemyAI ally = allySearchBuffer[i];
            if (ally == null)
                continue;

            Vector3 offset = ally.transform.position - transform.position;
            offset.y = 0f;
            float sqr = offset.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                closest = ally;
            }
        }

        return closest;
    }

    void TryGrantAllyShield()
    {
        CollectAllies(shieldlessAllies, grantShieldRadius, shieldlessOnly: true, roomWide: true);
        if (shieldlessAllies.Count == 0)
            return;

        EnemyAI target = shieldlessAllies[Random.Range(0, shieldlessAllies.Count)];
        if (target == null || target.HasActiveShield)
            return;

        target.GrantShield(grantedShieldHealth, grantedShieldRadiusMultiplier);

        if (shieldEffect != null)
        {
            shieldEffect.transform.position = target.transform.position + Vector3.up * 1.2f;
            shieldEffect.Play();
        }
    }

    void CollectAllies(List<EnemyAI> results, float radius, bool shieldlessOnly, bool roomWide)
    {
        results.Clear();
        float radiusSqr = radius * radius;

        if (BoundEncounter != null)
        {
            BoundEncounter.GetLivingEnemies(results);
            for (int i = results.Count - 1; i >= 0; i--)
            {
                EnemyAI ally = results[i];
                if (ally == null ||
                    ally == this ||
                    !IsAlliedWith(ally) ||
                    ally.IsSupportEnemy ||
                    (shieldlessOnly && ally.HasActiveShield))
                {
                    results.RemoveAt(i);
                    continue;
                }

                if (roomWide)
                    continue;

                Vector3 offset = ally.transform.position - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > radiusSqr)
                    results.RemoveAt(i);
            }

            return;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            overlapHits,
            allyMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null)
                continue;

            EnemyAI ally = hit.GetComponentInParent<EnemyAI>();
            if (ally == null ||
                ally == this ||
                !IsAlliedWith(ally) ||
                ally.IsSupportEnemy ||
                (shieldlessOnly && ally.HasActiveShield) ||
                results.Contains(ally))
                continue;

            results.Add(ally);
        }
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, grantShieldRadius);
        Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, preferredAllyRange);
    }
#endif
}
