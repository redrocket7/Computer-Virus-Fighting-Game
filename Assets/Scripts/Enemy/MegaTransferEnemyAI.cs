using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mega Transfer: tanks until half health, then summons damage containers and
/// redirects all further hits with no overload (preferring containers).
/// </summary>
public class MegaTransferEnemyAI : EnemyAI
{
    [Header("Transfer")]
    [SerializeField] ParticleSystem transferEffect;

    [Header("Damage Containers")]
    [SerializeField] GameObject damageContainerPrefab;
    [SerializeField, Range(0.05f, 0.95f)] float containerSpawnHealthFraction = 0.5f;
    [SerializeField] int containerSpawnCount = 4;

    readonly List<EnemyAI> roomEnemies = new List<EnemyAI>();
    readonly List<Vector3> occupiedSpawnBuffer = new List<Vector3>();
    bool containersSpawned;

    /// <summary>Normal Transfers / Link Guns must not dump damage onto the mega.</summary>
    public override bool CanBeTransferDamageTarget => false;

    public override void TakeDamage(float amount)
    {
        if (amount <= 0f || !IsAlive)
            return;

        if (HasActiveShield)
        {
            base.TakeDamage(amount);
            TrySpawnContainersFromHealth();
            return;
        }

        // Until containers are out, take real damage so the 50% threshold can be reached.
        if (!containersSpawned)
        {
            base.TakeDamage(amount);
            TrySpawnContainersFromHealth();
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
    }

    void TrySpawnContainersFromHealth()
    {
        if (containersSpawned || !IsAlive)
            return;

        float maxHp = Mathf.Max(0.01f, MaxHealth);
        if (CurrentHealth / maxHp > containerSpawnHealthFraction)
            return;

        SpawnDamageContainers();
    }

    void SpawnDamageContainers()
    {
        containersSpawned = true;

        if (damageContainerPrefab == null || containerSpawnCount <= 0)
            return;

        occupiedSpawnBuffer.Clear();
        occupiedSpawnBuffer.Add(transform.position);

        int spawned = 0;
        for (int i = 0; i < containerSpawnCount; i++)
        {
            Vector3 spawnPosition = transform.position;
            bool placed = BoundEncounter != null &&
                          BoundEncounter.TryChooseCombatSpawnPosition(
                              damageContainerPrefab,
                              occupiedSpawnBuffer,
                              out spawnPosition);

            if (!placed)
            {
                // Fallback near the mega if the room helper cannot place.
                Vector3 offset = Quaternion.Euler(0f, i * (360f / Mathf.Max(1, containerSpawnCount)), 0f) * Vector3.forward * 4f;
                spawnPosition = transform.position + offset;
                if (UnityEngine.AI.NavMesh.SamplePosition(
                        spawnPosition,
                        out UnityEngine.AI.NavMeshHit hit,
                        6f,
                        UnityEngine.AI.NavMesh.AllAreas))
                {
                    spawnPosition = hit.position;
                }
            }

            GameObject instance = Instantiate(
                damageContainerPrefab,
                spawnPosition,
                Quaternion.identity);

            EnemyAI child = instance.GetComponent<EnemyAI>() ?? instance.GetComponentInChildren<EnemyAI>();
            if (child != null && BoundEncounter != null)
                BoundEncounter.RegisterEnemy(child);

            occupiedSpawnBuffer.Add(spawnPosition);
            spawned++;
        }

        if (spawned > 0)
            Debug.Log($"{name} spawned {spawned} damage containers.", this);
    }

    EnemyAI FindTransferTarget()
    {
        RoomEncounter encounter = BoundEncounter;
        if (encounter == null)
            return null;

        encounter.GetLivingEnemies(roomEnemies);

        // Prefer living damage containers (least stored first to spread load).
        EnemyAI bestContainer = null;
        float lowestStored = float.MaxValue;
        for (int i = 0; i < roomEnemies.Count; i++)
        {
            EnemyAI ally = roomEnemies[i];
            if (ally == null || ally == this || !ally.IsAlive)
                continue;

            if (ally is not DamageContainerEnemyAI container)
                continue;

            if (container.StoredDamage < lowestStored)
            {
                lowestStored = container.StoredDamage;
                bestContainer = container;
            }
        }

        if (bestContainer != null)
            return bestContainer;

        // Fallback: weakest valid non-transfer ally (same rules as Transfer).
        EnemyAI weakest = null;
        float lowestHealth = float.MaxValue;
        for (int i = 0; i < roomEnemies.Count; i++)
        {
            EnemyAI ally = roomEnemies[i];
            if (ally == null || ally == this || !ally.IsAlive)
                continue;

            if (ally is TransferEnemyAI ||
                ally is MegaTransferEnemyAI ||
                ally is LinkGunEnemyAI ||
                !ally.CanBeTransferDamageTarget)
            {
                continue;
            }

            if (ally.CurrentHealth < lowestHealth)
            {
                lowestHealth = ally.CurrentHealth;
                weakest = ally;
            }
        }

        return weakest;
    }
}
