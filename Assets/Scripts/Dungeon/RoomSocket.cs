using UnityEngine;

public enum CardinalDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}

/// <summary>
/// Explicit room exit facing outward. Place at a floor-edge midpoint.
/// </summary>
public class RoomSocket : MonoBehaviour
{
    [SerializeField] CardinalDirection direction;
    [SerializeField] RoomDoor door;

    public CardinalDirection Direction => direction;
    public RoomDoor Door => door;
    public RoomDefinition Room { get; private set; }
    public RoomSocket Connected { get; private set; }
    public bool IsConnected => Connected != null;

    public CardinalDirection Opposite => direction switch
    {
        CardinalDirection.North => CardinalDirection.South,
        CardinalDirection.East => CardinalDirection.West,
        CardinalDirection.South => CardinalDirection.North,
        _ => CardinalDirection.East
    };

    public void Initialize(RoomDefinition room, CardinalDirection facing, RoomDoor roomDoor)
    {
        Room = room;
        direction = facing;
        door = roomDoor;
    }

    public void ConnectTo(RoomSocket other)
    {
        Connected = other;
        if (other != null)
            other.Connected = this;
    }

    public void SetLocked(bool locked)
    {
        SetLocked(locked, instant: false);
    }

    public void SetLocked(bool locked, bool instant)
    {
        if (door != null)
            door.SetLocked(locked, instant);
    }

    public void SealUnused()
    {
        if (door != null)
            door.SealPermanently();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = IsConnected ? Color.cyan : Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.6f);
        Gizmos.DrawRay(transform.position, transform.forward * 3f);
    }
#endif
}
