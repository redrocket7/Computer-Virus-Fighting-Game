using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Support enemy that stays near the closest ally and heals damaged allies in range.
/// If no allies remain, it attacks the player like a normal enemy.
/// </summary>
public class RepairEnemyAI : EnemyAI
{
    [Header("Support Movement")]
    [SerializeField] float preferredAllyRange = 4.5f;
    [SerializeField] float allySearchRadius = 40f;

    [Header("Repair")]
    [SerializeField] float healRadius = 12f;
    [SerializeField] float healPerSecond = 1.5f;
    [SerializeField] float healTickInterval = 0.25f;
    [SerializeField] LayerMask allyMask = ~0;
    [SerializeField] ParticleSystem healEffect;

    readonly List<EnemyAI> alliesInRange = new List<EnemyAI>();
    readonly List<EnemyAI> allySearchBuffer = new List<EnemyAI>();
    readonly Collider[] overlapHits = new Collider[32];

    EnemyAI supportTarget;
    float healTimer;

    protected override void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        supportTarget = FindClosestAlly();
        UpdateChase();
        UpdateContactDamage();

        healTimer -= Time.deltaTime;
        if (healTimer > 0f)
            return;

        float tick = Mathf.Max(0.05f, healTickInterval);
        healTimer = tick;
        HealAllies(healPerSecond * tick);
    }

    protected override Transform GetChaseTarget()
    {
        return supportTarget != null ? supportTarget.transform : Player;
    }

    protected override float GetChaseStopDistance()
    {
        if (supportTarget != null)
            return preferredAllyRange;
        return base.GetChaseStopDistance();
    }

    EnemyAI FindClosestAlly()
    {
        CollectAllies(allySearchBuffer, allySearchRadius, damagedOnly: false);
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

    void HealAllies(float amount)
    {
        if (amount <= 0f)
            return;

        CollectAllies(alliesInRange, healRadius, damagedOnly: true);
        if (alliesInRange.Count == 0)
            return;

        bool healedAny = false;
        for (int i = 0; i < alliesInRange.Count; i++)
        {
            EnemyAI ally = alliesInRange[i];
            float before = ally.CurrentHealth;
            ally.Heal(amount);
            if (ally.CurrentHealth > before)
                healedAny = true;
        }

        if (healedAny && healEffect != null && !healEffect.isPlaying)
            healEffect.Play();
    }

    void CollectAllies(List<EnemyAI> results, float radius, bool damagedOnly)
    {
        results.Clear();
        float radiusSqr = radius * radius;

        if (BoundEncounter != null)
        {
            BoundEncounter.GetLivingEnemies(results);
            for (int i = results.Count - 1; i >= 0; i--)
            {
                EnemyAI ally = results[i];
                if (ally == null || ally == this || !IsAlliedWith(ally) || (damagedOnly && !ally.IsDamaged))
                {
                    results.RemoveAt(i);
                    continue;
                }

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
            if (ally == null || !IsAlliedWith(ally) || (damagedOnly && !ally.IsDamaged) || results.Contains(ally))
                continue;

            results.Add(ally);
        }
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.95f, 0.45f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, healRadius);
        Gizmos.color = new Color(0.4f, 1f, 0.7f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, preferredAllyRange);
    }
#endif
}
