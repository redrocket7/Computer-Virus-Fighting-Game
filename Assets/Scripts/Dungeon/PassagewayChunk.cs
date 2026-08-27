using Unity.AI.Navigation;
using UnityEngine;

public enum PassagewayTurn
{
    Straight,
    Left,
    Right
}

/// <summary>
/// One passageway piece. Pivot is the piece center.
/// Straights stretch along local Z; turns stay fixed square tiles.
/// Entry and exit directions point outward from the piece.
/// </summary>
public class PassagewayChunk : MonoBehaviour
{
    [SerializeField] PassagewayTurn turn;
    [Tooltip("Unscaled travel length for straights, or square tile size for turns.")]
    [SerializeField] float size = 1f;
    [Tooltip("Corridor width across travel. Used for straight footprints.")]
    [SerializeField] float width = 8f;
    [SerializeField] int navigationLayer = 3;
    [SerializeField] float walkableSurfaceHeight = 0.75f;

    const int NotWalkableArea = 1;

    public PassagewayTurn Turn => turn;

    public Vector3 LocalEntryPosition => turn switch
    {
        PassagewayTurn.Straight => Vector3.back * size * 0.5f,
        _ => Vector3.left * size * 0.5f
    };

    public Vector3 LocalEntryDirection => turn == PassagewayTurn.Straight
        ? Vector3.back
        : Vector3.left;

    public Vector3 LocalExitPosition => turn switch
    {
        PassagewayTurn.Straight => Vector3.forward * size * 0.5f,
        PassagewayTurn.Left => Vector3.forward * size * 0.5f,
        _ => Vector3.back * size * 0.5f
    };

    public Vector3 LocalExitDirection => turn switch
    {
        PassagewayTurn.Straight => Vector3.forward,
        PassagewayTurn.Left => Vector3.forward,
        _ => Vector3.back
    };

    public Vector3 EntryPosition => transform.TransformPoint(LocalEntryPosition);
    public Vector3 EntryDirection => transform.TransformDirection(LocalEntryDirection).normalized;
    public Vector3 ExitPosition => transform.TransformPoint(LocalExitPosition);
    public Vector3 ExitDirection => transform.TransformDirection(LocalExitDirection).normalized;

    /// <summary>Rooms this corridor piece was generated between. Used by fog covers.</summary>
    public RoomDefinition ConnectedRoomA { get; private set; }
    public RoomDefinition ConnectedRoomB { get; private set; }

    public void BindConnectedRooms(RoomDefinition roomA, RoomDefinition roomB)
    {
        ConnectedRoomA = roomA;
        ConnectedRoomB = roomB;
    }

    /// <summary>
    /// Stretch a straight along its travel axis so entry-to-exit covers <paramref name="length"/>.
    /// </summary>
    public void SetLength(float length)
    {
        if (turn != PassagewayTurn.Straight)
            return;

        float baseLength = Mathf.Max(0.0001f, size);
        Vector3 scale = transform.localScale;
        scale.z = Mathf.Max(0.0001f, length) / baseLength;
        transform.localScale = scale;
    }

    public void PrepareForNavigation()
    {
        float floorHeight = transform.position.y + walkableSurfaceHeight;

        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.gameObject.layer = navigationLayer;

            if (renderer.bounds.max.y <= floorHeight)
                continue;

            if (!renderer.TryGetComponent(out NavMeshModifier modifier))
                modifier = renderer.gameObject.AddComponent<NavMeshModifier>();

            modifier.overrideArea = true;
            modifier.area = NotWalkableArea;
        }
    }

    public Rect GetWorldFootprint(float padding = 0f)
    {
        return GetWorldTileFootprint(padding);
    }

    public Rect GetWorldTileFootprint(float padding = 0f)
    {
        // World-space entry/exit + width so stretched straights stay corridor-shaped
        // (not a square inflated by localScale).
        if (turn == PassagewayTurn.Straight)
            return BuildOrientedFootprint(EntryPosition, ExitPosition, width, padding);

        float half = size * 0.5f;
        Vector3[] corners =
        {
            transform.TransformPoint(new Vector3(-half, 0f, -half)),
            transform.TransformPoint(new Vector3(half, 0f, -half)),
            transform.TransformPoint(new Vector3(-half, 0f, half)),
            transform.TransformPoint(new Vector3(half, 0f, half))
        };

        return RectFromCorners(corners, padding);
    }

    static Rect BuildOrientedFootprint(
        Vector3 entry,
        Vector3 exit,
        float corridorWidth,
        float padding)
    {
        Vector3 along = exit - entry;
        along.y = 0f;
        float length = along.magnitude;
        Vector3 forward = length > 0.0001f
            ? along / length
            : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;

        float halfW = Mathf.Max(0.01f, corridorWidth) * 0.5f;
        float halfL = length * 0.5f;
        Vector3 mid = (entry + exit) * 0.5f;
        mid.y = 0f;

        Vector3[] corners =
        {
            mid + right * halfW + forward * halfL,
            mid + right * halfW - forward * halfL,
            mid - right * halfW + forward * halfL,
            mid - right * halfW - forward * halfL
        };

        return RectFromCorners(corners, padding);
    }

    static Rect RectFromCorners(Vector3[] corners, float padding)
    {
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

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(EntryPosition, 0.25f);
        Gizmos.DrawRay(EntryPosition, EntryDirection);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(ExitPosition, 0.25f);
        Gizmos.DrawRay(ExitPosition, ExitDirection);
    }
#endif
}
