using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Legacy minimal motor. Prefer <see cref="PlayerController"/> on networked players.
/// Kept for older NetworkPlayer prefabs until rebuilt via Tools > Virus Game > Rebuild Network Player Prefab.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[System.Obsolete("Use PlayerController (network-aware) instead of NetworkPlayerMotor.")]
public class NetworkPlayerMotor : NetworkBehaviour
{
    [SerializeField] float moveSpeed = 7f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] Renderer bodyRenderer;

    static readonly Color[] PlayerColors =
    {
        new Color(0.25f, 0.85f, 1f),
        new Color(1f, 0.55f, 0.2f),
        new Color(0.45f, 1f, 0.45f),
        new Color(0.95f, 0.4f, 0.85f)
    };

    public override void OnNetworkSpawn()
    {
        ApplyColor();

        if (IsOwner)
            name = $"Network Player (You #{OwnerClientId})";
        else
            name = $"Network Player (#{OwnerClientId})";

        // Owner-authoritative transform: only the owning client may set the spawn pose.
        if (IsOwner)
            PlaceAtSpawnSlot((int)OwnerClientId);
    }

    void Update()
    {
        if (!IsOwner || !IsSpawned)
            return;

        Vector2 input = ReadMoveInput();
        Vector3 direction = new Vector3(input.x, 0f, input.y);
        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.position += direction * (moveSpeed * Time.deltaTime);

            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.deltaTime);
        }
    }

    void PlaceAtSpawnSlot(int slot)
    {
        float angle = slot * 90f;
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.5f;
        transform.position = new Vector3(offset.x, 0.5f, offset.z);
        transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
    }

    void ApplyColor()
    {
        if (bodyRenderer == null)
            bodyRenderer = GetComponentInChildren<Renderer>();
        if (bodyRenderer == null)
            return;

        Color color = PlayerColors[OwnerClientId % (ulong)PlayerColors.Length];
        Material material = bodyRenderer.material;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
    }

    static Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                input.x -= 1f;
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.05f)
                input += stick;
        }

        return Vector2.ClampMagnitude(input, 1f);
    }
}
