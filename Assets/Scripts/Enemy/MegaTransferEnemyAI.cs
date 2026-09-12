using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

    readonly List<EnemyAI> roomEnemies = new List<EnemyAI>(16);
    readonly List<DamageContainerEnemyAI> livingContainers = new List<DamageContainerEnemyAI>(8);
    readonly List<Vector3> occupiedSpawnBuffer = new List<Vector3>(8);
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

        if (CurrentHealth > MaxHealth * containerSpawnHealthFraction)
            return;

        SpawnDamageContainers();
    }

    void SpawnDamageContainers()
    {
        containersSpawned = true;

        if (damageContainerPrefab == null || containerSpawnCount <= 0)
            return;

        RoomEncounter encounter = BoundEncounter;
        occupiedSpawnBuffer.Clear();
        occupiedSpawnBuffer.Add(transform.position);
        livingContainers.Clear();

        float angleStep = 360f / containerSpawnCount;
        Vector3 selfPos = transform.position;

        for (int i = 0; i < containerSpawnCount; i++)
        {
            Vector3 spawnPosition = selfPos;
            bool placed = encounter != null &&
                          encounter.TryChooseCombatSpawnPosition(
                              damageContainerPrefab,
                              occupiedSpawnBuffer,
                              out spawnPosition);

            if (!placed)
            {
                Vector3 offset = Quaternion.Euler(0f, i * angleStep, 0f) * Vector3.forward * 4f;
                spawnPosition = selfPos + offset;
                if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    spawnPosition = hit.position;
            }

            GameObject instance = Instantiate(damageContainerPrefab, spawnPosition, Quaternion.identity);
            DamageContainerEnemyAI container = instance.GetComponentInChildren<DamageContainerEnemyAI>();

            if (container != null)
            {
                livingContainers.Add(container);
                if (encounter != null)
                    encounter.RegisterEnemy(container);
            }

            occupiedSpawnBuffer.Add(spawnPosition);
        }
    }

    EnemyAI FindTransferTarget()
    {
        // Prefer tracked containers (least stored first) without scanning the whole room.
        DamageContainerEnemyAI bestContainer = null;
        float lowestStored = float.MaxValue;

        for (int i = livingContainers.Count - 1; i >= 0; i--)
        {
            DamageContainerEnemyAI container = livingContainers[i];
            if (container == null || !container.IsAlive)
            {
                livingContainers.RemoveAt(i);
                continue;
            }

            float stored = container.StoredDamage;
            if (stored < lowestStored)
            {
                lowestStored = stored;
                bestContainer = container;
            }
        }

        if (bestContainer != null)
            return bestContainer;

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

            // Skip other redirectors; CanBeTransferDamageTarget covers Link Gun / Mega Transfer.
            if (ally is TransferEnemyAI || !ally.CanBeTransferDamageTarget)
                continue;

            float hp = ally.CurrentHealth;
            if (hp < lowestHealth)
            {
                lowestHealth = hp;
                weakest = ally;
            }
        }

        return weakest;
    }
}
