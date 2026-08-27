using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Destination for map teleports. Place in a room, or let the dungeon generator spawn one.
/// </summary>
public class TeleportPad : MonoBehaviour
{
    static readonly List<TeleportPad> activePads = new List<TeleportPad>();

    public static IReadOnlyList<TeleportPad> ActivePads => activePads;

    RoomDefinition room;

    public RoomDefinition Room => room;
    public Vector3 ArrivePosition
    {
        get
        {
            Vector3 position = transform.position;
            position.y += 1f;
            return position;
        }
    }

    void Awake()
    {
        room = GetComponentInParent<RoomDefinition>();
    }

    void OnEnable()
    {
        if (!activePads.Contains(this))
            activePads.Add(this);
    }

    void OnDisable()
    {
        activePads.Remove(this);
    }

    public static TeleportPad FindForRoom(RoomDefinition room)
    {
        if (room == null)
            return null;

        for (int i = 0; i < activePads.Count; i++)
        {
            TeleportPad pad = activePads[i];
            if (pad != null && pad.room == room)
                return pad;
        }

        return room.GetComponentInChildren<TeleportPad>(true);
    }
}
