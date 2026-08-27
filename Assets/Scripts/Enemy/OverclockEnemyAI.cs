using System.Collections.Generic;
using UnityEngine;

public enum OverclockBuffType
{
    Damage,
    MaxHealth,
    Speed,
    FireRate
}

/// <summary>
/// Support enemy that stays near the closest ally and periodically overclocks a random ally in the room.
/// If no allies remain, it attacks the player like a normal enemy.
/// </summary>
public class OverclockEnemyAI : EnemyAI
{
    static readonly OverclockBuffType[] AllBuffTypes =
    {
        OverclockBuffType.Damage,
        OverclockBuffType.MaxHealth,
        OverclockBuffType.Speed,
        OverclockBuffType.FireRate
    };

    [Header("Support Movement")]
    [SerializeField] float preferredAllyRange = 4.5f;
    [SerializeField] float allySearchRadius = 40f;

    [Header("Overclock")]
    [Tooltip("Only used when this enemy is not bound to a room encounter.")]
    [SerializeField] float buffRadius = 14f;
    [SerializeField] float buffInterval = 3f;
    [SerializeField] float damageBuffMultiplier = 1.35f;
    [SerializeField] float maxHealthBuffMultiplier = 1.35f;
    [SerializeField] float speedBuffMultiplier = 1.25f;
    [SerializeField] float fireRateBuffMultiplier = 1.35f;
    [SerializeField] int maxStacksPerBuff = 1;
    [SerializeField] LayerMask allyMask = ~0;
    [SerializeField] ParticleSystem buffEffect;

    readonly List<EnemyAI> alliesInRange = new List<EnemyAI>();
    readonly List<EnemyAI> allySearchBuffer = new List<EnemyAI>();
    readonly List<OverclockBuffType> validBuffs = new List<OverclockBuffType>(4);
    readonly Collider[] overlapHits = new Collider[32];

    EnemyAI supportTarget;
    float buffTimer;

    protected override void Start()
    {
        base.Start();
        buffTimer = buffInterval * 0.35f;
    }

    protected override void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        supportTarget = FindClosestAlly();
        UpdateChase();
        UpdateContactDamage();

        buffTimer -= Time.deltaTime;
        if (buffTimer > 0f)
            return;

        buffTimer = buffInterval;
        TryBuffAlly();
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
        CollectAllies(allySearchBuffer, allySearchRadius, damagedOnly: false, roomWide: false);
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

    void TryBuffAlly()
    {
        // Prefer any living ally in the same room; fall back to radius if unbound.
        CollectAllies(alliesInRange, buffRadius, damagedOnly: false, roomWide: true);
        if (alliesInRange.Count == 0)
            return;

        EnemyAI target = null;
        for (int attempt = 0; attempt < alliesInRange.Count; attempt++)
        {
            EnemyAI candidate = alliesInRange[Random.Range(0, alliesInRange.Count)];
            if (!HasAnyAvailableBuff(candidate))
                continue;

            target = candidate;
            break;
        }

        if (target == null)
            return;

        if (!TryPickBuff(target, out OverclockBuffType buffType))
            return;

        if (!ApplyBuff(target, buffType))
            return;

        if (buffEffect != null)
        {
            buffEffect.transform.position = target.transform.position + Vector3.up * 1.2f;
            buffEffect.Play();
        }
    }

    void CollectAllies(List<EnemyAI> results, float radius, bool damagedOnly, bool roomWide)
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
            if (ally == null || !IsAlliedWith(ally) || (damagedOnly && !ally.IsDamaged) || results.Contains(ally))
                continue;

            results.Add(ally);
        }
    }

    bool HasAnyAvailableBuff(EnemyAI ally)
    {
        return ally.CanReceiveDamageBuff(maxStacksPerBuff) ||
               ally.CanReceiveMaxHealthBuff(maxStacksPerBuff) ||
               ally.CanReceiveSpeedBuff(maxStacksPerBuff) ||
               ally.CanReceiveFireRateBuff(maxStacksPerBuff);
    }

    bool TryPickBuff(EnemyAI ally, out OverclockBuffType buffType)
    {
        validBuffs.Clear();
        for (int i = 0; i < AllBuffTypes.Length; i++)
        {
            OverclockBuffType type = AllBuffTypes[i];
            if (CanReceive(ally, type))
                validBuffs.Add(type);
        }

        if (validBuffs.Count == 0)
        {
            buffType = default;
            return false;
        }

        buffType = validBuffs[Random.Range(0, validBuffs.Count)];
        return true;
    }

    bool CanReceive(EnemyAI ally, OverclockBuffType type)
    {
        return type switch
        {
            OverclockBuffType.Damage => ally.CanReceiveDamageBuff(maxStacksPerBuff),
            OverclockBuffType.MaxHealth => ally.CanReceiveMaxHealthBuff(maxStacksPerBuff),
            OverclockBuffType.Speed => ally.CanReceiveSpeedBuff(maxStacksPerBuff),
            OverclockBuffType.FireRate => ally.CanReceiveFireRateBuff(maxStacksPerBuff),
            _ => false
        };
    }

    bool ApplyBuff(EnemyAI ally, OverclockBuffType type)
    {
        return type switch
        {
            OverclockBuffType.Damage => ally.TryApplyDamageBuff(damageBuffMultiplier, maxStacksPerBuff),
            OverclockBuffType.MaxHealth => ally.TryApplyMaxHealthBuff(maxHealthBuffMultiplier, maxStacksPerBuff),
            OverclockBuffType.Speed => ally.TryApplySpeedBuff(speedBuffMultiplier, maxStacksPerBuff),
            OverclockBuffType.FireRate => ally.TryApplyFireRateBuff(fireRateBuffMultiplier, maxStacksPerBuff),
            _ => false
        };
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, buffRadius);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, preferredAllyRange);
    }
#endif
}
