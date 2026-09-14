using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Follows one or more players at a fixed pitch/yaw.
/// In local co-op, frames the midpoint of living players and zooms out with separation.
/// </summary>
public class TopDownCameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform target;
    [SerializeField] bool usePlayerRegistry;

    [Header("Angle")]
    [Tooltip("Yaw: horizontal rotation around the player in degrees (0 = behind, 90 = right side).")]
    [SerializeField] float yaw = 0f;
    [Tooltip("Pitch: vertical tilt in degrees (90 = straight down, 45 = isometric-style).")]
    [SerializeField][Range(10f, 90f)] float pitch = 60f;
    [Tooltip("Distance from the player along the camera's back direction.")]
    [SerializeField] float distance = 15f;
    [Tooltip("World-space Z shift of the follow point. Positive moves the frame forward.")]
    [SerializeField] float zOffset = 0f;

    [Header("Co-op Framing")]
    [SerializeField] float minDistance = 15f;
    [SerializeField] float maxDistance = 32f;
    [SerializeField] float separationZoomScale = 0.55f;

    [Header("Smoothing")]
    [SerializeField] float smoothTime = 0.15f;

    readonly List<Transform> targets = new List<Transform>(4);
    Vector3 velocity = Vector3.zero;

    public Transform Target => target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        usePlayerRegistry = false;
        targets.Clear();
        if (newTarget != null)
            targets.Add(newTarget);
    }

    public void UsePlayerRegistry()
    {
        usePlayerRegistry = true;
        RefreshTargetsFromRegistry();
    }

    public void SetTargets(IReadOnlyList<Transform> newTargets)
    {
        usePlayerRegistry = false;
        targets.Clear();
        if (newTargets == null)
            return;

        for (int i = 0; i < newTargets.Count; i++)
        {
            if (newTargets[i] != null)
                targets.Add(newTargets[i]);
        }
    }

    void LateUpdate()
    {
        if (usePlayerRegistry)
            RefreshTargetsFromRegistry();

        if (!TryGetFollowPoint(out Vector3 followPoint, out float framedDistance))
            return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -framedDistance);
        Vector3 desiredPosition = followPoint + new Vector3(0f, 0f, zOffset) + offset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            smoothTime);

        transform.rotation = rotation;
    }

    void RefreshTargetsFromRegistry()
    {
        targets.Clear();
        IReadOnlyList<PlayerController> players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerController player = players[i];
            if (player == null || player.IsDead)
                continue;
            targets.Add(player.transform);
        }

        if (targets.Count == 0 && target != null)
            targets.Add(target);
    }

    bool TryGetFollowPoint(out Vector3 followPoint, out float framedDistance)
    {
        framedDistance = Mathf.Max(minDistance, distance);

        if (targets.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == null)
                    continue;
                sum += targets[i].position;
                count++;
            }

            if (count > 0)
            {
                followPoint = sum / count;
                if (count >= 2)
                {
                    float separation = 0f;
                    Transform first = null;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (targets[i] == null)
                            continue;
                        if (first == null)
                        {
                            first = targets[i];
                            continue;
                        }

                        Vector3 delta = first.position - targets[i].position;
                        delta.y = 0f;
                        separation = Mathf.Max(separation, delta.magnitude);
                    }

                    framedDistance = Mathf.Clamp(
                        minDistance + separation * separationZoomScale,
                        minDistance,
                        maxDistance);
                }

                return true;
            }
        }

        if (target != null)
        {
            followPoint = target.position;
            return true;
        }

        followPoint = Vector3.zero;
        return false;
    }
}
