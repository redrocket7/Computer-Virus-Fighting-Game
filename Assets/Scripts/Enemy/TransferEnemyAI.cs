using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When shot, redirects damage to the lowest-current-HP ally in the room instead of taking it.
/// After enough total damage has been redirected, overloads and must take damage itself until the cooldown ends.
/// If no valid ally exists, takes the damage normally.
/// </summary>
public class TransferEnemyAI : EnemyAI
{
    [Header("Transfer")]
    [Tooltip("Optional VFX played on the ally that receives redirected damage.")]
    [SerializeField] ParticleSystem transferEffect;
    [Tooltip("Total damage that can be redirected before this enemy overloads and takes damage itself.")]
    [SerializeField] float maxDamageBeforeOverload = 10f;
    [Tooltip("How long overload lasts before transfers work again.")]
    [SerializeField] float overloadDuration = 2.5f;

    readonly List<EnemyAI> roomEnemies = new List<EnemyAI>();
    float damageRedirected;
    float overloadTimer;

    bool IsOverloaded => overloadTimer > 0f;

    protected override void Update()
    {
        if (overloadTimer > 0f)
            overloadTimer -= Time.deltaTime;

        base.Update();
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

        EnemyAI weakest = FindWeakestAlly();
        if (weakest == null)
        {
            base.TakeDamage(amount);
            return;
        }

        if (transferEffect != null)
        {
            transferEffect.transform.position = weakest.transform.position + Vector3.up * 1.2f;
            transferEffect.Play();
        }

        weakest.TakeDamage(amount);
        damageRedirected += amount;

        if (damageRedirected >= Mathf.Max(0.01f, maxDamageBeforeOverload))
        {
            damageRedirected = 0f;
            overloadTimer = Mathf.Max(0.1f, overloadDuration);
        }
    }

    EnemyAI FindWeakestAlly()
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

            // Avoid bouncing damage between Transfer enemies, and never feed Link Guns.
            if (ally is TransferEnemyAI || !ally.CanBeTransferDamageTarget)
                continue;

            if (ally.CurrentHealth < lowestHealth)
            {
                lowestHealth = ally.CurrentHealth;
                weakest = ally;
            }
        }

        return weakest;
    }
}
