using UnityEngine;

/// <summary>
/// Invisible solid barrier on a connected doorway that only opens when every
/// living local player is gathered at that doorway (co-op sync gate).
/// Solo / single living player: barrier stays disabled.
/// </summary>
[DisallowMultipleComponent]
public class CoopDoorwayGate : MonoBehaviour
{
    [SerializeField] float gatherRadius = 1.75f;
    [SerializeField] Vector3 barrierSize = new Vector3(7f, 4f, 1.35f);
    [SerializeField] float barrierCenterY = 2f;

    RoomSocket socket;
    BoxCollider barrier;
    bool latchOpen;
    bool barrierBlocking = true;

    public static CoopDoorwayGate EnsureOn(RoomSocket socket)
    {
        if (socket == null)
            return null;

        CoopDoorwayGate gate = socket.GetComponent<CoopDoorwayGate>();
        if (gate == null)
            gate = socket.gameObject.AddComponent<CoopDoorwayGate>();

        gate.Bind(socket);
        return gate;
    }

    void Bind(RoomSocket roomSocket)
    {
        socket = roomSocket;
        EnsureBarrier();
        SetBarrierBlocking(false);
        latchOpen = false;
    }

    void Awake()
    {
        if (socket == null)
            socket = GetComponent<RoomSocket>();
        EnsureBarrier();
    }

    void FixedUpdate()
    {
        UpdateGate();
    }

    void EnsureBarrier()
    {
        if (barrier != null)
            return;

        // Parent to the socket — never the RoomDoor mesh, which slides underground when open.
        var go = new GameObject("Coop Doorway Barrier");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, barrierCenterY, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = gameObject.layer;

        Vector3 size = barrierSize;
        if (socket != null && socket.Door != null)
        {
            BoxCollider doorCollider = socket.Door.GetComponent<BoxCollider>();
            if (doorCollider != null)
            {
                Vector3 doorWorldSize = Vector3.Scale(doorCollider.size, socket.Door.transform.lossyScale);
                size.x = Mathf.Max(size.x, Mathf.Abs(doorWorldSize.x) + 0.5f);
                size.y = Mathf.Max(size.y, Mathf.Abs(doorWorldSize.y));
                size.z = Mathf.Max(1.15f, Mathf.Abs(doorWorldSize.z) + 0.4f);
            }
        }

        barrier = go.AddComponent<BoxCollider>();
        barrier.isTrigger = false;
        barrier.center = Vector3.zero;
        barrier.size = size;
    }

    void UpdateGate()
    {
        if (barrier == null)
            EnsureBarrier();
        if (barrier == null || socket == null)
            return;

        // Solo (or only one living player left): never block.
        if (PlayerRegistry.LivingCount <= 1)
        {
            latchOpen = false;
            SetBarrierBlocking(false);
            return;
        }

        // Encounter already seals doors — don't stack a second solid wall on top.
        if (IsEncounterBlocking())
        {
            latchOpen = false;
            SetBarrierBlocking(false);
            return;
        }

        // Door locked by encounter / seal — leave physics to RoomDoor.
        if (socket.Door != null && socket.Door.IsLocked)
        {
            latchOpen = false;
            SetBarrierBlocking(false);
            return;
        }

        // Sample both faces of this door only (not the far room's connected socket).
        Vector3 doorPos = socket.transform.position;
        Vector3 inward = doorPos - socket.transform.forward * 1.25f;
        Vector3 outward = doorPos + socket.transform.forward * 1.25f;

        bool allNear = PlayerRegistry.AllLivingNearEither(inward, outward, gatherRadius);
        bool anyNear = PlayerRegistry.AnyLivingNearEither(inward, outward, gatherRadius);

        if (allNear)
            latchOpen = true;
        else if (!anyNear)
            latchOpen = false;

        SetBarrierBlocking(!latchOpen);
    }

    bool IsEncounterBlocking()
    {
        RoomDefinition room = socket.Room;
        if (room != null)
        {
            RoomEncounter encounter = room.GetComponent<RoomEncounter>();
            if (encounter != null && encounter.IsInProgress)
                return true;
        }

        return false;
    }

    void SetBarrierBlocking(bool blocking)
    {
        if (barrier == null)
            return;

        barrierBlocking = blocking;
        barrier.enabled = blocking;
        barrier.isTrigger = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (socket == null)
            socket = GetComponent<RoomSocket>();
        if (socket == null)
            return;

        Vector3 doorPos = socket.transform.position;
        Vector3 inward = doorPos - socket.transform.forward * 1.25f;
        Vector3 outward = doorPos + socket.transform.forward * 1.25f;
        Gizmos.color = latchOpen ? new Color(0.2f, 1f, 0.4f, 0.35f) : new Color(1f, 0.35f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(inward, gatherRadius);
        Gizmos.DrawWireSphere(outward, gatherRadius);
    }
#endif
}
