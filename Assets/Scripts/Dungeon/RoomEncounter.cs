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
    PacketLoss = 3,
    /// <summary>One random enemy respawns once after death, weaker but faster.</summary>
    CorruptedSave = 4
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

    [Header("Depth Threat Mix")]
    [SerializeField] bool scaleEnemyMixByDepth = true;
    [SerializeField] int encounterDepth;
    [SerializeField] int enemyMixFullDepth = 8;
    [SerializeField] float easyThreatExponent = -1.25f;
    [SerializeField] float hardThreatExponent = 1.4f;

    [Header("Health Pickup Drop")]
    [SerializeField] float healthPickupMinCenterDistance = 14f;
    [SerializeField] float healthPickupSampleRadius = 3f;
    [SerializeField] int healthPickupSpawnAttempts = 48;

    GameObject healthPickupPrefab;
    float healthPickupHealAmount;
    bool pendingHealthPickupDrop;
    GameObject usbDashPickupPrefab;
    bool pendingUsbDashDrop;
    GameObject goatDashPickupPrefab;
    bool pendingGoatDashDrop;
    readonly List<PendingWeaponPickupDrop> pendingWeaponPickupDrops = new List<PendingWeaponPickupDrop>();
    GameObject bonusMegaPrefab;
    RoomModifierType roomModifier = RoomModifierType.None;
    int bootLoopWavesRemaining;
    float bootLoopSpawnDelay = 1.5f;
    float bootLoopAttackDelay = 1.25f;
    float packetLossFizzleChance;
    bool bootLoopWavePending;
    EnemyAI corruptedSaveHost;
    GameObject corruptedSavePrefab;
    bool corruptedSaveConsumed;
    bool corruptedSavePending;
    float corruptedSaveHealthMultiplier = 0.5f;
    float corruptedSaveSpeedMultiplier = 1.4f;
    float corruptedSaveRespawnDelay = 0.6f;
    [Header("Spawn Telegraph")]
    [SerializeField] float spawnTelegraphMinDelay = 1f;
    [SerializeField] float spawnTelegraphMaxDelay = 6f;
    bool initialWavePending;

    RoomDefinition room;
    readonly HashSet<EnemyAI> livingEnemies = new HashSet<EnemyAI>();
    readonly List<BootLoopSpawnMarker> activeSpawnMarkers = new List<BootLoopSpawnMarker>();
    readonly Dictionary<GameObject, EnemyAI> prefabAiCache = new Dictionary<GameObject, EnemyAI>();
    readonly List<GameObject> eligiblePrefabBuffer = new List<GameObject>(32);
    readonly List<float> spawnWeightBuffer = new List<float>(32);
    readonly List<Vector3> occupiedSpawnBuffer = new List<Vector3>(16);
    readonly List<PlannedSpawn> plannedSpawnBuffer = new List<PlannedSpawn>(16);
    float mixExponent;
    bool started;
    bool cleared;

    public bool IsCleared => cleared;
    public bool IsInProgress => started && !cleared;
    public bool IsRevealed => cleared || !enabled;
    public int LivingEnemyCount => livingEnemies.Count;
    public RoomModifierType RoomModifier => roomModifier;
    public int EncounterDepth => encounterDepth;
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
        prefabAiCache.Clear();
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

    public void SetEncounterDepth(int depth)
    {
        encounterDepth = Mathf.Max(0, depth);
        RefreshMixExponent();
    }

    public void SetDepthThreatMixSettings(
        bool enabled,
        int fullDepth,
        float easyExponent,
        float hardExponent)
    {
        scaleEnemyMixByDepth = enabled;
        enemyMixFullDepth = Mathf.Max(1, fullDepth);
        easyThreatExponent = easyExponent;
        hardThreatExponent = hardExponent;
        RefreshMixExponent();
    }

    void RefreshMixExponent()
    {
        float depth01 = Mathf.Clamp01(encounterDepth / (float)Mathf.Max(1, enemyMixFullDepth));
        mixExponent = Mathf.Lerp(easyThreatExponent, hardThreatExponent, depth01);
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

        if (modifier != RoomModifierType.CorruptedSave)
        {
            corruptedSaveHost = null;
            corruptedSavePrefab = null;
            corruptedSaveConsumed = false;
            corruptedSavePending = false;
        }
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

    public void SetCorruptedSaveSettings(
        float healthMultiplier,
        float speedMultiplier,
        float respawnDelay)
    {
        corruptedSaveHealthMultiplier = Mathf.Clamp(healthMultiplier, 0.05f, 1f);
        corruptedSaveSpeedMultiplier = Mathf.Max(1f, speedMultiplier);
        corruptedSaveRespawnDelay = Mathf.Max(0f, respawnDelay);
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

    public void ConfigureUsbDashPickupDrop(GameObject prefab)
    {
        usbDashPickupPrefab = prefab;
        pendingUsbDashDrop = prefab != null;
    }

    public void ClearUsbDashPickupDrop()
    {
        usbDashPickupPrefab = null;
        pendingUsbDashDrop = false;
    }

    public void ConfigureGoatDashPickupDrop(GameObject prefab)
    {
        goatDashPickupPrefab = prefab;
        pendingGoatDashDrop = prefab != null;
    }

    public void ClearGoatDashPickupDrop()
    {
        goatDashPickupPrefab = null;
        pendingGoatDashDrop = false;
    }

    public void ConfigureWeaponPickupDrop(GameObject prefab, int weaponIndex)
    {
        if (prefab == null)
            return;

        pendingWeaponPickupDrops.Add(new PendingWeaponPickupDrop
        {
            Prefab = prefab,
            WeaponIndex = Mathf.Max(0, weaponIndex)
        });
    }

    public void ClearWeaponPickupDrops()
    {
        pendingWeaponPickupDrops.Clear();
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
        InfectionReport.RecordRoomEntered(encounterDepth, roomModifier);
        Started?.Invoke();
        StartCoroutine(SpawnInitialWaveRoutine());
    }

    IEnumerator SpawnInitialWaveRoutine()
    {
        initialWavePending = true;

        List<PlannedSpawn> planned = new List<PlannedSpawn>(PlanWaveSpawns(includeBonuses: true));
        if (planned.Count == 0)
        {
            initialWavePending = false;
            CompleteEncounter();
            yield break;
        }

        yield return SpawnPlannedWaveStaggered(planned, attackDelay, allowCorruptedSavePick: true);
        initialWavePending = false;

        if (livingEnemies.Count == 0)
            CompleteEncounter();
    }

    float RollSpawnTelegraphDelay()
    {
        float min = Mathf.Min(spawnTelegraphMinDelay, spawnTelegraphMaxDelay);
        float max = Mathf.Max(spawnTelegraphMinDelay, spawnTelegraphMaxDelay);
        return Random.Range(min, max);
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

        if (TryBeginCorruptedSaveRespawn(enemy))
            return;

        if (!started || cleared || livingEnemies.Count > 0 || bootLoopWavePending || corruptedSavePending || initialWavePending)
            return;

        if (TryBeginBootLoopWave())
            return;

        CompleteEncounter();
    }

    bool TryBeginCorruptedSaveRespawn(EnemyAI enemy)
    {
        if (roomModifier != RoomModifierType.CorruptedSave || corruptedSaveConsumed)
            return false;

        if (enemy != corruptedSaveHost || corruptedSavePrefab == null)
            return false;

        corruptedSaveConsumed = true;
        corruptedSavePending = true;
        Vector3 deathPosition = enemy.transform.position;
        StartCoroutine(CorruptedSaveRespawnRoutine(deathPosition));
        return true;
    }

    IEnumerator CorruptedSaveRespawnRoutine(Vector3 deathPosition)
    {
        BootLoopSpawnMarker marker = null;
        if (corruptedSaveRespawnDelay > 0f)
        {
            marker = BootLoopSpawnMarker.Create(deathPosition, 1.1f);
            yield return new WaitForSeconds(corruptedSaveRespawnDelay);
            if (marker != null)
                Destroy(marker.gameObject);
        }

        if (cleared || corruptedSavePrefab == null)
        {
            corruptedSavePending = false;
            if (livingEnemies.Count == 0 && !bootLoopWavePending)
                CompleteEncounter();
            yield break;
        }

        Vector3 spawnPosition = deathPosition;
        if (NavMesh.SamplePosition(deathPosition, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            spawnPosition = hit.position;

        GameObject instance = Instantiate(corruptedSavePrefab, spawnPosition, Quaternion.identity);
        EnemyAI ai = instance.GetComponent<EnemyAI>() ?? instance.GetComponentInChildren<EnemyAI>();
        if (ai != null)
        {
            ai.ApplyCorruptedSaveMutation(corruptedSaveHealthMultiplier, corruptedSaveSpeedMultiplier);
            RegisterEnemy(ai);
            float holdoff = Mathf.Max(0.15f, attackDelay * 0.5f);
            ai.SetCombatHoldoff(holdoff);
        }

        corruptedSavePending = false;

        if (livingEnemies.Count == 0)
        {
            Debug.LogWarning(
                $"Corrupted Save room '{room.name}' failed to respawn the corrupted enemy.",
                this);
            CompleteEncounter();
        }
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
        List<PlannedSpawn> planned = new List<PlannedSpawn>(PlanWaveSpawns(includeBonuses: false));
        if (planned.Count == 0)
        {
            bootLoopWavePending = false;
            Debug.LogWarning(
                $"Boot Loop room '{room.name}' failed to plan a reinforcement wave.",
                this);
            CompleteEncounter();
            yield break;
        }

        yield return SpawnPlannedWaveStaggered(planned, bootLoopAttackDelay, allowCorruptedSavePick: false);
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
        plannedSpawnBuffer.Clear();
        if (room == null)
            return plannedSpawnBuffer;

        bool hasNormalPool = enemyPrefabs != null && enemyPrefabs.Length > 0 && spawnCount > 0;
        bool hasMega = includeBonuses && bonusMegaPrefab != null;
        bool hasRaidSupport = includeBonuses &&
                              roomModifier == RoomModifierType.RaidArray &&
                              enemyPrefabs != null &&
                              enemyPrefabs.Length > 0;
        if (!hasNormalPool && !hasMega && !hasRaidSupport)
            return plannedSpawnBuffer;

        occupiedSpawnBuffer.Clear();
        PlayerController player = PlayerController.Instance;
        Transform playerTransform = player != null ? player.transform : null;
        int spawnedOverclock = 0;
        int spawnedRepair = 0;
        int spawnedShielder = 0;

        if (hasMega)
            TryPlanSpawn(bonusMegaPrefab, occupiedSpawnBuffer, playerTransform, plannedSpawnBuffer, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);

        if (hasRaidSupport)
        {
            GameObject support = ChooseRaidSupportPrefab(spawnedOverclock, spawnedRepair, spawnedShielder);
            if (support != null)
                TryPlanSpawn(support, occupiedSpawnBuffer, playerTransform, plannedSpawnBuffer, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        }

        if (!hasNormalPool)
            return plannedSpawnBuffer;

        for (int i = 0; i < spawnCount; i++)
        {
            GameObject prefab = ChooseEnemyPrefab(
                spawnedOverclock,
                spawnedRepair,
                spawnedShielder);
            if (prefab == null)
                continue;

            TryPlanSpawn(prefab, occupiedSpawnBuffer, playerTransform, plannedSpawnBuffer, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        }

        return plannedSpawnBuffer;
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

    IEnumerator SpawnPlannedWaveStaggered(
        List<PlannedSpawn> planned,
        float holdoff,
        bool allowCorruptedSavePick)
    {
        ClearSpawnMarkers();

        var markers = new BootLoopSpawnMarker[planned.Count];
        for (int i = 0; i < planned.Count; i++)
        {
            PlannedSpawn spawn = planned[i];
            BootLoopSpawnMarker marker = BootLoopSpawnMarker.Create(spawn.Position, spawn.MarkerRadius);
            markers[i] = marker;
            activeSpawnMarkers.Add(marker);
        }

        int corruptedIndex = -1;
        if (allowCorruptedSavePick &&
            roomModifier == RoomModifierType.CorruptedSave &&
            corruptedSaveHost == null &&
            planned.Count > 0)
        {
            corruptedIndex = Random.Range(0, planned.Count);
        }

        int remaining = planned.Count;
        for (int i = 0; i < planned.Count; i++)
        {
            int index = i;
            StartCoroutine(SpawnSingleEnemyTelegraphed(
                planned[index],
                markers[index],
                holdoff,
                index == corruptedIndex,
                () => remaining--));
        }

        while (remaining > 0 && !cleared)
            yield return null;
    }

    IEnumerator SpawnSingleEnemyTelegraphed(
        PlannedSpawn spawn,
        BootLoopSpawnMarker marker,
        float holdoff,
        bool markAsCorruptedSave,
        System.Action onComplete)
    {
        float delay = RollSpawnTelegraphDelay();
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (marker != null)
        {
            activeSpawnMarkers.Remove(marker);
            Destroy(marker.gameObject);
        }

        if (cleared || spawn.Prefab == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        GameObject instance = Instantiate(spawn.Prefab, spawn.Position, Quaternion.identity);
        EnemyAI ai = instance.GetComponent<EnemyAI>() ?? instance.GetComponentInChildren<EnemyAI>();
        RegisterEnemy(ai);
        if (ai != null && holdoff > 0f)
            ai.SetCombatHoldoff(holdoff);
        TryGrantRandomShield(ai);

        if (markAsCorruptedSave && ai != null)
        {
            corruptedSaveHost = ai;
            corruptedSavePrefab = spawn.Prefab;
        }

        onComplete?.Invoke();
    }

    void SpawnPlannedWave(List<PlannedSpawn> planned, float holdoff, bool allowCorruptedSavePick = false)
    {
        var justSpawned = new List<(EnemyAI ai, GameObject prefab)>(planned.Count);

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

            if (ai != null)
                justSpawned.Add((ai, spawn.Prefab));
        }

        if (allowCorruptedSavePick &&
            roomModifier == RoomModifierType.CorruptedSave &&
            corruptedSaveHost == null &&
            justSpawned.Count > 0)
        {
            (EnemyAI ai, GameObject prefab) pick = justSpawned[Random.Range(0, justSpawned.Count)];
            corruptedSaveHost = pick.ai;
            corruptedSavePrefab = pick.prefab;
        }
    }

    void SpawnWave(bool includeBonuses, float holdoffOverride)
    {
        List<PlannedSpawn> planned = PlanWaveSpawns(includeBonuses);
        float holdoff = holdoffOverride >= 0f ? holdoffOverride : attackDelay;
        SpawnPlannedWave(planned, holdoff, allowCorruptedSavePick: includeBonuses);
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
        int spawnedOverclock,
        int spawnedRepair,
        int spawnedShielder)
    {
        eligiblePrefabBuffer.Clear();
        spawnWeightBuffer.Clear();

        float totalWeight = 0f;
        bool weighted = scaleEnemyMixByDepth;

        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
                continue;

            if (!CanSpawnBuffPrefab(prefab, spawnedOverclock, spawnedRepair, spawnedShielder))
                continue;

            eligiblePrefabBuffer.Add(prefab);
            if (!weighted)
                continue;

            float weight = GetThreatWeight(prefab, mixExponent);
            spawnWeightBuffer.Add(weight);
            totalWeight += weight;
        }

        int count = eligiblePrefabBuffer.Count;
        if (count == 0)
            return null;

        if (!weighted || count == 1 || totalWeight <= 0f)
            return eligiblePrefabBuffer[Random.Range(0, count)];

        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < count; i++)
        {
            cumulative += spawnWeightBuffer[i];
            if (roll <= cumulative)
                return eligiblePrefabBuffer[i];
        }

        return eligiblePrefabBuffer[count - 1];
    }

    float GetThreatWeight(GameObject prefab, float exponent)
    {
        EnemyAI ai = GetPrefabAi(prefab);
        float threat = ai != null ? ai.Difficulty : 1f;
        if (threat < 1f)
            threat = 1f;
        float weight = Mathf.Pow(threat, exponent);
        return weight > 0.0001f ? weight : 0.0001f;
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

    EnemyAI GetPrefabAi(GameObject prefab)
    {
        if (prefab == null)
            return null;

        if (prefabAiCache.TryGetValue(prefab, out EnemyAI cached))
            return cached;

        EnemyAI ai = prefab.GetComponent<EnemyAI>() ?? prefab.GetComponentInChildren<EnemyAI>();
        prefabAiCache[prefab] = ai;
        return ai;
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

        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindAnyObjectByType<PlayerController>();
        Vector3 playerPosition = player != null ? player.transform.position : center;

        var candidates = new List<Vector3>();
        int attempts = Mathf.Max(1, healthPickupSpawnAttempts);

        // Bias samples toward the player so nearby NavMesh spots are more likely.
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 desired;
            if (player != null && attempt < attempts / 2)
            {
                float radius = Random.Range(2f, Mathf.Max(4f, healthPickupMinCenterDistance));
                float angle = Random.Range(0f, Mathf.PI * 2f);
                desired = playerPosition + new Vector3(
                    Mathf.Cos(angle) * radius,
                    1f,
                    Mathf.Sin(angle) * radius);
            }
            else
            {
                Vector3 localPoint = room.FootprintCenter + new Vector3(
                    Random.Range(-halfWidth, halfWidth),
                    1f,
                    Random.Range(-halfDepth, halfDepth));
                desired = room.transform.TransformPoint(localPoint);
            }

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

        // Prefer the valid candidate closest to the player.
        float bestDistanceSqr = float.MaxValue;
        int bestIndex = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 offset = candidates[i] - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestIndex = i;
        }

        spawnPosition = candidates[bestIndex];
        return true;
    }

    void CompleteEncounter()
    {
        if (cleared)
            return;

        cleared = true;
        bootLoopWavePending = false;
        initialWavePending = false;
        corruptedSavePending = false;
        ClearSpawnMarkers();
        EndPacketLossEffect();
        SetDoorsLocked(false);
        TrySpawnHealthPickup();
        TrySpawnUsbDashPickup();
        TrySpawnGoatDashPickup();
        TrySpawnWeaponPickups();
        InfectionReport.RecordRoomCleared(encounterDepth, roomModifier);
        Cleared?.Invoke();
    }

    void TrySpawnUsbDashPickup()
    {
        if (!pendingUsbDashDrop || usbDashPickupPrefab == null)
            return;

        pendingUsbDashDrop = false;

        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindAnyObjectByType<PlayerController>();
        if (player != null && player.HasUsbDash)
            return;

        if (!TryChooseHealthPickupPosition(out Vector3 spawnPosition))
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh position for a USB Dash pickup in {room.name}.",
                this);
            return;
        }

        Instantiate(usbDashPickupPrefab, spawnPosition, Quaternion.identity);
    }

    void TrySpawnGoatDashPickup()
    {
        if (!pendingGoatDashDrop || goatDashPickupPrefab == null)
            return;

        pendingGoatDashDrop = false;

        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindAnyObjectByType<PlayerController>();
        if (player != null && player.HasGoatDash)
            return;

        if (!TryChooseHealthPickupPosition(out Vector3 spawnPosition))
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh position for a Goat Dash pickup in {room.name}.",
                this);
            return;
        }

        Instantiate(goatDashPickupPrefab, spawnPosition, Quaternion.identity);
    }

    void TrySpawnWeaponPickups()
    {
        if (pendingWeaponPickupDrops.Count == 0)
            return;

        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindAnyObjectByType<PlayerController>();

        for (int i = 0; i < pendingWeaponPickupDrops.Count; i++)
        {
            PendingWeaponPickupDrop drop = pendingWeaponPickupDrops[i];
            if (drop.Prefab == null)
                continue;

            if (player != null && player.HasWeapon(drop.WeaponIndex))
                continue;

            if (!TryChooseHealthPickupPosition(out Vector3 spawnPosition))
            {
                Debug.LogWarning(
                    $"Could not find a valid NavMesh position for a weapon pickup in {room.name}.",
                    this);
                continue;
            }

            GameObject instance = Instantiate(drop.Prefab, spawnPosition, Quaternion.identity);
            WeaponPickup pickup = instance.GetComponent<WeaponPickup>();
            if (pickup != null)
                pickup.Configure(drop.WeaponIndex);
        }

        pendingWeaponPickupDrops.Clear();
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

    struct PendingWeaponPickupDrop
    {
        public GameObject Prefab;
        public int WeaponIndex;
    }
}
