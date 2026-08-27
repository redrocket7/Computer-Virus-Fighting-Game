using UnityEngine;

/// <summary>
/// Custom two-bone IK solver for a single spider leg.
/// Prefer Unity Animation Rigging (Two Bone IK Constraint + RigTargetDriver)
/// if you want visual setup in the Editor. Keep this script if you are not
/// using the Animation Rigging package.
///
/// Hierarchy expected per leg:
///   Shoulder  (this script lives here)
///   └─ UpperLeg  (cube/cylinder, assigned to upperBone)
///      └─ LowerLeg  (cube/cylinder, assigned to lowerBone)
///         └─ FootMesh  (optional visual at the tip)
///
/// The foot TARGET is driven by a ProceduralLegStepper on a separate empty
/// GameObject — assign that stepper to footStepper.
/// </summary>
public class LegIK : MonoBehaviour
{
    [Header("Bones")]
    [Tooltip("The upper leg bone (child of shoulder).")]
    [SerializeField] Transform upperBone;
    [Tooltip("The lower leg bone (child of upper leg).")]
    [SerializeField] Transform lowerBone;
    [Tooltip("Length of the upper leg segment.")]
    [SerializeField] float upperLength = 0.5f;
    [Tooltip("Length of the lower leg segment.")]
    [SerializeField] float lowerLength = 0.5f;

    [Header("Foot")]
    [Tooltip("The ProceduralLegStepper whose FootPosition drives the IK target.")]
    [SerializeField] ProceduralLegStepper footStepper;

    [Header("Pole Vector")]
    [Tooltip("A transform that the knee bends toward. Place it to the side/above the leg so the knee bends in the right direction.")]
    [SerializeField] Transform poleTarget;

    [Header("Visuals")]
    [Tooltip("Scale the bones along their local Y axis to match their IK length (tick this if your bones are unit-length cubes/cylinders).")]
    [SerializeField] bool scaleBonesToLength = true;

    void LateUpdate()
    {
        if (upperBone == null || lowerBone == null || footStepper == null)
            return;

        Vector3 root = transform.position;         // shoulder
        Vector3 target = footStepper.FootPosition; // foot target from stepper

        SolveTwoBoneIK(root, target);
    }

    void SolveTwoBoneIK(Vector3 root, Vector3 target)
    {
        float a = upperLength;
        float b = lowerLength;
        float c = Vector3.Distance(root, target);
        c = Mathf.Clamp(c, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);

        // Law of cosines: angle at root (shoulder → knee)
        float angleA = Mathf.Acos(Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f));
        // Angle at upper bone (knee bend)
        float angleB = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f));

        Vector3 toTarget = (target - root).normalized;

        // Pole vector: defines which way the knee bends
        Vector3 poleDir = poleTarget != null
            ? (poleTarget.position - root).normalized
            : transform.right;

        // Build a plane perpendicular to the reach direction, containing the pole
        Vector3 binormal = Vector3.Cross(toTarget, poleDir);
        if (binormal.sqrMagnitude < 0.0001f)
            binormal = Vector3.Cross(toTarget, transform.up);
        binormal.Normalize();
        Vector3 normal = Vector3.Cross(binormal, toTarget).normalized;

        // Shoulder rotation: rotate toTarget by angleA around the pole-plane normal
        Quaternion shoulderRot = Quaternion.LookRotation(toTarget, normal) *
                                 Quaternion.Euler(Mathf.Rad2Deg * angleA, 0f, 0f);
        upperBone.rotation = shoulderRot;

        // Knee position
        Vector3 kneePos = root + upperBone.forward * a;

        // Lower bone points from knee toward foot
        Vector3 toFoot = (target - kneePos).normalized;
        lowerBone.rotation = Quaternion.LookRotation(toFoot, upperBone.up);

        if (scaleBonesToLength)
        {
            ScaleBone(upperBone, a);
            ScaleBone(lowerBone, b);
        }
    }

    // Scales bone along local Z (forward) to match the IK segment length.
    void ScaleBone(Transform bone, float length)
    {
        Vector3 s = bone.localScale;
        // Assumes the bone mesh is 1 unit long along its local Z axis.
        bone.localScale = new Vector3(s.x, s.y, length);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (footStepper == null || upperBone == null || lowerBone == null) return;

        Vector3 shoulder = transform.position;
        Vector3 knee = shoulder + upperBone.forward * upperLength;
        Vector3 foot = footStepper.FootPosition;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(shoulder, knee);
        Gizmos.DrawLine(knee, foot);
        Gizmos.DrawWireSphere(knee, 0.05f);
        Gizmos.DrawWireSphere(foot, 0.05f);

        if (poleTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(knee, poleTarget.position);
            Gizmos.DrawWireSphere(poleTarget.position, 0.05f);
        }
    }
#endif
}
