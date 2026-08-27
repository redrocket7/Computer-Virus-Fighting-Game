using UnityEngine;

/// <summary>
/// Place on an Animation Rigging Two Bone IK Constraint Target.
/// Copies the procedural foot position into the IK target each frame
/// so Unity's rig solves the leg bend for you.
/// </summary>
public class RigTargetDriver : MonoBehaviour
{
    [Tooltip("The ProceduralLegStepper that owns this foot's world position.")]
    [SerializeField] ProceduralLegStepper stepper;

    [Tooltip("Optional: copy the stepper's transform rotation as well.")]
    [SerializeField] bool matchRotation;

    void LateUpdate()
    {
        if (stepper == null)
            return;

        transform.position = stepper.FootPosition;

        if (matchRotation)
            transform.rotation = stepper.transform.rotation;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (stepper == null)
            return;

        // In Edit Mode FootPosition is still (0,0,0) until Play — use the stepper transform instead.
        Vector3 footPos = Application.isPlaying
            ? stepper.FootPosition
            : stepper.transform.position;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, footPos);
        Gizmos.DrawWireSphere(footPos, 0.06f);
    }
#endif
}
