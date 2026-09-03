using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum RoomModifierType
{
    None = 0,
    /// <summary>Guarantees an extra Repair or Overclock support in the room.</summary>
    RaidArray = 1,
    /// <summary>When the current pack dies, another wave of enemies spawns.</summary>
    BootLoop = 2,
    /// <summary>Player shots have a chance to fizzle while the encounter is active.</summary>
    PacketLoss = 3
}

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
    [Min(0)]
    [Tooltip("Max Shielder enemies that can spawn in this room. 0 = none.")]
    [SerializeField] int maxShielderPerRoom = 1;

    [Header("Random Shields")]
    [Range(0f, 1f)]
    [Tooltip("Chance each spawned enemy gets a shield dome.")]
    [SerializeField] float shieldSpawnChance = 0.2f;
    [SerializeField] float shieldHealth = 4f;

    [Header("Health Pickup Drop")]
    [SerializeField] float healthPickupMinCenterDistance = 14f;
    [SerializeField] float healthPickupSampleRadius = 3f;
    [SerializeField] int healthPickupSpawnAttempts = 48;

    GameObject healthPickupPrefab;
    float healthPickupHealAmount;
    bool pendingHealthPickupDrop;
    GameObject bonusMegaPrefab;
    RoomModifierType roomModifier = RoomModifierType.None;
    int bootLoopWavesRemaining;
    float bootLoopSpawnDelay = 1.5f;
    float bootLoopAttackDelay = 1.25f;
    float packetLossFizzleChance;
    bool bootLoopWavePending;

    RoomDefinition room;
    readonly HashSet<EnemyAI> livingEnemies = new HashSet<EnemyAI>();
    readonly List<BootLoopSpawnMarker> activeSpawnMarkers = new List<BootLoopSpawnMarker>();
    bool started;
    bool cleared;

    public bool IsCleared => cleared;
    public bool IsInProgress => started && !cleared;
    public bool IsRevealed => cleared || !enabled;
    public int LivingEnemyCount => livingEnemies.Count;
    public RoomModifierType RoomModifier => roomModifier;
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

    public void SetBuffEnemyLimits(int overclock, int repair, int shielder)
    {
        maxOverclockPerRoom = Mathf.Max(0, overclock);
        maxRepairPerRoom = Mathf.Max(0, repair);
        maxShielderPerRoom = Mathf.Max(0, shielder);
    }

    public void SetShieldSpawnSettings(float chance, float health)
    {
        shieldSpawnChance = Mathf.Clamp01(chance);
        shieldHealth = Mathf.Max(0.1f, health);
    }

    public void SetSpawnCount(int count)
    {
        spawnCount = Mathf.Max(0, count);
    }

    public int SpawnCount => spawnCount;

    public void SetBonusMegaEnemy(GameObject prefab)
    {
        bonusMegaPrefab = prefab;
    }

    public void SetRoomModifier(RoomModifierType modifier)
    {
        roomModifier = modifier;
        if (modifier != RoomModifierType.BootLoop)
            bootLoopWavesRemaining = 0;
    }

    public void SetBootLoopExtraWaves(int extraWaves)
    {
        bootLoopWavesRemaining = Mathf.Max(0, extraWaves);
    }

    public void SetBootLoopTiming(float spawnDelay, float attackDelay)
    {
        bootLoopSpawnDelay = Mathf.Max(0f, spawnDelay);
        bootLoopAttackDelay = Mathf.Max(0f, attackDelay);
    }

    public void SetPacketLossSettings(float fizzleChance)
    {
        packetLossFizzleChance = Mathf.Clamp01(fizzleChance);
    }

    public void ConfigureHealthPickupDrop(GameObject prefab, float healAmount)
    {
        healthPickupPrefab = prefab;
        healthPickupHealAmount = Mathf.Max(0f, healAmount);
        pendingHealthPickupDrop = prefab != null && healthPickupHealAmount > 0f;
    }

    public void SetHealthPickupSpawnSettings(
        float minCenterDistance,
        float sampleRadius,
        int attempts)
    {
        healthPickupMinCenterDistance = Mathf.Max(0f, minCenterDistance);
        healthPickupSampleRadius = Mathf.Max(0.25f, sampleRadius);
        healthPickupSpawnAttempts = Mathf.Max(1, attempts);
    }

    public void ClearHealthPickupDrop()
    {
        healthPickupPrefab = null;
        healthPickupHealAmount = 0f;
        pendingHealthPickupDrop = false;
    }

    void Awake()
    {
        room = GetComponent<RoomDefinition>();
    }

    void OnDestroy()
    {
        ClearSpawnMarkers();
        EndPacketLossEffect();
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
        BeginPacketLossEffect();
        SpawnWave(includeBonuses: true, holdoffOverride: -1f);

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
        if (!started || cleared || livingEnemies.Count > 0 || bootLoopWavePending)
            return;

        if (TryBeginBootLoopWave())
            return;

        CompleteEncounter();
    }

    bool TryBeginBootLoopWave()
    {
        if (roomModifier != RoomModifierType.BootLoop || bootLoopWavesRemaining <= 0)
            return false;

        bootLoopWavesRemaining--;
        bootLoopWavePending = true;
        StartCoroutine(SpawnBootLoopWaveRoutine());
        return true;
    }

    IEnumerator SpawnBootLoopWaveRoutine()
    {
        List<PlannedSpawn> planned = PlanWaveSpawns(includeBonuses: false);
        if (planned.Count == 0)
        {
            bootLoopWavePending = false;
            Debug.LogWarning(
                $"Boot Loop room '{room.name}' failed to plan a reinforcement wave.",
                this);
            CompleteEncounter();
            yield break;
        }

        ShowSpawnMarkers(planned);

        float delay = bootLoopSpawnDelay;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        ClearSpawnMarkers();
        SpawnPlannedWave(planned, bootLoopAttackDelay);
        bootLoopWavePending = false;

        if (livingEnemies.Count == 0)
        {
            Debug.LogWarning(
                $"Boot Loop room '{room.name}' failed to spawn a reinforcement wave.",
                this);
            CompleteEncounter();
        }
    }

    struct PlannedSpawn
    {
        public GameObject Prefab;
        public Vector3 Position;
        public float MarkerRadius;
    }

    List<PlannedSpawn> PlanWaveSpawns(bool includeBonuses)
    {
        var planned = new List<PlannedSpawn>();
        if (room == null)
            return planned;

        bool hasNormalPool = enemyPrefabs != null && enemyPrefabs.Length > 0 && spawnCount > 0;
        bool hasMega = includeBonuses && bonusMegaPrefab != null;
        bool hasRaidSupport = includeBonuses &&
                              roomModifier == RoomModifierType.RaidArray &&
                              enemyPrefabs != null &&
                              enemyPrefabs.Length > 0;
        if (!hasNormalPool && !hasMega && !hasRaidSupport)
            return planned;

        var occupiedPositions = new List<Vector3>(spawnCount + (hasMega ? 1 : 0) + 1);
        var eligiblePrefabs = new List<GameObject>(hasNormalPool ? enemyPrefabs.Length : 0);
        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindAnyObjectByType<PlayerController>();
        Transform playerTransform = player != null ? player.transform : null;
        int spawnedOverclock = 0;
        int spawnedRepair = 0;
        int spawnedShielder = 0;

        if (hasMega)
            TryPlanSpawn(bonusMegaPrefab, occupiedPositions, playerTransform, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);

        if (hasRaidSupport)
        {
            GameObject support = ChooseRaidSupportPrefab(spawnedOverclock, spawnedRepair, spawnedShielder);
            if (support != null)
                TryPlanSpawn(support, occupiedPositions, playerTransform, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        }

        if (!hasNormalPool)
            return planned;

        for (int i = 0; i < spawnCount; i++)
        {
            GameObject prefab = ChooseEnemyPrefab(
                eligiblePrefabs,
                spawnedOverclock,
                spawnedRepair,
                spawnedShielder);
            if (prefab == null)
                continue;

            TryPlanSpawn(prefab, occupiedPositions, playerTransform, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        }

        return planned;
    }

    void TryPlanSpawn(
        GameObject prefab,
        List<Vector3> occupiedPositions,
        Transform player,
        List<PlannedSpawn> planned,
        ref int spawnedOverclock,
        ref int spawnedRepair,
        ref int spawnedShielder)
    {
        if (prefab == null)
            return;

        if (!TryChooseRandomSpawnPosition(prefab, occupiedPositions, player, out Vector3 spawnPosition))
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh spawn point for {prefab.name} in {room.name}.",
                this);
            return;
        }

        occupiedPositions.Add(spawnPosition);
        CountBuffSpawn(GetPrefabAi(prefab), ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        planned.Add(new PlannedSpawn
        {
            Prefab = prefab,
            Position = spawnPosition,
            MarkerRadius = EstimateMarkerRadius(prefab)
        });
    }

    static float EstimateMarkerRadius(GameObject prefab)
    {
        NavMeshAgent agent =
            prefab.GetComponent<NavMeshAgent>() ??
            prefab.GetComponentInChildren<NavMeshAgent>();
        if (agent != null && agent.radius > 0.1f)
            return Mathf.Clamp(agent.radius * 1.35f, 0.75f, 2.2f);
        return 1.1f;
    }

    void ShowSpawnMarkers(List<PlannedSpawn> planned)
    {
        ClearSpawnMarkers();
        for (int i = 0; i < planned.Count; i++)
        {
            PlannedSpawn spawn = planned[i];
            BootLoopSpawnMarker marker = BootLoopSpawnMarker.Create(spawn.Position, spawn.MarkerRadius);
            activeSpawnMarkers.Add(marker);
        }
    }

    void ClearSpawnMarkers()
    {
        for (int i = 0; i < activeSpawnMarkers.Count; i++)
        {
            if (activeSpawnMarkers[i] != null)
                Destroy(activeSpawnMarkers[i].gameObject);
        }

        activeSpawnMarkers.Clear();
    }

    void SpawnPlannedWave(List<PlannedSpawn> planned, float holdoff)
    {
        for (int i = 0; i < planned.Count; i++)
        {
            PlannedSpawn spawn = planned[i];
            if (spawn.Prefab == null)
                continue;

            GameObject instance = Instantiate(spawn.Prefab, spawn.Position, Quaternion.identity);
            EnemyAI ai = instance.GetComponent<EnemyAI>() ?? instance.GetComponentInChildren<EnemyAI>();
            RegisterEnemy(ai);
            if (ai != null && holdoff > 0f)
                ai.SetCombatHoldoff(holdoff);
            TryGrantRandomShield(ai);
        }
    }

    void SpawnWave(bool includeBonuses, float holdoffOverride)
    {
        List<PlannedSpawn> planned = PlanWaveSpawns(includeBonuses);
        float holdoff = holdoffOverride >= 0f ? holdoffOverride : attackDelay;
        SpawnPlannedWave(planned, holdoff);
    }

    GameObject ChooseRaidSupportPrefab(int spawnedOverclock, int spawnedRepair, int spawnedShielder)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            return null;

        var supports = new List<GameObject>(4);
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
                continue;

            EnemyAI ai = GetPrefabAi(prefab);
            if (ai == null || !ai.IsSupportEnemy)
                continue;

            if (!CanSpawnBuffPrefab(prefab, spawnedOverclock, spawnedRepair, spawnedShielder))
                continue;

            supports.Add(prefab);
        }

        if (supports.Count == 0)
            return null;

        return supports[Random.Range(0, supports.Count)];
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
        int spawnedRepair,
        int spawnedShielder)
    {
        eligiblePrefabs.Clear();
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
                continue;

            if (!CanSpawnBuffPrefab(prefab, spawnedOverclock, spawnedRepair, spawnedShielder))
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
        int spawnedRepair,
        int spawnedShielder)
    {
        EnemyAI ai = GetPrefabAi(prefab);
        if (ai is OverclockEnemyAI)
            return spawnedOverclock < maxOverclockPerRoom;
        if (ai is RepairEnemyAI)
            return spawnedRepair < maxRepairPerRoom;
        if (ai is ShielderEnemyAI)
            return spawnedShielder < maxShielderPerRoom;

        return true;
    }

    static void CountBuffSpawn(
        EnemyAI ai,
        ref int spawnedOverclock,
        ref int spawnedRepair,
        ref int spawnedShielder)
    {
        if (ai is OverclockEnemyAI)
            spawnedOverclock++;
        else if (ai is RepairEnemyAI)
            spawnedRepair++;
        else if (ai is ShielderEnemyAI)
            spawnedShielder++;
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

    void TrySpawnHealthPickup()
    {
        if (!pendingHealthPickupDrop || healthPickupPrefab == null)
            return;

        pendingHealthPickupDrop = false;

        if (!TryChooseHealthPickupPosition(out Vector3 spawnPosition))
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh position for a health pickup in {room.name}.",
                this);
            return;
        }

        GameObject instance = Instantiate(healthPickupPrefab, spawnPosition, Quaternion.identity);
        if (instance.TryGetComponent(out HealthPickup pickup))
            pickup.Configure(healthPickupHealAmount);
    }

    bool TryChooseHealthPickupPosition(out Vector3 spawnPosition)
    {
        spawnPosition = default;
        if (room == null)
            room = GetComponent<RoomDefinition>();
        if (room == null)
            return false;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = 0,
            areaMask = NavMesh.AllAreas
        };

        float halfWidth = Mathf.Max(0.5f, room.FootprintSize.x * 0.5f - edgePadding);
        float halfDepth = Mathf.Max(0.5f, room.FootprintSize.y * 0.5f - edgePadding);
        Rect allowedFootprint = room.GetWorldFootprint(-edgePadding);
        Vector3 center = room.transform.TransformPoint(room.FootprintCenter);
        float minCenterDistanceSqr = healthPickupMinCenterDistance * healthPickupMinCenterDistance;
        var candidates = new List<Vector3>();

        int attempts = Mathf.Max(1, healthPickupSpawnAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 localPoint = room.FootprintCenter + new Vector3(
                Random.Range(-halfWidth, halfWidth),
                1f,
                Random.Range(-halfDepth, halfDepth));
            Vector3 desired = room.transform.TransformPoint(localPoint);

            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, healthPickupSampleRadius, filter))
                continue;

            if (hit.position.y - room.transform.position.y > maximumSpawnHeight)
                continue;

            Vector2 hitXZ = new Vector2(hit.position.x, hit.position.z);
            if (!allowedFootprint.Contains(hitXZ))
                continue;

            Vector3 centerOffset = hit.position - center;
            centerOffset.y = 0f;
            if (centerOffset.sqrMagnitude < minCenterDistanceSqr)
                continue;

            candidates.Add(hit.position);
        }

        if (candidates.Count == 0)
            return false;

        spawnPosition = candidates[Random.Range(0, candidates.Count)];
        return true;
    }

    void CompleteEncounter()
    {
        if (cleared)
            return;

        cleared = true;
        bootLoopWavePending = false;
        ClearSpawnMarkers();
        EndPacketLossEffect();
        SetDoorsLocked(false);
        TrySpawnHealthPickup();
        Cleared?.Invoke();
    }

    void BeginPacketLossEffect()
    {
        if (roomModifier != RoomModifierType.PacketLoss || packetLossFizzleChance <= 0f)
            return;

        PacketLossCombatEffect.Begin(packetLossFizzleChance);
    }

    void EndPacketLossEffect()
    {
        if (roomModifier != RoomModifierType.PacketLoss)
            return;

        PacketLossCombatEffect.End();
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
