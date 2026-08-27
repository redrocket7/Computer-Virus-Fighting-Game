using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared reveal rules for the minimap and full map overlay.
/// Passages reveal only via real door/endpoint links — not nearby AABB overlap.
/// </summary>
public static class DungeonMapLayout
{
    // Generator snaps endpoints together; keep a small tolerance for float drift.
    const float EndpointLinkDistance = 1.75f;
    const float EndpointLinkDistanceSqr = EndpointLinkDistance * EndpointLinkDistance;
    // How far a passage end may sit outside a room footprint / socket and still count as attached.
    const float RoomAttachPadding = 1.25f;

    static readonly Queue<PassagewayChunk> revealQueue = new Queue<PassagewayChunk>(64);

    public static bool IsRoomRevealed(RoomDefinition room)
    {
        RoomEncounter encounter = GetEncounter(room);
        if (encounter == null || !encounter.enabled)
            return true;

        return encounter.IsCleared;
    }

    public static RoomEncounter GetEncounter(RoomDefinition room)
    {
        if (room == null)
            return null;

        return room.Encounter != null ? room.Encounter : room.GetComponent<RoomEncounter>();
    }

    public static void CollectRevealedPassages(
        DungeonGenerator dungeon,
        HashSet<PassagewayChunk> revealedPassages)
    {
        revealedPassages.Clear();
        revealQueue.Clear();
        if (dungeon == null || dungeon.PlacedPassages.Count == 0)
            return;

        IReadOnlyList<PassagewayChunk> passages = dungeon.PlacedPassages;

        for (int i = 0; i < dungeon.PlacedRooms.Count; i++)
        {
            RoomDefinition room = dungeon.PlacedRooms[i];
            if (!IsRoomRevealed(room))
                continue;

            for (int p = 0; p < passages.Count; p++)
            {
                PassagewayChunk passage = passages[p];
                if (!TouchesRoom(passage, room) || !revealedPassages.Add(passage))
                    continue;

                revealQueue.Enqueue(passage);
            }
        }

        while (revealQueue.Count > 0)
        {
            PassagewayChunk current = revealQueue.Dequeue();
            for (int p = 0; p < passages.Count; p++)
            {
                PassagewayChunk other = passages[p];
                if (other == current || revealedPassages.Contains(other))
                    continue;

                if (!ArePassagesConnected(current, other))
                    continue;

                revealedPassages.Add(other);
                revealQueue.Enqueue(other);
            }
        }
    }

    public static bool TouchesRoom(PassagewayChunk passage, RoomDefinition room)
    {
        if (passage == null || room == null)
            return false;

        Vector3 entry = passage.EntryPosition;
        Vector3 exit = passage.ExitPosition;

        // Prefer real door sockets — avoids catching parallel corridors that only skim the room AABB.
        IReadOnlyList<RoomSocket> sockets = room.Sockets;
        if (sockets != null && sockets.Count > 0)
        {
            for (int i = 0; i < sockets.Count; i++)
            {
                RoomSocket socket = sockets[i];
                if (socket == null)
                    continue;

                Vector3 socketPos = socket.transform.position;
                if (HorizontalDistanceSqr(entry, socketPos) <= EndpointLinkDistanceSqr ||
                    HorizontalDistanceSqr(exit, socketPos) <= EndpointLinkDistanceSqr)
                    return true;
            }
        }

        IReadOnlyList<Rect> roomRects = room.GetMinimapRects();
        for (int i = 0; i < roomRects.Count; i++)
        {
            Rect roomRect = ExpandRect(roomRects[i], RoomAttachPadding);
            if (ContainsXZ(roomRect, entry) || ContainsXZ(roomRect, exit))
                return true;
        }

        return false;
    }

    public static bool ArePassagesConnected(PassagewayChunk a, PassagewayChunk b)
    {
        if (a == null || b == null)
            return false;

        // Only exit→entry links (how the generator chains pieces).
        // Footprint overlap was falsely linking nearby parallel corridors.
        return HorizontalDistanceSqr(a.ExitPosition, b.EntryPosition) <= EndpointLinkDistanceSqr ||
               HorizontalDistanceSqr(b.ExitPosition, a.EntryPosition) <= EndpointLinkDistanceSqr;
    }

    public static Rect ExpandRect(Rect rect, float padding)
    {
        return new Rect(
            rect.xMin - padding,
            rect.yMin - padding,
            rect.width + padding * 2f,
            rect.height + padding * 2f);
    }

    static bool ContainsXZ(Rect rect, Vector3 worldPosition)
    {
        return rect.Contains(new Vector2(worldPosition.x, worldPosition.z));
    }

    static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
