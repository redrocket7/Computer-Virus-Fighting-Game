using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum RoomModifierType
{
    None = 0,
    /// <summary>Guarantees Repair, Overclock, and Shielder supports in the room.</summary>
    RaidArray = 1,
    /// <summary>When the current pack dies, another wave of enemies spawns.</summary>
    BootLoop = 2,
    /// <summary>Player shots have a chance to fizzle while the encounter is active.</summary>
    PacketLoss = 3,
    /// <summary>One random enemy respawns once after death, weaker but faster.</summary>
    CorruptedSave = 4,
    /// <summary>Spawns tiny enemies over time until all non-tiny enemies are dead.</summary>
    ForkBomb = 5,
    /// <summary>Guarantees one mega enemy in the room (at most one Critical Process room per run).</summary>
    CriticalProcess = 6
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
    [Tooltip("Near-player enemies (Cache) spawn at least this far from the player.")]
    [SerializeField] float nearPlayerMinDistance = 2.5f;
    [Tooltip("Near-player enemies (Cache) spawn at most this far from the player.")]
    [SerializeField] float nearPlayerMaxDistance = 5.5f;
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

    [Header("Cache Enemy")]
    [Range(0f, 1f)]
    [Tooltip("How often Cache is eligible when rolling room enemies. 0 = never, 1 = full pool weight.")]
    [SerializeField] float cacheEnemySpawnChance = 0.35f;

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
    GameObject forkBombTinyPrefab;
    float forkBombSpawnInterval = 4f;
    float forkBombAttackDelay = 1f;
    int forkBombPackMinSize = 3;
    int forkBombPackMaxSize = 4;
    int forkBombMaxLivingTinies = 12;
    bool forkBombActive;
    bool forkBombWavePending;
    Coroutine forkBombRoutine;

    [Header("Tiny Pack Spawns")]
    [SerializeField] int tinyPackMinSize = 3;
    [SerializeField] int tinyPackMaxSize = 4;
    [SerializeField] float tinyPackClusterRadius = 2.25f;

    [Header("Spawn Telegraph")]
    [SerializeField] float spawnTelegraphMinDelay = 1f;
    [SerializeField] float spawnTelegraphMaxDelay = 6f;

    [Header("Critical Process")]
    [Tooltip("Seconds to show the mega silhouette/name before doors lock.")]
    [SerializeField] float criticalProcessTelegraphDuration = 2.4f;

    bool initialWavePending;
    Coroutine criticalProcessTelegraphRoutine;
    CriticalProcessSilhouette activeCriticalSilhouette;

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

    /// <summary>Fired when a combat room with a non-None modifier begins.</summary>
    public static event System.Action<RoomModifierType> ModifierEncounterStarted;

    /// <summary>Fired when Critical Process shows its mega preview (doors still unlocked).</summary>
    public static event System.Action<string> CriticalProcessTelegraphStarted;

    /// <summary>Fired when the Critical Process preview ends or is cancelled.</summary>
    public static event System.Action CriticalProcessTelegraphEnded;

    /// <summary>Display name of the pending Critical Process mega, if any.</summary>
    public string BonusMegaDisplayName => FormatEnemyDisplayName(bonusMegaPrefab);

    public static string GetModifierDisplayName(RoomModifierType type)
    {
        return type switch
        {
            RoomModifierType.RaidArray => "RAID Array",
            RoomModifierType.BootLoop => "Boot Loop",
            RoomModifierType.PacketLoss => "Packet Loss",
            RoomModifierType.CorruptedSave => "Corrupted Save",
            RoomModifierType.ForkBomb => "Fork Bomb",
            RoomModifierType.CriticalProcess => "Critical Process",
            _ => type.ToString()
        };
    }

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

    public void SetCacheEnemySpawnChance(float chance)
    {
        cacheEnemySpawnChance = Mathf.Clamp01(chance);
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

        if (modifier != RoomModifierType.ForkBomb)
            StopForkBomb(clearSettings: true);

        if (modifier != RoomModifierType.CriticalProcess)
            bonusMegaPrefab = null;
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

    public void SetForkBombSettings(
        GameObject tinyPrefab,
        float spawnInterval,
        float attackDelay,
        int packMinSize,
        int packMaxSize,
        int maxLivingTinies)
    {
        forkBombTinyPrefab = tinyPrefab;
        forkBombSpawnInterval = Mathf.Max(0.5f, spawnInterval);
        forkBombAttackDelay = Mathf.Max(0f, attackDelay);
        forkBombPackMinSize = Mathf.Max(1, packMinSize);
        forkBombPackMaxSize = Mathf.Max(forkBombPackMinSize, packMaxSize);
        forkBombMaxLivingTinies = Mathf.Max(1, maxLivingTinies);
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
        CancelCriticalProcessTelegraph(invokeEndedEvent: false);
        ClearSpawnMarkers();
        EndPacketLossEffect();
    }

    void OnTriggerEnter(Collider other)
    {
        if (started || !enabled)
            return;

        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        if (ShouldTelegraphCriticalProcess())
        {
            if (criticalProcessTelegraphRoutine == null)
                criticalProcessTelegraphRoutine = StartCoroutine(CriticalProcessTelegraphRoutine());
            return;
        }

        BeginEncounter();
    }

    void OnTriggerExit(Collider other)
    {
        if (started || criticalProcessTelegraphRoutine == null)
            return;

        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        CancelCriticalProcessTelegraph(invokeEndedEvent: true);
    }

    public void BeginEncounter()
    {
        if (started)
            return;

        CancelCriticalProcessTelegraph(invokeEndedEvent: false);

        started = true;
        if (room == null)
            room = GetComponent<RoomDefinition>();

        SetDoorsLocked(true);
        BeginPacketLossEffect();
        InfectionReport.RecordRoomEntered(encounterDepth, roomModifier);
        if (roomModifier != RoomModifierType.None)
            ModifierEncounterStarted?.Invoke(roomModifier);
        Started?.Invoke();
        StartCoroutine(SpawnInitialWaveRoutine());
    }

    bool ShouldTelegraphCriticalProcess()
    {
        return roomModifier == RoomModifierType.CriticalProcess && bonusMegaPrefab != null;
    }

    IEnumerator CriticalProcessTelegraphRoutine()
    {
        if (room == null)
            room = GetComponent<RoomDefinition>();

        string megaName = FormatEnemyDisplayName(bonusMegaPrefab);
        CriticalProcessTelegraphStarted?.Invoke(megaName);

        Vector3 silhouettePosition = GetCriticalProcessSilhouettePosition();
        activeCriticalSilhouette = CriticalProcessSilhouette.Create(bonusMegaPrefab, silhouettePosition);

        float remaining = Mathf.Max(0.35f, criticalProcessTelegraphDuration);
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        criticalProcessTelegraphRoutine = null;
        ClearCriticalProcessSilhouette();
        CriticalProcessTelegraphEnded?.Invoke();
        BeginEncounter();
    }

    void CancelCriticalProcessTelegraph(bool invokeEndedEvent)
    {
        if (criticalProcessTelegraphRoutine != null)
        {
            StopCoroutine(criticalProcessTelegraphRoutine);
            criticalProcessTelegraphRoutine = null;
        }

        ClearCriticalProcessSilhouette();

        if (invokeEndedEvent)
            CriticalProcessTelegraphEnded?.Invoke();
    }

    void ClearCriticalProcessSilhouette()
    {
        if (activeCriticalSilhouette == null)
            return;

        Destroy(activeCriticalSilhouette.gameObject);
        activeCriticalSilhouette = null;
    }

    Vector3 GetCriticalProcessSilhouettePosition()
    {
        if (room == null)
            return transform.position;

        Vector3 center = room.transform.TransformPoint(room.FootprintCenter);
        center.y = room.transform.position.y + 1f;
        if (NavMesh.SamplePosition(center, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            return hit.position;

        center.y = room.transform.position.y;
        return center;
    }

    public static string FormatEnemyDisplayName(GameObject prefab)
    {
        if (prefab == null)
            return "Unknown";

        string name = prefab.name;
        if (name.EndsWith(" Enemy", System.StringComparison.Ordinal))
            name = name.Substring(0, name.Length - " Enemy".Length);

        return name;
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

        TryStartForkBomb();

        if (livingEnemies.Count == 0 && !forkBombWavePending)
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

        if (roomModifier == RoomModifierType.ForkBomb && !HasLivingNonTinyEnemies())
            forkBombActive = false;

        if (TryBeginCorruptedSaveRespawn(enemy))
            return;

        if (!started ||
            cleared ||
            livingEnemies.Count > 0 ||
            bootLoopWavePending ||
            corruptedSavePending ||
            initialWavePending ||
            forkBombWavePending)
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

    void TryStartForkBomb()
    {
        if (roomModifier != RoomModifierType.ForkBomb || forkBombTinyPrefab == null || cleared)
            return;

        if (!IsTinyEnemyPrefab(forkBombTinyPrefab))
        {
            Debug.LogWarning(
                $"Fork Bomb room '{(room != null ? room.name : name)}' tiny prefab is not a TinyEnemyAI.",
                this);
            return;
        }

        forkBombActive = true;
        if (forkBombRoutine != null)
            StopCoroutine(forkBombRoutine);
        forkBombRoutine = StartCoroutine(ForkBombSpawnRoutine());
    }

    void StopForkBomb(bool clearSettings)
    {
        forkBombActive = false;
        forkBombWavePending = false;
        if (forkBombRoutine != null)
        {
            StopCoroutine(forkBombRoutine);
            forkBombRoutine = null;
        }

        if (!clearSettings)
            return;

        forkBombTinyPrefab = null;
    }

    IEnumerator ForkBombSpawnRoutine()
    {
        while (!cleared && forkBombActive)
        {
            if (!HasLivingNonTinyEnemies())
                break;

            yield return new WaitForSeconds(forkBombSpawnInterval);

            if (cleared || !forkBombActive || !HasLivingNonTinyEnemies())
                break;

            if (CountLivingTinies() >= forkBombMaxLivingTinies)
                continue;

            List<PlannedSpawn> planned = PlanForkBombPack();
            if (planned.Count == 0)
                continue;

            forkBombWavePending = true;
            yield return SpawnPlannedWaveStaggered(planned, forkBombAttackDelay, allowCorruptedSavePick: false);
            forkBombWavePending = false;

            if (!started || cleared)
                yield break;

            if (livingEnemies.Count == 0 && !HasLivingNonTinyEnemies())
            {
                CompleteEncounter();
                yield break;
            }
        }

        forkBombActive = false;
        forkBombRoutine = null;
        forkBombWavePending = false;

        if (!cleared && livingEnemies.Count == 0 && !bootLoopWavePending && !corruptedSavePending && !initialWavePending)
            CompleteEncounter();
    }

    List<PlannedSpawn> PlanForkBombPack()
    {
        plannedSpawnBuffer.Clear();
        if (forkBombTinyPrefab == null || room == null)
            return plannedSpawnBuffer;

        occupiedSpawnBuffer.Clear();
        foreach (EnemyAI enemy in livingEnemies)
        {
            if (enemy != null && enemy.IsAlive)
                occupiedSpawnBuffer.Add(enemy.transform.position);
        }

        PlayerController player = PlayerController.Instance;
        Transform playerTransform = player != null ? player.transform : null;
        int unusedOverclock = 0;
        int unusedRepair = 0;
        int unusedShielder = 0;

        int minSize = Mathf.Max(1, forkBombPackMinSize);
        int maxSize = Mathf.Max(minSize, forkBombPackMaxSize);
        int remainingCap = Mathf.Max(0, forkBombMaxLivingTinies - CountLivingTinies());
        int packSize = Mathf.Min(Random.Range(minSize, maxSize + 1), remainingCap);
        if (packSize <= 0)
            return plannedSpawnBuffer;

        if (!TryChooseRandomSpawnPosition(forkBombTinyPrefab, occupiedSpawnBuffer, playerTransform, out Vector3 anchor))
            return plannedSpawnBuffer;

        AddPlannedSpawn(
            forkBombTinyPrefab,
            anchor,
            occupiedSpawnBuffer,
            plannedSpawnBuffer,
            ref unusedOverclock,
            ref unusedRepair,
            ref unusedShielder);

        float clusterRadius = Mathf.Max(0.5f, tinyPackClusterRadius);
        for (int i = 1; i < packSize; i++)
        {
            if (!TryChoosePackMatePosition(
                    forkBombTinyPrefab,
                    anchor,
                    clusterRadius,
                    occupiedSpawnBuffer,
                    playerTransform,
                    out Vector3 matePosition))
                continue;

            AddPlannedSpawn(
                forkBombTinyPrefab,
                matePosition,
                occupiedSpawnBuffer,
                plannedSpawnBuffer,
                ref unusedOverclock,
                ref unusedRepair,
                ref unusedShielder);
        }

        return plannedSpawnBuffer;
    }

    bool HasLivingNonTinyEnemies()
    {
        foreach (EnemyAI enemy in livingEnemies)
        {
            if (enemy == null || !enemy.IsAlive)
                continue;
            if (enemy is TinyEnemyAI)
                continue;
            return true;
        }

        return false;
    }

    int CountLivingTinies()
    {
        int count = 0;
        foreach (EnemyAI enemy in livingEnemies)
        {
            if (enemy is TinyEnemyAI tiny && tiny.IsAlive)
                count++;
        }

        return count;
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
            PlanRaidSupportSpawns(occupiedSpawnBuffer, playerTransform, plannedSpawnBuffer, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);

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

            if (IsTinyEnemyPrefab(prefab))
            {
                TryPlanTinyPack(
                    prefab,
                    occupiedSpawnBuffer,
                    playerTransform,
                    plannedSpawnBuffer,
                    ref spawnedOverclock,
                    ref spawnedRepair,
                    ref spawnedShielder);
                continue;
            }

            TryPlanSpawn(prefab, occupiedSpawnBuffer, playerTransform, plannedSpawnBuffer, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        }

        return plannedSpawnBuffer;
    }

    bool IsTinyEnemyPrefab(GameObject prefab)
    {
        return GetPrefabAi(prefab) is TinyEnemyAI;
    }

    void TryPlanTinyPack(
        GameObject prefab,
        List<Vector3> occupiedPositions,
        Transform player,
        List<PlannedSpawn> planned,
        ref int spawnedOverclock,
        ref int spawnedRepair,
        ref int spawnedShielder)
    {
        int minSize = Mathf.Max(1, tinyPackMinSize);
        int maxSize = Mathf.Max(minSize, tinyPackMaxSize);
        int packSize = Random.Range(minSize, maxSize + 1);

        if (!TryChooseRandomSpawnPosition(prefab, occupiedPositions, player, out Vector3 anchor))
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh spawn point for tiny pack ({prefab.name}) in {room.name}.",
                this);
            return;
        }

        AddPlannedSpawn(prefab, anchor, occupiedPositions, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);

        float clusterRadius = Mathf.Max(0.5f, tinyPackClusterRadius);
        for (int i = 1; i < packSize; i++)
        {
            if (!TryChoosePackMatePosition(prefab, anchor, clusterRadius, occupiedPositions, player, out Vector3 matePosition))
                continue;

            AddPlannedSpawn(prefab, matePosition, occupiedPositions, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        }
    }

    void AddPlannedSpawn(
        GameObject prefab,
        Vector3 spawnPosition,
        List<Vector3> occupiedPositions,
        List<PlannedSpawn> planned,
        ref int spawnedOverclock,
        ref int spawnedRepair,
        ref int spawnedShielder)
    {
        occupiedPositions.Add(spawnPosition);
        CountBuffSpawn(GetPrefabAi(prefab), ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        planned.Add(new PlannedSpawn
        {
            Prefab = prefab,
            Position = spawnPosition,
            MarkerRadius = EstimateMarkerRadius(prefab)
        });
    }

    bool TryChoosePackMatePosition(
        GameObject prefab,
        Vector3 anchor,
        float clusterRadius,
        List<Vector3> occupiedPositions,
        Transform player,
        out Vector3 spawnPosition)
    {
        spawnPosition = anchor;
        float minSeparation = Mathf.Max(0.75f, minimumSpawnSeparation * 0.45f);

        for (int attempt = 0; attempt < spawnAttemptsPerEnemy; attempt++)
        {
            Vector2 disk = Random.insideUnitCircle * clusterRadius;
            Vector3 candidate = anchor + new Vector3(disk.x, 0f, disk.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, clusterRadius, NavMesh.AllAreas))
                continue;

            spawnPosition = hit.position;
            if (!IsSpawnClearOfOccupied(spawnPosition, occupiedPositions, minSeparation))
                continue;

            if (player != null)
            {
                Vector3 toPlayer = spawnPosition - player.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude < minimumPlayerDistance * minimumPlayerDistance)
                    continue;
            }

            if (room != null && spawnPosition.y - room.transform.position.y > maximumSpawnHeight)
                continue;

            return true;
        }

        return false;
    }

    bool IsSpawnClearOfOccupied(Vector3 position, List<Vector3> occupiedPositions, float minSeparation)
    {
        float minSeparationSq = minSeparation * minSeparation;
        for (int i = 0; i < occupiedPositions.Count; i++)
        {
            Vector3 offset = position - occupiedPositions[i];
            offset.y = 0f;
            if (offset.sqrMagnitude < minSeparationSq)
                return false;
        }

        return true;
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

        EnemyAI prefabAi = GetPrefabAi(prefab);
        Vector3 spawnPosition;
        bool placed = prefabAi != null && prefabAi.PrefersNearPlayerSpawn
            ? TryChooseNearPlayerSpawnPosition(prefab, occupiedPositions, player, out spawnPosition)
            : TryChooseRandomSpawnPosition(prefab, occupiedPositions, player, out spawnPosition);

        if (!placed)
        {
            Debug.LogWarning(
                $"Could not find a valid NavMesh spawn point for {prefab.name} in {room.name}.",
                this);
            return;
        }

        occupiedPositions.Add(spawnPosition);
        CountBuffSpawn(prefabAi, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
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

    void PlanRaidSupportSpawns(
        List<Vector3> occupiedPositions,
        Transform player,
        List<PlannedSpawn> planned,
        ref int spawnedOverclock,
        ref int spawnedRepair,
        ref int spawnedShielder)
    {
        // RAID Array guarantees one of each support type when their prefabs exist.
        TryPlanRaidSupportOfType<RepairEnemyAI>(occupiedPositions, player, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        TryPlanRaidSupportOfType<OverclockEnemyAI>(occupiedPositions, player, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
        TryPlanRaidSupportOfType<ShielderEnemyAI>(occupiedPositions, player, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
    }

    void TryPlanRaidSupportOfType<TSupport>(
        List<Vector3> occupiedPositions,
        Transform player,
        List<PlannedSpawn> planned,
        ref int spawnedOverclock,
        ref int spawnedRepair,
        ref int spawnedShielder)
        where TSupport : EnemyAI
    {
        GameObject prefab = FindSupportPrefabOfType<TSupport>(spawnedOverclock, spawnedRepair, spawnedShielder);
        if (prefab == null)
            return;

        TryPlanSpawn(prefab, occupiedPositions, player, planned, ref spawnedOverclock, ref spawnedRepair, ref spawnedShielder);
    }

    GameObject FindSupportPrefabOfType<TSupport>(
        int spawnedOverclock,
        int spawnedRepair,
        int spawnedShielder)
        where TSupport : EnemyAI
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            return null;

        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
                continue;

            EnemyAI ai = GetPrefabAi(prefab);
            if (ai is not TSupport)
                continue;

            if (!CanSpawnBuffPrefab(prefab, spawnedOverclock, spawnedRepair, spawnedShielder))
                continue;

            return prefab;
        }

        return null;
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

            // Fork Bomb rooms drip tinies over time; keep the initial wave non-tiny.
            if (roomModifier == RoomModifierType.ForkBomb && IsTinyEnemyPrefab(prefab))
                continue;

            if (!CanIncludeCachePrefab(prefab, weighted))
                continue;

            eligiblePrefabBuffer.Add(prefab);
            if (!weighted)
                continue;

            float weight = GetThreatWeight(prefab, mixExponent);
            if (GetPrefabAi(prefab) is CacheEnemyAI)
                weight *= Mathf.Max(0.0001f, cacheEnemySpawnChance);

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

    bool CanIncludeCachePrefab(GameObject prefab, bool weightedMix)
    {
        if (GetPrefabAi(prefab) is not CacheEnemyAI)
            return true;

        if (cacheEnemySpawnChance <= 0f)
            return false;

        // Weighted mix uses chance as a weight multiplier instead of a hard gate.
        if (weightedMix)
            return true;

        return Random.value <= cacheEnemySpawnChance;
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
        bool isSupport = ai is OverclockEnemyAI || ai is RepairEnemyAI || ai is ShielderEnemyAI;
        if (!isSupport)
            return true;

        // Normal rooms: at most one support enemy total across all three types.
        // RAID Array rooms: one of each type (enforced by the per-type caps below).
        if (roomModifier != RoomModifierType.RaidArray)
        {
            int totalSupports = spawnedOverclock + spawnedRepair + spawnedShielder;
            if (totalSupports >= 1)
                return false;
        }

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

    public bool TryChooseCombatSpawnPosition(
        GameObject prefab,
        IReadOnlyList<Vector3> occupiedPositions,
        out Vector3 spawnPosition)
    {
        Transform player = PlayerController.Instance != null
            ? PlayerController.Instance.transform
            : null;

        EnemyAI prefabAi = GetPrefabAi(prefab);
        if (prefabAi != null && prefabAi.PrefersNearPlayerSpawn)
            return TryChooseNearPlayerSpawnPosition(prefab, occupiedPositions, player, out spawnPosition);

        return TryChooseRandomSpawnPosition(prefab, occupiedPositions, player, out spawnPosition);
    }

    bool TryChooseNearPlayerSpawnPosition(
        GameObject prefab,
        IReadOnlyList<Vector3> occupiedPositions,
        Transform player,
        out Vector3 spawnPosition)
    {
        spawnPosition = default;
        if (player == null)
            return TryChooseRandomSpawnPosition(prefab, occupiedPositions, player, out spawnPosition);

        NavMeshAgent prefabAgent =
            prefab.GetComponent<NavMeshAgent>() ??
            prefab.GetComponentInChildren<NavMeshAgent>();

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = prefabAgent != null ? prefabAgent.agentTypeID : 0,
            areaMask = prefabAgent != null ? prefabAgent.areaMask : NavMesh.AllAreas
        };

        Rect allowedFootprint = room.GetWorldFootprint(-edgePadding);
        float separationSqr = minimumSpawnSeparation * minimumSpawnSeparation;
        float minDist = Mathf.Max(0.5f, nearPlayerMinDistance);
        float maxDist = Mathf.Max(minDist + 0.25f, nearPlayerMaxDistance);
        float minDistSqr = minDist * minDist;
        float maxDistSqr = maxDist * maxDist;
        Vector3 playerPos = player.position;

        Vector3 best = default;
        float bestScore = float.MaxValue;
        bool found = false;

        int attempts = Mathf.Max(1, spawnAttemptsPerEnemy);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float angle = Random.Range(0f, 360f);
            float radius = Random.Range(minDist, maxDist);
            Vector3 desired = playerPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
            desired.y = playerPos.y + 1f;

            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, spawnSampleRadius, filter))
                continue;

            if (hit.position.y - room.transform.position.y > maximumSpawnHeight)
                continue;

            Vector2 hitXZ = new Vector2(hit.position.x, hit.position.z);
            if (!allowedFootprint.Contains(hitXZ))
                continue;

            Vector3 playerOffset = hit.position - playerPos;
            playerOffset.y = 0f;
            float distSqr = playerOffset.sqrMagnitude;
            if (distSqr < minDistSqr || distSqr > maxDistSqr)
                continue;

            bool tooClose = false;
            for (int i = 0; i < occupiedPositions.Count; i++)
            {
                Vector3 offset = hit.position - occupiedPositions[i];
                offset.y = 0f;
                if (offset.sqrMagnitude < separationSqr)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            // Prefer the closest valid sample so Cache starts in the player's face.
            if (!found || distSqr < bestScore)
            {
                found = true;
                bestScore = distSqr;
                best = hit.position;
            }
        }

        if (!found)
            return TryChooseRandomSpawnPosition(prefab, occupiedPositions, player, out spawnPosition);

        spawnPosition = best;
        return true;
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
        StopForkBomb(clearSettings: false);
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
