using UnityEngine;

/// <summary>
/// Follows the player at a fixed distance and configurable pitch/yaw angle.
/// Attach to the Camera. Assign the player transform in the Inspector.
/// </summary>
public class TopDownCameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform target;

    [Header("Angle")]
    [Tooltip("Yaw: horizontal rotation around the player in degrees (0 = behind, 90 = right side).")]
    [SerializeField] float yaw = 0f;
    [Tooltip("Pitch: vertical tilt in degrees (90 = straight down, 45 = isometric-style).")]
    [SerializeField][Range(10f, 90f)] float pitch = 60f;
    [Tooltip("Distance from the player along the camera's back direction.")]
    [SerializeField] float distance = 15f;
    [Tooltip("World-space Z shift of the follow point. Positive moves the frame forward.")]
    [SerializeField] float zOffset = 0f;

    [Header("Smoothing")]
    [SerializeField] float smoothTime = 0.15f;

    Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null)
            return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);

        Vector3 followPoint = target.position + new Vector3(0f, 0f, zOffset);
        Vector3 desiredPosition = followPoint + offset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            smoothTime);

        transform.rotation = rotation;
    }
}
