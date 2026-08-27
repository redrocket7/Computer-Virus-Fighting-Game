using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Describes a room's footprint, sockets, spawn points, and encounter.
/// Missing sockets / spawn points are created from the footprint at runtime.
/// </summary>
public class RoomDefinition : MonoBehaviour
{
    [Header("Footprint (XZ)")]
    [SerializeField] Vector2 footprintSize = new Vector2(90f, 30f);
    [SerializeField] Vector3 footprintCenter = Vector3.zero;

    [Header("References")]
    [SerializeField] RoomSocket[] sockets;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField] RoomEncounter encounter;

    [Header("Navigation")]
    [Tooltip("Room geometry rising higher than this above the floor is baked as a NavMesh hole instead of a walkable surface.")]
    [SerializeField] float walkableSurfaceHeight = 0.75f;

    /// <summary>Built-in NavMesh area index that punches a hole in the surface.</summary>
    const int NotWalkableArea = 1;

    public IReadOnlyList<RoomSocket> Sockets => sockets;
    public IReadOnlyList<Transform> SpawnPoints => spawnPoints;
    public RoomEncounter Encounter => encounter;
    public Vector2 FootprintSize => footprintSize;
    public Vector3 FootprintCenter => footprintCenter;

    List<Rect> cachedMinimapRects;

    void Awake()
    {
        DisableBakedSurfaces();
        EnsureLayout();
    }

    public void EnsureLayout()
    {
        if (encounter == null)
            encounter = GetComponent<RoomEncounter>();

        CollectChildSockets();

        if (sockets == null || sockets.Length == 0)
            BuildDefaultSockets();

        foreach (var socket in sockets)
        {
            if (socket != null)
                socket.Initialize(this, socket.Direction, socket.Door);
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
            BuildDefaultSpawnPoints();

        EnsureTrigger();
        MarkPropsAsObstacles();
    }

    /// <summary>
    /// Props, pillars, walls and ceilings share the ground layer, so the bake would otherwise
    /// place walkable NavMesh on top of them. Tag them "not walkable" so they become holes.
    /// </summary>
    void MarkPropsAsObstacles()
    {
        float floorHeight = transform.position.y + walkableSurfaceHeight;

        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.GetComponentInParent<RoomDoor>() != null)
                continue;

            if (renderer.TryGetComponent<NavMeshModifier>(out _))
                continue;

            if (renderer.bounds.max.y <= floorHeight)
                continue;

            var modifier = renderer.gameObject.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = NotWalkableArea;
        }
    }

    void EnsureTrigger()
    {
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            box.isTrigger = true;
            return;
        }

        box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = footprintCenter + new Vector3(0f, 2f, 0f);
        box.size = new Vector3(Mathf.Max(1f, footprintSize.x - 8f), 4f, Mathf.Max(1f, footprintSize.y - 8f));
    }

    public Rect GetWorldFootprint(float padding = 0f)
    {
        Vector3 half = new Vector3(footprintSize.x * 0.5f, 0f, footprintSize.y * 0.5f);
        Vector3[] corners =
        {
            transform.TransformPoint(footprintCenter + new Vector3(-half.x, 0f, -half.z)),
            transform.TransformPoint(footprintCenter + new Vector3(half.x, 0f, -half.z)),
            transform.TransformPoint(footprintCenter + new Vector3(-half.x, 0f, half.z)),
            transform.TransformPoint(footprintCenter + new Vector3(half.x, 0f, half.z))
        };

        float minX = corners[0].x;
        float maxX = corners[0].x;
        float minZ = corners[0].z;
        float maxZ = corners[0].z;
        for (int i = 1; i < corners.Length; i++)
        {
            minX = Mathf.Min(minX, corners[i].x);
            maxX = Mathf.Max(maxX, corners[i].x);
            minZ = Mathf.Min(minZ, corners[i].z);
            maxZ = Mathf.Max(maxZ, corners[i].z);
        }

        return new Rect(minX - padding, minZ - padding, maxX - minX + padding * 2f, maxZ - minZ + padding * 2f);
    }

    /// <summary>
    /// Walkable XZ rectangles that match the room's actual shape on the minimap.
    /// Falls back to the bounding footprint if the NavMesh is not ready.
    /// </summary>
    public IReadOnlyList<Rect> GetMinimapRects()
    {
        if (cachedMinimapRects == null)
            cachedMinimapRects = BuildMinimapRects();

        return cachedMinimapRects;
    }

    List<Rect> BuildMinimapRects()
    {
        Rect bounds = GetWorldFootprint();
        const float cellSize = 5f;
        int columns = Mathf.Max(1, Mathf.RoundToInt(bounds.width / cellSize));
        int rows = Mathf.Max(1, Mathf.RoundToInt(bounds.height / cellSize));
        float cellWidth = bounds.width / columns;
        float cellHeight = bounds.height / rows;

        var occupied = new bool[columns, rows];
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = 0,
            areaMask = NavMesh.AllAreas
        };

        int filled = 0;
        float sampleY = transform.position.y + 1f;
        for (int x = 0; x < columns; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                float worldX = bounds.xMin + (x + 0.5f) * cellWidth;
                float worldZ = bounds.yMin + (z + 0.5f) * cellHeight;
                Vector3 sample = new Vector3(worldX, sampleY, worldZ);
                float radius = Mathf.Min(cellWidth, cellHeight) * 0.55f;
                if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, radius, filter))
                    continue;

                if (Mathf.Abs(hit.position.x - worldX) > cellWidth * 0.6f ||
                    Mathf.Abs(hit.position.z - worldZ) > cellHeight * 0.6f)
                    continue;

                occupied[x, z] = true;
                filled++;
            }
        }

        if (filled == 0)
            return new List<Rect> { bounds };

        return MergeOccupiedCells(occupied, bounds, cellWidth, cellHeight);
    }

    static List<Rect> MergeOccupiedCells(bool[,] occupied, Rect bounds, float cellWidth, float cellHeight)
    {
        int columns = occupied.GetLength(0);
        int rows = occupied.GetLength(1);
        var used = new bool[columns, rows];
        var rects = new List<Rect>();

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (!occupied[x, z] || used[x, z])
                    continue;

                int width = 1;
                while (x + width < columns && occupied[x + width, z] && !used[x + width, z])
                    width++;

                int height = 1;
                bool canGrow = true;
                while (canGrow && z + height < rows)
                {
                    for (int i = 0; i < width; i++)
                    {
                        if (!occupied[x + i, z + height] || used[x + i, z + height])
                        {
                            canGrow = false;
                            break;
                        }
                    }

                    if (canGrow)
                        height++;
                }

                for (int iz = 0; iz < height; iz++)
                {
                    for (int ix = 0; ix < width; ix++)
                        used[x + ix, z + iz] = true;
                }

                rects.Add(new Rect(
                    bounds.xMin + x * cellWidth,
                    bounds.yMin + z * cellHeight,
                    width * cellWidth,
                    height * cellHeight));
            }
        }

        return rects;
    }

    public RoomSocket GetSocket(CardinalDirection direction)
    {
        return GetRandomUnusedSocket(direction);
    }

    public RoomSocket GetRandomUnusedSocket(CardinalDirection direction)
    {
        if (sockets == null)
            return null;

        int eligible = 0;
        foreach (var socket in sockets)
        {
            if (socket != null && !socket.IsConnected && socket.Direction == direction)
                eligible++;
        }

        if (eligible == 0)
            return null;

        int selected = Random.Range(0, eligible);
        foreach (var socket in sockets)
        {
            if (socket == null || socket.IsConnected || socket.Direction != direction)
                continue;

            if (selected-- == 0)
                return socket;
        }

        return null;
    }

    public IEnumerable<RoomSocket> UnusedSockets()
    {
        if (sockets == null)
            yield break;

        foreach (var socket in sockets)
        {
            if (socket != null && !socket.IsConnected)
                yield return socket;
        }
    }

    public void SetEncounterActive(bool active)
    {
        if (encounter != null)
            encounter.enabled = active;
    }

    public Vector3 GetPlayerSpawnPosition()
    {
        Vector3 world = transform.TransformPoint(footprintCenter);
        world.y = 1f;
        return world;
    }

    void CollectChildSockets()
    {
        var found = GetComponentsInChildren<RoomSocket>(true);
        if (found == null || found.Length == 0)
            return;

        sockets = found;
    }

    void DisableBakedSurfaces()
    {
        foreach (var surface in GetComponentsInChildren<NavMeshSurface>(true))
            surface.enabled = false;
    }

    void BuildDefaultSockets()
    {
        var built = new List<RoomSocket>(4);
        built.Add(CreateSocket(CardinalDirection.North, new Vector3(0f, 0f, footprintSize.y * 0.5f)));
        built.Add(CreateSocket(CardinalDirection.East, new Vector3(footprintSize.x * 0.5f, 0f, 0f)));
        built.Add(CreateSocket(CardinalDirection.South, new Vector3(0f, 0f, -footprintSize.y * 0.5f)));
        built.Add(CreateSocket(CardinalDirection.West, new Vector3(-footprintSize.x * 0.5f, 0f, 0f)));
        sockets = built.ToArray();
    }

    RoomSocket CreateSocket(CardinalDirection direction, Vector3 localOffset)
    {
        var go = new GameObject($"Exit_{direction}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = footprintCenter + localOffset;
        go.transform.localRotation = Quaternion.LookRotation(DirectionVector(direction), Vector3.up);

        var door = CreateDoor(go.transform);
        var socket = go.AddComponent<RoomSocket>();
        socket.Initialize(this, direction, door);
        return socket;
    }

    static RoomDoor CreateDoor(Transform parent)
    {
        var doorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        doorObject.name = "Door";
        doorObject.transform.SetParent(parent, false);
        doorObject.transform.localPosition = new Vector3(0f, 2f, 0f);
        doorObject.transform.localScale = new Vector3(8f, 4f, 1.5f);
        doorObject.layer = 0;

        var collider = doorObject.GetComponent<BoxCollider>();
        collider.isTrigger = true;

        var obstacle = doorObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = false;
        obstacle.carveOnlyStationary = true;

        var door = doorObject.AddComponent<RoomDoor>();
        door.SetLocked(false, instant: true);
        return door;
    }

    void BuildDefaultSpawnPoints()
    {
        Vector2 inset = footprintSize * 0.25f;
        var points = new[]
        {
            new Vector3(-inset.x, 0.1f, -inset.y),
            new Vector3(inset.x, 0.1f, -inset.y),
            new Vector3(-inset.x, 0.1f, inset.y),
            new Vector3(inset.x, 0.1f, inset.y)
        };

        spawnPoints = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var go = new GameObject($"Spawn_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = footprintCenter + points[i];
            spawnPoints[i] = go.transform;
        }
    }

    static Vector3 DirectionVector(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.North => Vector3.forward,
            CardinalDirection.East => Vector3.right,
            CardinalDirection.South => Vector3.back,
            _ => Vector3.left
        };
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Vector3 center = transform.TransformPoint(footprintCenter);
        Vector3 size = new Vector3(footprintSize.x, 0.2f, footprintSize.y);
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
#endif
}
