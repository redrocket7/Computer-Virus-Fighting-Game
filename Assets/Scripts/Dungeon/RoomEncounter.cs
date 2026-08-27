using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One-time Gungeon-style encounter: lock doors, spawn enemies, unlock when all are dead.
/// </summary>
public class RoomEncounter : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] GameObject[] enemyPrefabs;
    [SerializeField] int spawnCount = 3;
    [Tooltip("Seconds after spawning before enemies can chase or attack.")]
    public float attackDelay = 1.25f;

    [Header("Random NavMesh Spawning")]
    [SerializeField] float spawnSampleRadius = 3f;
    [SerializeField] float edgePadding = 5f;
    [SerializeField] float minimumSpawnSeparation = 4f;
    [Tooltip("Enemies will not spawn this close to the player.")]
    [SerializeField] float minimumPlayerDistance = 12f;
    [SerializeField] int spawnAttemptsPerEnemy = 48;
    [Tooltip("Reject samples this far above the room floor so nothing spawns on top of props.")]
    [SerializeField] float maximumSpawnHeight = 1.5f;

    [Header("Buff Enemy Limits")]
    [Min(0)]
    [Tooltip("Max Overclock enemies that can spawn in this room. 0 = none.")]
    [SerializeField] int maxOverclockPerRoom = 1;
    [Min(0)]
    [Tooltip("Max Repair enemies that can spawn in this room. 0 = none.")]
    [SerializeField] int maxRepairPerRoom = 1;

    [Header("Random Shields")]
    [Range(0f, 1f)]
    [Tooltip("Chance each spawned enemy gets a shield dome.")]
    [SerializeField] float shieldSpawnChance = 0.2f;
    [SerializeField] float shieldHealth = 4f;

    GameObject bonusMegaPrefab;

    RoomDefinition room;
    readonly HashSet<EnemyAI> livingEnemies = new HashSet<EnemyAI>();
    bool started;
    bool cleared;

    public bool IsCleared => cleared;
    public bool IsInProgress => started && !cleared;
    public bool IsRevealed => cleared || !enabled;
    public int LivingEnemyCount => livingEnemies.Count;
    public event System.Action Started;
    public event System.Action Cleared;

    public void GetLivingEnemies(List<EnemyAI> results)
    {
        results.Clear();
        foreach (EnemyAI enemy in livingEnemies)
        {
            if (enemy != null && enemy.IsAlive)
                results.Add(enemy);
        }
    }

    public void SetEnemyPrefabs(GameObject[] prefabs)
    {
        enemyPrefabs = prefabs;
    }

    public void SetAttackDelay(float seconds)
    {
        attackDelay = Mathf.Max(0f, seconds);
    }

    public void SetBuffEnemyLimits(int overclock, int repair)
    {
        maxOverclockPerRoom = Mathf.Max(0, overclock);
        maxRepairPerRoom = Mathf.Max(0, repair);
    }

    public void SetShieldSpawnSettings(float chance, float health)
    {
        shieldSpawnChance = Mathf.Clamp01(chance);
        shieldHealth = Mathf.Max(0.1f, health);
    }

    public void SetBonusMegaEnemy(GameObject prefab)
    {
        bonusMegaPrefab = prefab;
    }

    void Awake()
    {
        room = GetComponent<RoomDefinition>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (started || !enabled)
            return;

        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        BeginEncounter();
    }

    public void BeginEncounter()
    {
        if (started)
            return;

        started = true;
        if (room == null)
            room = GetComponent<RoomDefinition>();

        SetDoorsLocked(true);
        SpawnEnemies();

        if (livingEnemies.Count == 0)
            CompleteEncounter();
        else
            Started?.Invoke();
    }

    public void RegisterEnemy(EnemyAI enemy)
    {
        if (enemy == null || cleared)
            return;

        livingEnemies.Add(enemy);
        enemy.BindEncounter(this);
    }

    public void NotifyEnemyDestroyed(EnemyAI enemy)
    {
        if (enemy == null)
            return;

        livingEnemies.Remove(enemy);
        if (started && livingEnemies.Count == 0)
            CompleteEncounter();
    }

    void SpawnEnemies()
    {
        if (room == null)
            return;

        bool hasNormalPool = enemyPrefabs != null && enemyPrefabs.Length > 0 && spawnCount > 0;
        bool hasMega = bonusMegaPrefab != null;
        if (!hasNormalPool && !hasMega)
            return;

        var occupiedPositions = new List<Vector3>(spawnCount + (hasMega ? 1 : 0));
        var eligiblePrefabs = new List<GameObject>(hasNormalPool ? enemyPrefabs.Length : 0);
        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindAnyObjectByType<PlayerController>();
        int spawnedOverclock = 0;
        int spawnedRepair = 0;

        if (hasMega)
            TrySpawnEnemy(bonusMegaPrefab, occupiedPositions, player, ref spawnedOverclock, ref spawnedRepair);

        if (!hasNormalPool)
            return;

        for (int i = 0; i < spawnCount; i++)
        {
            GameObject prefab = ChooseEnemyPrefab(
                eligiblePrefabs,
                spawnedOverclock,
                spawnedRepair);
            if (prefab == null)
                continue;

            TrySpawnEnemy(prefab, occupiedPositions, player, ref spawnedOverclock, ref spawnedRepair);
        }
    }

    void TrySpawnEnemy(
        GameObject prefab,
        List<Vector3> occupiedPositions,
        PlayerController player,
        ref int spawnedOverclock,
        ref int spawnedRepair)
    {
        if (prefab == null)
            return;

        if (!TryChooseRandomSpawnPosition(
                prefab,
                occupiedPositions,
                player != null ? player.transform : null,
                out Vector3 spawnPosition))
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh spawn point for {prefab.name} in {room.name}.",
                this);
            return;
        }

        GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
        EnemyAI ai = instance.GetComponent<EnemyAI>() ?? instance.GetComponentInChildren<EnemyAI>();
        RegisterEnemy(ai);
        if (ai != null && attackDelay > 0f)
            ai.SetCombatHoldoff(attackDelay);
        TryGrantRandomShield(ai);
        occupiedPositions.Add(spawnPosition);

        CountBuffSpawn(ai ?? GetPrefabAi(prefab), ref spawnedOverclock, ref spawnedRepair);
    }

    void TryGrantRandomShield(EnemyAI ai)
    {
        if (ai == null || shieldSpawnChance <= 0f || shieldHealth <= 0f)
            return;

        if (Random.value > shieldSpawnChance)
            return;

        ai.GrantShield(shieldHealth);
    }

    GameObject ChooseEnemyPrefab(
        List<GameObject> eligiblePrefabs,
        int spawnedOverclock,
        int spawnedRepair)
    {
        eligiblePrefabs.Clear();
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
                continue;

            if (!CanSpawnBuffPrefab(prefab, spawnedOverclock, spawnedRepair))
                continue;

            eligiblePrefabs.Add(prefab);
        }

        if (eligiblePrefabs.Count == 0)
            return null;

        return eligiblePrefabs[Random.Range(0, eligiblePrefabs.Count)];
    }

    bool CanSpawnBuffPrefab(
        GameObject prefab,
        int spawnedOverclock,
        int spawnedRepair)
    {
        EnemyAI ai = GetPrefabAi(prefab);
        if (ai is OverclockEnemyAI)
            return spawnedOverclock < maxOverclockPerRoom;
        if (ai is RepairEnemyAI)
            return spawnedRepair < maxRepairPerRoom;

        return true;
    }

    static void CountBuffSpawn(
        EnemyAI ai,
        ref int spawnedOverclock,
        ref int spawnedRepair)
    {
        if (ai is OverclockEnemyAI)
            spawnedOverclock++;
        else if (ai is RepairEnemyAI)
            spawnedRepair++;
    }

    static EnemyAI GetPrefabAi(GameObject prefab)
    {
        if (prefab == null)
            return null;

        return prefab.GetComponent<EnemyAI>() ?? prefab.GetComponentInChildren<EnemyAI>();
    }

    bool TryChooseRandomSpawnPosition(
        GameObject prefab,
        IReadOnlyList<Vector3> occupiedPositions,
        Transform player,
        out Vector3 spawnPosition)
    {
        NavMeshAgent prefabAgent =
            prefab.GetComponent<NavMeshAgent>() ??
            prefab.GetComponentInChildren<NavMeshAgent>();

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = prefabAgent != null ? prefabAgent.agentTypeID : 0,
            areaMask = prefabAgent != null ? prefabAgent.areaMask : NavMesh.AllAreas
        };

        float halfWidth = Mathf.Max(0.5f, room.FootprintSize.x * 0.5f - edgePadding);
        float halfDepth = Mathf.Max(0.5f, room.FootprintSize.y * 0.5f - edgePadding);
        Rect allowedFootprint = room.GetWorldFootprint(-edgePadding);
        float separationSqr = minimumSpawnSeparation * minimumSpawnSeparation;
        float playerDistanceSqr = minimumPlayerDistance * minimumPlayerDistance;
        var candidates = new List<Vector3>();

        int attempts = Mathf.Max(1, spawnAttemptsPerEnemy);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 localPoint = room.FootprintCenter + new Vector3(
                Random.Range(-halfWidth, halfWidth),
                1f,
                Random.Range(-halfDepth, halfDepth));
            Vector3 desired = room.transform.TransformPoint(localPoint);

            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, spawnSampleRadius, filter))
                continue;

            if (hit.position.y - room.transform.position.y > maximumSpawnHeight)
                continue;

            Vector2 hitXZ = new Vector2(hit.position.x, hit.position.z);
            if (!allowedFootprint.Contains(hitXZ))
                continue;

            if (player != null)
            {
                Vector3 playerOffset = hit.position - player.position;
                playerOffset.y = 0f;
                if (playerOffset.sqrMagnitude < playerDistanceSqr)
                    continue;
            }

            bool tooClose = false;
            foreach (Vector3 occupied in occupiedPositions)
            {
                Vector3 offset = hit.position - occupied;
                offset.y = 0f;
                if (offset.sqrMagnitude < separationSqr)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            candidates.Add(hit.position);
        }

        if (candidates.Count == 0)
        {
            spawnPosition = default;
            return false;
        }

        spawnPosition = candidates[Random.Range(0, candidates.Count)];
        return true;
    }

    void CompleteEncounter()
    {
        if (cleared)
            return;

        cleared = true;
        SetDoorsLocked(false);
        Cleared?.Invoke();
    }

    void SetDoorsLocked(bool locked)
    {
        if (room == null)
            return;

        foreach (var socket in room.Sockets)
        {
            if (socket == null)
                continue;

            if (!socket.IsConnected)
                continue;

            socket.SetLocked(locked);
            socket.Connected?.SetLocked(locked);
        }
    }
}
