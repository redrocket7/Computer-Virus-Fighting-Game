using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds a branching dungeon from room prefabs, then bakes NavMeshes for both agent types.
/// </summary>
public class DungeonGenerator : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] RoomDefinition startRoomPrefab;
    [SerializeField] RoomDefinition[] roomPrefabs;
    [SerializeField] PassagewayChunk[] passagewayPrefabs;
    [SerializeField] GameObject[] enemyPrefabs;
    [Tooltip("Seconds after enemies spawn before they can chase or attack.")]
    public float enemyAttackDelay = 1.25f;
    [Header("Buff Enemy Limits Per Room")]
    [Min(0)]
    [Tooltip("Max Overclock enemies that can spawn in one room. 0 = none.")]
    [SerializeField] int maxOverclockPerRoom = 1;
    [Min(0)]
    [Tooltip("Max Repair enemies that can spawn in one room. 0 = none.")]
    [SerializeField] int maxRepairPerRoom = 1;

    [Header("Random Shields")]
    [Range(0f, 1f)]
    [Tooltip("Chance each spawned enemy gets a shield dome.")]
    [SerializeField] float shieldSpawnChance = 0.2f;
    [SerializeField] float shieldHealth = 4f;

    [Header("Mega Enemies")]
    [Tooltip("Boss-tier enemies. One may be assigned to a random combat room each dungeon.")]
    [SerializeField] GameObject[] megaEnemyPrefabs;
    [Range(0f, 1f)]
    [Tooltip("Chance that this dungeon places one mega enemy in a random room.")]
    [SerializeField] float megaEnemySpawnChance = 0.35f;
    [Tooltip("When enabled, the start room is never chosen for a mega spawn.")]
    [SerializeField] bool excludeStartRoomForMega = true;
    [Min(1)]
    [Tooltip("Prefer mega hosts within this many door-hops of the start room.")]
    [SerializeField] int megaPreferredMaxDepth = 2;

    [SerializeField] int targetRoomCount = 6;
    [SerializeField] int maxPlacementAttempts = 80;
    [SerializeField] float overlapPadding = 2f;
    [SerializeField] int seed;

    [Header("Passageways")]
    [Min(0.01f)]
    [Tooltip("Minimum total length of all straight segments between two rooms (world units).")]
    [SerializeField] float minimumPassageChunks = 3f;
    [Min(0.01f)]
    [Tooltip("Maximum total length of all straight segments between two rooms (world units).")]
    [SerializeField] float maximumPassageChunks = 15f;
    [Min(0)]
    [Tooltip("Maximum left/right turn pieces allowed between each pair of rooms.")]
    [SerializeField] int maximumPassageTurns = 2;
    [Min(0.01f)]
    [Tooltip("Minimum straight length immediately after leaving a door and before entering the next one.")]
    [SerializeField] float minimumStraightsAtDoors = 2f;
    [Min(0.01f)]
    [Tooltip("Minimum straight length between two turn pieces.")]
    [SerializeField] float minimumStraightsBetweenTurns = 2f;
    [SerializeField] float passageOverlapPadding = 0.1f;

    [Header("Depth Scaling")]
    [Tooltip("Add more enemies in rooms farther from the start (door-hops).")]
    [SerializeField] bool scaleSpawnCountByDepth = true;
    [Min(0)]
    [Tooltip("Extra enemies added per door-hop from the start room.")]
    [SerializeField] int extraEnemiesPerDepth = 1;
    [Min(0)]
    [Tooltip("Hard cap on spawn count after depth scaling. 0 = no cap.")]
    [SerializeField] int maxSpawnCount = 0;

    [Header("Room Modifiers")]
    [Range(0f, 1f)]
    [Tooltip("Chance each combat room becomes a RAID Array (guaranteed Repair or Overclock).")]
    [SerializeField] float raidArrayChance = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("Chance each combat room becomes a Boot Loop (another enemy wave after the first pack dies).")]
    [SerializeField] float bootLoopChance = 0.2f;
    [Min(1)]
    [Tooltip("How many extra waves spawn after the first clear in a Boot Loop room.")]
    [SerializeField] int bootLoopExtraWaves = 1;
    [Min(0f)]
    [Tooltip("Delay after a Boot Loop wave is cleared before the next wave spawns.")]
    [SerializeField] float bootLoopSpawnDelay = 1.5f;
    [Min(0f)]
    [Tooltip("Combat holdoff for enemies spawned by a Boot Loop reinforcement wave.")]
    [SerializeField] float bootLoopAttackDelay = 1.25f;
    [Tooltip("When enabled, the start room never rolls a room modifier.")]
    [SerializeField] bool excludeStartRoomForModifiers = true;

    [Header("Navigation")]
    [SerializeField] int smallAgentTypeId = 0;
    [SerializeField] int fatAgentTypeId = -1372625422;
    [SerializeField] LayerMask groundMask = 1 << 3;

    [Header("Player")]
    [SerializeField] Transform player;

    [Header("Fog Cover")]
    [Tooltip("When enabled, rooms and passages get fog lids until the player reveals them.")]
    [SerializeField] bool enableFogCover = true;
    [SerializeField] Material fogCoverMaterial;
    [SerializeField] Color fogCoverColor = Color.black;
    [SerializeField] bool fogOverrideMaterialColor = true;
    [SerializeField] float fogCoverHeight = 6f;
    [SerializeField] float fogCoverInset = 0f;
    [SerializeField] float fogCoverThickness = 0.15f;

    readonly List<RoomDefinition> placedRooms = new List<RoomDefinition>();
    readonly List<PassagewayChunk> placedPassages = new List<PassagewayChunk>();

    public int PlacedRoomCount => placedRooms.Count;
    public IReadOnlyList<RoomDefinition> PlacedRooms => placedRooms;
    public IReadOnlyList<PassagewayChunk> PlacedPassages => placedPassages;
    public event System.Action Generated;

    void Start()
    {
        Generate();
    }

    public void Generate()
    {
        if (startRoomPrefab == null ||
            roomPrefabs == null ||
            roomPrefabs.Length == 0 ||
            passagewayPrefabs == null ||
            passagewayPrefabs.Length == 0)
        {
            Debug.LogError(
                "Dungeon Generator needs a start room, regular rooms, and passageway chunks.",
                this);
            return;
        }

        if (seed != 0)
            Random.InitState(seed);

        ClearGenerated();

        RoomDefinition start = Instantiate(startRoomPrefab, Vector3.zero, Quaternion.identity, transform);
        start.EnsureLayout();
        start.SetEncounterActive(false);
        ApplyEnemyPool(start);
        placedRooms.Add(start);

        int attempts = 0;
        while (placedRooms.Count < targetRoomCount && attempts < maxPlacementAttempts)
        {
            attempts++;
            if (!TryPlaceBranch())
                continue;
        }

        SealUnusedExits();
        ApplyDepthSpawnScaling();
        AssignRoomModifiers();
        AssignMegaEnemy();
        BuildNavigation();
        PlacePlayer(start);
        if (enableFogCover)
            EnsureRoomFogCovers();
        GetComponent<OutOfBoundsCover>()?.Rebuild();
        Debug.Log($"Dungeon generated {placedRooms.Count}/{targetRoomCount} rooms.", this);
        Generated?.Invoke();
    }

    void EnsureRoomFogCovers()
    {
        for (int i = 0; i < placedRooms.Count; i++)
        {
            RoomDefinition room = placedRooms[i];
            if (room == null)
                continue;

            RoomFogCover roomCover = room.GetComponent<RoomFogCover>();
            if (roomCover == null)
                roomCover = room.gameObject.AddComponent<RoomFogCover>();

            roomCover.Configure(
                fogCoverMaterial,
                fogCoverColor,
                fogOverrideMaterialColor,
                fogCoverHeight,
                fogCoverInset,
                fogCoverThickness);
            roomCover.RefreshVisibility();
        }

        for (int i = 0; i < placedPassages.Count; i++)
        {
            PassagewayChunk passage = placedPassages[i];
            if (passage == null)
                continue;

            PassageFogCover passageCover = passage.GetComponent<PassageFogCover>();
            if (passageCover == null)
                passageCover = passage.gameObject.AddComponent<PassageFogCover>();

            passageCover.Configure(
                this,
                fogCoverMaterial,
                fogCoverColor,
                fogOverrideMaterialColor,
                fogCoverHeight,
                fogCoverInset,
                fogCoverThickness);
        }

        PassageFogCover.RefreshAll();
    }

    void AssignMegaEnemy()
    {
        if (megaEnemyPrefabs == null || megaEnemyPrefabs.Length == 0)
        {
            Debug.LogWarning("Mega spawn skipped: Mega Enemy Prefabs list is empty.", this);
            return;
        }

        float chance = Mathf.Clamp01(megaEnemySpawnChance);
        if (chance <= 0f || Random.value > chance)
            return;

        GameObject megaPrefab = ChooseMegaPrefab();
        if (megaPrefab == null)
        {
            Debug.LogWarning("Mega spawn skipped: no valid mega prefab.", this);
            return;
        }

        RoomDefinition host = ChooseMegaHostRoom();
        if (host == null)
        {
            Debug.LogWarning("Mega spawn skipped: no eligible combat room found.", this);
            return;
        }

        RoomEncounter encounter = GetEncounter(host);
        if (encounter == null || !encounter.enabled)
            return;

        encounter.SetBonusMegaEnemy(megaPrefab);
        Debug.Log($"Mega enemy '{megaPrefab.name}' assigned to room '{host.name}'.", host);
    }

    GameObject ChooseMegaPrefab()
    {
        int valid = 0;
        for (int i = 0; i < megaEnemyPrefabs.Length; i++)
        {
            if (megaEnemyPrefabs[i] != null)
                valid++;
        }

        if (valid == 0)
            return null;

        int selected = Random.Range(0, valid);
        for (int i = 0; i < megaEnemyPrefabs.Length; i++)
        {
            if (megaEnemyPrefabs[i] == null)
                continue;

            if (selected-- == 0)
                return megaEnemyPrefabs[i];
        }

        return null;
    }

    RoomDefinition ChooseMegaHostRoom()
    {
        if (placedRooms.Count == 0)
            return null;

        Dictionary<RoomDefinition, int> depthByRoom = BuildRoomDepthMap();
        RoomDefinition start = placedRooms[0];

        int preferredMax = Mathf.Max(1, megaPreferredMaxDepth);
        var preferred = new List<RoomDefinition>();
        var fallback = new List<RoomDefinition>();

        for (int i = 0; i < placedRooms.Count; i++)
        {
            RoomDefinition room = placedRooms[i];
            if (room == null)
                continue;

            if (excludeStartRoomForMega && room == start)
                continue;

            RoomEncounter encounter = GetEncounter(room);
            if (encounter == null || !encounter.enabled)
                continue;

            if (!depthByRoom.TryGetValue(room, out int depth))
                depth = int.MaxValue;

            fallback.Add(room);
            if (depth >= 1 && depth <= preferredMax)
                preferred.Add(room);
        }

        List<RoomDefinition> pool = preferred.Count > 0 ? preferred : fallback;
        if (pool.Count == 0)
            return null;

        return pool[Random.Range(0, pool.Count)];
    }

    void ApplyDepthSpawnScaling()
    {
        if (!scaleSpawnCountByDepth || extraEnemiesPerDepth <= 0)
            return;

        Dictionary<RoomDefinition, int> depthByRoom = BuildRoomDepthMap();
        for (int i = 0; i < placedRooms.Count; i++)
        {
            RoomDefinition room = placedRooms[i];
            if (room == null)
                continue;

            RoomEncounter encounter = GetEncounter(room);
            if (encounter == null)
                continue;

            if (!depthByRoom.TryGetValue(room, out int depth) || depth <= 0)
                continue;

            int scaled = encounter.SpawnCount + depth * extraEnemiesPerDepth;
            if (maxSpawnCount > 0)
                scaled = Mathf.Min(scaled, maxSpawnCount);

            encounter.SetSpawnCount(scaled);
        }
    }

    void AssignRoomModifiers()
    {
        float raidChance = Mathf.Clamp01(raidArrayChance);
        float loopChance = Mathf.Clamp01(bootLoopChance);
        if (raidChance <= 0f && loopChance <= 0f)
            return;

        bool hasSupports = EnemyPoolHasSupportPrefab();
        RoomDefinition start = placedRooms.Count > 0 ? placedRooms[0] : null;
        var candidates = new List<RoomModifierType>(2);

        for (int i = 0; i < placedRooms.Count; i++)
        {
            RoomDefinition room = placedRooms[i];
            if (room == null)
                continue;

            if (excludeStartRoomForModifiers && room == start)
                continue;

            RoomEncounter encounter = GetEncounter(room);
            if (encounter == null || !encounter.enabled)
                continue;

            if (encounter.RoomModifier != RoomModifierType.None)
                continue;

            candidates.Clear();
            if (raidChance > 0f && Random.value <= raidChance)
            {
                if (hasSupports)
                    candidates.Add(RoomModifierType.RaidArray);
            }

            if (loopChance > 0f && Random.value <= loopChance)
                candidates.Add(RoomModifierType.BootLoop);

            if (candidates.Count == 0)
                continue;

            RoomModifierType chosen = candidates[Random.Range(0, candidates.Count)];
            encounter.SetRoomModifier(chosen);
            if (chosen == RoomModifierType.BootLoop)
            {
                encounter.SetBootLoopExtraWaves(bootLoopExtraWaves);
                encounter.SetBootLoopTiming(bootLoopSpawnDelay, bootLoopAttackDelay);
            }

            Debug.Log($"{chosen} assigned to room '{room.name}'.", room);
        }

        if (raidChance > 0f && !hasSupports)
        {
            Debug.LogWarning(
                "RAID Array rolls skipped: enemy prefab pool has no Repair/Overclock supports.",
                this);
        }
    }

    bool EnemyPoolHasSupportPrefab()
    {
        if (enemyPrefabs == null)
            return false;

        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
                continue;

            EnemyAI ai = prefab.GetComponent<EnemyAI>() ?? prefab.GetComponentInChildren<EnemyAI>();
            if (ai != null && ai.IsSupportEnemy)
                return true;
        }

        return false;
    }

    Dictionary<RoomDefinition, int> BuildRoomDepthMap()
    {
        var depthByRoom = new Dictionary<RoomDefinition, int>(placedRooms.Count);
        if (placedRooms.Count == 0)
            return depthByRoom;

        var queue = new Queue<RoomDefinition>();
        RoomDefinition start = placedRooms[0];
        depthByRoom[start] = 0;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            RoomDefinition current = queue.Dequeue();
            int depth = depthByRoom[current];
            foreach (RoomSocket socket in current.Sockets)
            {
                if (socket == null || !socket.IsConnected || socket.Connected == null)
                    continue;

                RoomDefinition neighbor = socket.Connected.Room;
                if (neighbor == null || depthByRoom.ContainsKey(neighbor))
                    continue;

                depthByRoom[neighbor] = depth + 1;
                queue.Enqueue(neighbor);
            }
        }

        return depthByRoom;
    }

    static RoomEncounter GetEncounter(RoomDefinition room)
    {
        if (room == null)
            return null;

        return room.Encounter != null ? room.Encounter : room.GetComponent<RoomEncounter>();
    }

    bool TryPlaceBranch()
    {
        RoomDefinition host = placedRooms[Random.Range(0, placedRooms.Count)];
        var unused = new List<RoomSocket>(host.UnusedSockets());
        if (unused.Count == 0)
            return false;

        RoomSocket openSocket = unused[Random.Range(0, unused.Count)];
        var candidatePassages = new List<PassagewayChunk>();
        Vector3 passageEnd = openSocket.transform.position;
        Vector3 passageDirection = Flatten(openSocket.transform.forward);

        float minimumLength = Mathf.Max(0.01f, minimumPassageChunks);
        float maximumLength = Mathf.Max(minimumLength, maximumPassageChunks);
        float totalStraightLength = Random.Range(minimumLength, maximumLength);
        List<PassageStep> sequence = BuildPassageSequence(totalStraightLength);

        foreach (PassageStep step in sequence)
        {
            PassagewayChunk passagePrefab = ChoosePassagePrefab(step.Turn);
            if (passagePrefab == null)
            {
                DiscardPassages(candidatePassages);
                return false;
            }

            PassagewayChunk passage = Instantiate(passagePrefab, transform);
            if (step.Turn == PassagewayTurn.Straight)
                passage.SetLength(step.Length);
            AlignPassage(passage, passageEnd, passageDirection);
            passage.PrepareForNavigation();

            Rect passageRect = passage.GetWorldFootprint(-passageOverlapPadding);
            if (PassageOverlapsDungeon(passageRect, candidatePassages))
            {
                DestroyGenerated(passage.gameObject);
                DiscardPassages(candidatePassages);
                return false;
            }

            candidatePassages.Add(passage);
            passageEnd = passage.ExitPosition;
            passageDirection = Flatten(passage.ExitDirection);
        }

        RoomDefinition prefab = roomPrefabs[Random.Range(0, roomPrefabs.Length)];
        RoomDefinition candidate = Instantiate(prefab, transform);
        candidate.EnsureLayout();

        CardinalDirection incomingDirection = Opposite(DirectionFromVector(passageDirection));
        RoomSocket incoming = candidate.GetRandomUnusedSocket(incomingDirection);
        if (incoming == null)
        {
            DiscardCandidate(candidate);
            DiscardPassages(candidatePassages);
            return false;
        }

        AlignSocketToEndpoint(candidate.transform, incoming, passageEnd, passageDirection);

        Rect candidateRect = candidate.GetWorldFootprint(overlapPadding);
        Rect candidateInterior = candidate.GetWorldFootprint(-1f);
        foreach (var existing in placedRooms)
        {
            bool overlaps = existing == host
                ? existing.GetWorldFootprint(-1f).Overlaps(candidateInterior)
                : existing.GetWorldFootprint(overlapPadding).Overlaps(candidateRect);
            if (overlaps)
            {
                DiscardCandidate(candidate);
                DiscardPassages(candidatePassages);
                return false;
            }
        }

        foreach (var passage in placedPassages)
        {
            if (passage.GetWorldFootprint(-passageOverlapPadding).Overlaps(candidateInterior))
            {
                DiscardCandidate(candidate);
                DiscardPassages(candidatePassages);
                return false;
            }
        }

        for (int i = 0; i < candidatePassages.Count - 1; i++)
        {
            if (candidatePassages[i]
                .GetWorldFootprint(-passageOverlapPadding)
                .Overlaps(candidateInterior))
            {
                DiscardCandidate(candidate);
                DiscardPassages(candidatePassages);
                return false;
            }
        }

        openSocket.ConnectTo(incoming);
        openSocket.SetLocked(false, instant: true);
        incoming.SetLocked(false, instant: true);
        for (int i = 0; i < candidatePassages.Count; i++)
        {
            if (candidatePassages[i] != null)
                candidatePassages[i].BindConnectedRooms(host, candidate);
        }
        placedPassages.AddRange(candidatePassages);
        ApplyEnemyPool(candidate);
        placedRooms.Add(candidate);
        return true;
    }

    List<PassageStep> BuildPassageSequence(float totalStraightLength)
    {
        float doorMin = Mathf.Max(0.01f, minimumStraightsAtDoors);
        float betweenMin = Mathf.Max(0.01f, minimumStraightsBetweenTurns);
        int maxTurns = Mathf.Max(0, maximumPassageTurns);

        int maxFeasibleTurns = 0;
        for (int turns = maxTurns; turns >= 0; turns--)
        {
            if (MinimumStraightLengthForTurns(turns, doorMin, betweenMin) <= totalStraightLength + 0.0001f)
            {
                maxFeasibleTurns = turns;
                break;
            }
        }

        // Prefer corridors with at least one jog when the length allows it.
        int turnCount = maxFeasibleTurns > 0
            ? Random.Range(1, maxFeasibleTurns + 1)
            : 0;

        if (turnCount == 0)
        {
            return new List<PassageStep>
            {
                PassageStep.Straight(Mathf.Max(doorMin, totalStraightLength))
            };
        }

        int gapCount = turnCount + 1;
        var gaps = new float[gapCount];
        gaps[0] = doorMin;
        gaps[gapCount - 1] = doorMin;
        for (int i = 1; i < gapCount - 1; i++)
            gaps[i] = betweenMin;

        float extra = totalStraightLength - MinimumStraightLengthForTurns(turnCount, doorMin, betweenMin);
        if (extra > 0f)
        {
            // Sprinkle leftover length across gaps so corridors are not locked to integer tiles.
            for (int i = 0; i < gapCount - 1 && extra > 0.0001f; i++)
            {
                float add = Random.Range(0f, extra);
                gaps[i] += add;
                extra -= add;
            }

            gaps[gapCount - 1] += extra;
        }

        var sequence = new List<PassageStep>(gapCount + turnCount);
        for (int gap = 0; gap < gapCount; gap++)
        {
            sequence.Add(PassageStep.Straight(gaps[gap]));
            if (gap < turnCount)
            {
                sequence.Add(Random.value < 0.5f
                    ? PassageStep.MakeTurn(PassagewayTurn.Left)
                    : PassageStep.MakeTurn(PassagewayTurn.Right));
            }
        }

        return sequence;
    }

    static float MinimumStraightLengthForTurns(int turns, float doorMin, float betweenMin)
    {
        if (turns <= 0)
            return doorMin;

        return doorMin * 2f + (turns - 1) * betweenMin;
    }

    struct PassageStep
    {
        public PassagewayTurn Turn;
        public float Length;

        public static PassageStep Straight(float length) => new PassageStep
        {
            Turn = PassagewayTurn.Straight,
            Length = length
        };

        public static PassageStep MakeTurn(PassagewayTurn turn) => new PassageStep
        {
            Turn = turn,
            Length = 0f
        };
    }

    PassagewayChunk ChoosePassagePrefab(PassagewayTurn turn)
    {
        int eligibleCount = 0;
        foreach (var prefab in passagewayPrefabs)
        {
            if (prefab != null && prefab.Turn == turn)
                eligibleCount++;
        }

        if (eligibleCount == 0)
            return null;

        int selected = Random.Range(0, eligibleCount);
        foreach (var prefab in passagewayPrefabs)
        {
            if (prefab == null || prefab.Turn != turn)
                continue;

            if (selected-- == 0)
                return prefab;
        }

        return null;
    }

    void ApplyEnemyPool(RoomDefinition room)
    {
        if (room == null || enemyPrefabs == null || enemyPrefabs.Length == 0)
            return;

        RoomEncounter encounter = room.Encounter;
        if (encounter == null)
            encounter = room.GetComponent<RoomEncounter>();

        encounter?.SetEnemyPrefabs(enemyPrefabs);
        encounter?.SetAttackDelay(enemyAttackDelay);
        encounter?.SetBuffEnemyLimits(maxOverclockPerRoom, maxRepairPerRoom);
        encounter?.SetShieldSpawnSettings(shieldSpawnChance, shieldHealth);
        encounter?.SetBonusMegaEnemy(null);
        encounter?.SetRoomModifier(RoomModifierType.None);
        encounter?.SetBootLoopExtraWaves(0);
        encounter?.SetBootLoopTiming(1.5f, 1.25f);
    }

    bool PassageOverlapsDungeon(
        Rect passageRect,
        IReadOnlyList<PassagewayChunk> candidatePassages)
    {
        foreach (var room in placedRooms)
        {
            Rect roomInterior = room.GetWorldFootprint(-passageOverlapPadding);
            if (roomInterior.Overlaps(passageRect))
                return true;
        }

        foreach (var passage in placedPassages)
        {
            if (passage.GetWorldFootprint(-passageOverlapPadding).Overlaps(passageRect))
                return true;
        }

        foreach (var passage in candidatePassages)
        {
            if (passage.GetWorldFootprint(-passageOverlapPadding).Overlaps(passageRect))
                return true;
        }

        return false;
    }

    static void AlignPassage(
        PassagewayChunk passage,
        Vector3 endpoint,
        Vector3 outwardDirection)
    {
        Vector3 desiredEntryDirection = -Flatten(outwardDirection);
        float yaw = Vector3.SignedAngle(
            Flatten(passage.EntryDirection),
            desiredEntryDirection,
            Vector3.up);
        passage.transform.Rotate(0f, yaw, 0f, Space.World);
        passage.transform.position += endpoint - passage.EntryPosition;
    }

    static void AlignSocketToEndpoint(
        Transform newRoom,
        RoomSocket incoming,
        Vector3 endpoint,
        Vector3 outwardDirection)
    {
        Vector3 newForward = Flatten(incoming.transform.forward);
        Vector3 desiredForward = Flatten(-outwardDirection);
        if (newForward.sqrMagnitude < 0.001f || desiredForward.sqrMagnitude < 0.001f)
            return;

        float yaw = Vector3.SignedAngle(newForward, desiredForward, Vector3.up);
        newRoom.Rotate(0f, yaw, 0f, Space.World);
        newRoom.position += endpoint - incoming.transform.position;
        newRoom.position = new Vector3(newRoom.position.x, 0f, newRoom.position.z);
    }

    static CardinalDirection DirectionFromVector(Vector3 direction)
    {
        direction = Flatten(direction);
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
            return direction.x >= 0f ? CardinalDirection.East : CardinalDirection.West;

        return direction.z >= 0f ? CardinalDirection.North : CardinalDirection.South;
    }

    static CardinalDirection Opposite(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.North => CardinalDirection.South,
            CardinalDirection.East => CardinalDirection.West,
            CardinalDirection.South => CardinalDirection.North,
            _ => CardinalDirection.East
        };
    }

    void SealUnusedExits()
    {
        foreach (var room in placedRooms)
        {
            foreach (var socket in room.UnusedSockets())
                socket.SealUnused();
        }
    }

    void BuildNavigation()
    {
        foreach (var existing in GetComponents<NavMeshSurface>())
        {
            if (Application.isPlaying)
                Destroy(existing);
            else
                DestroyImmediate(existing);
        }

        BuildSurface(smallAgentTypeId, 0.6666667f);
        BuildSurface(fatAgentTypeId, 1.1333333f);
    }

    void BuildSurface(int agentTypeId, float voxelSize)
    {
        var surface = gameObject.AddComponent<NavMeshSurface>();
        surface.agentTypeID = agentTypeId;
        surface.collectObjects = CollectObjects.Children;
        surface.layerMask = groundMask;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.overrideVoxelSize = true;
        surface.voxelSize = voxelSize;
        surface.BuildNavMesh();
    }

    void PlacePlayer(RoomDefinition start)
    {
        if (player == null)
        {
            var controller = FindAnyObjectByType<PlayerController>();
            if (controller != null)
                player = controller.transform;
        }

        if (player != null)
            player.position = start.GetPlayerSpawnPosition();
    }

    static void DiscardCandidate(RoomDefinition candidate)
    {
        if (candidate == null)
            return;

        DestroyGenerated(candidate.gameObject);
    }

    static void DiscardPassages(IReadOnlyList<PassagewayChunk> passages)
    {
        foreach (var passage in passages)
        {
            if (passage != null)
                DestroyGenerated(passage.gameObject);
        }
    }

    static void DestroyGenerated(GameObject target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            target.SetActive(false);
            Destroy(target);
        }
        else
            DestroyImmediate(target);
    }

    void ClearGenerated()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child.name == "Out Of Bounds Cover")
                continue;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        placedRooms.Clear();
        placedPassages.Clear();
    }

    static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.001f ? value.normalized : Vector3.forward;
    }
}
