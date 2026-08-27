using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Attach to the spider body root.
///
/// Leg Pairs (feet only) alternate steps — only one foot per pair lifts at a time.
/// Rest Points are a matching list in pair order: pair0.A, pair0.B, pair1.A, pair1.B, ...
///
/// Rest facing uses a smoothed rotation (so fast turns don't yank all homes),
/// and homes are pushed forward along body velocity (look-ahead) for natural strides.
/// </summary>
public class ProceduralSpiderBody : MonoBehaviour
{
    [System.Serializable]
    public struct LegPair
    {
        public ProceduralLegStepper footA;
        public ProceduralLegStepper footB;
    }

    [Header("Legs")]
    [Tooltip("Opposite feet grouped together. Only one foot in a pair steps at a time.")]
    [SerializeField] LegPair[] legPairs;
    [Tooltip("Rest transforms in pair order: pair0.footA, pair0.footB, pair1.footA, pair1.footB, ...")]
    [SerializeField] Transform[] restPoints;
    [Tooltip("Pause after a foot starts stepping before the next foot in the gait can lift.")]
    [SerializeField] float stepDelay = 0.08f;
    [Tooltip("How far through a step (0-1) before the next foot is allowed to lift. 0.5 = midway.")]
    [SerializeField][Range(0f, 1f)] float nextStepAtProgress = 0.5f;

    [Header("Stance / Look-Ahead")]
    [Tooltip("How quickly the stance rotation catches up to the body facing. Lower = more stable on fast turns.")]
    [SerializeField] float stanceRotationSpeed = 4f;
    [Tooltip("How far ahead (in seconds) to push rest points along movement velocity.")]
    [SerializeField] float lookAheadTime = 0.25f;
    [Tooltip("Ignore look-ahead when horizontal speed is below this.")]
    [SerializeField] float lookAheadMinSpeed = 0.15f;

    public void SetLookAheadTime(float seconds)
    {
        lookAheadTime = Mathf.Max(0f, seconds);
    }

    [Header("Body Tilt")]
    [SerializeField] Transform bodyMesh;
    [Tooltip("How much the body tilts to match the average ground normal.")]
    [SerializeField] float tiltSmoothing = 6f;
    [SerializeField] float tiltUpdateInterval = 0.1f;

    Quaternion stanceRotation;
    Vector3 lastRootPosition;
    Vector3 horizontalVelocity;
    NavMeshAgent agent;
    bool stanceInitialized;
    int gaitIndex;
    float stepDelayTimer;
    float tiltUpdateTimer;
    Quaternion tiltTargetRotation;
    bool hasTiltTarget;
    readonly System.Collections.Generic.List<ProceduralLegStepper> orderedLegs =
        new System.Collections.Generic.List<ProceduralLegStepper>(8);
    bool orderedLegsDirty = true;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        stanceRotation = transform.rotation;
        lastRootPosition = transform.position;
        stanceInitialized = true;
        RebuildOrderedLegs();
    }

    void OnValidate()
    {
        orderedLegsDirty = true;
    }

    void Update()
    {
        UpdateVelocityAndStance();
        UpdateLegTargets();
        TryStepLegs();
        UpdateBodyTilt();
    }

    void RebuildOrderedLegs()
    {
        orderedLegs.Clear();
        orderedLegsDirty = false;
        if (legPairs == null)
            return;

        for (int i = 0; i < legPairs.Length; i++)
        {
            LegPair pair = legPairs[i];
            if (pair.footA != null)
                orderedLegs.Add(pair.footA);
            if (pair.footB != null)
                orderedLegs.Add(pair.footB);
        }
    }

    void UpdateVelocityAndStance()
    {
        if (!stanceInitialized)
        {
            stanceRotation = transform.rotation;
            lastRootPosition = transform.position;
            stanceInitialized = true;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            horizontalVelocity = agent.velocity;
        }
        else
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            horizontalVelocity = (transform.position - lastRootPosition) / dt;
        }

        horizontalVelocity.y = 0f;
        lastRootPosition = transform.position;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
        {
            Quaternion targetYaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
            stanceRotation = Quaternion.Slerp(
                stanceRotation,
                targetYaw,
                1f - Mathf.Exp(-stanceRotationSpeed * Time.deltaTime));
        }
    }

    void UpdateLegTargets()
    {
        if (legPairs == null || restPoints == null)
            return;

        int restIndex = 0;
        for (int i = 0; i < legPairs.Length; i++)
        {
            AssignHome(legPairs[i].footA, ref restIndex);
            AssignHome(legPairs[i].footB, ref restIndex);
        }
    }

    void AssignHome(ProceduralLegStepper foot, ref int restIndex)
    {
        if (foot == null)
            return;

        if (restIndex >= restPoints.Length || restPoints[restIndex] == null)
        {
            restIndex++;
            return;
        }

        foot.HomePosition = GetSmoothedHome(restPoints[restIndex]);
        restIndex++;
    }

    Vector3 GetSmoothedHome(Transform rest)
    {
        Vector3 localOffset = transform.InverseTransformPoint(rest.position);
        Vector3 home = transform.position + stanceRotation * localOffset;
        home.y = rest.position.y;

        if (horizontalVelocity.sqrMagnitude >= lookAheadMinSpeed * lookAheadMinSpeed)
            home += horizontalVelocity * lookAheadTime;

        return home;
    }

    void TryStepLegs()
    {
        if (orderedLegsDirty)
            RebuildOrderedLegs();

        if (orderedLegs.Count == 0)
            return;

        if (stepDelayTimer > 0f)
            stepDelayTimer -= Time.deltaTime;

        for (int i = 0; i < orderedLegs.Count; i++)
        {
            ProceduralLegStepper foot = orderedLegs[i];
            if (foot.IsStepping && foot.StepProgress < nextStepAtProgress)
                return;
        }

        if (stepDelayTimer > 0f)
            return;

        if (gaitIndex >= orderedLegs.Count)
            gaitIndex = 0;

        for (int n = 0; n < orderedLegs.Count; n++)
        {
            int i = (gaitIndex + n) % orderedLegs.Count;
            if (!orderedLegs[i].NeedsStep())
                continue;

            orderedLegs[i].TryStep();
            gaitIndex = (i + 1) % orderedLegs.Count;
            stepDelayTimer = Mathf.Max(0f, stepDelay);
            return;
        }
    }

    void UpdateBodyTilt()
    {
        if (bodyMesh == null)
            return;

        if (orderedLegsDirty)
            RebuildOrderedLegs();

        if (orderedLegs.Count == 0)
            return;

        tiltUpdateTimer -= Time.deltaTime;
        if (tiltUpdateTimer <= 0f)
        {
            tiltUpdateTimer = Mathf.Max(0.02f, tiltUpdateInterval);

            int count = 0;
            Vector3 avgNormal = Vector3.zero;
            for (int i = 0; i < orderedLegs.Count; i++)
            {
                ProceduralLegStepper foot = orderedLegs[i];
                if (foot == null)
                    continue;

                avgNormal += foot.GroundNormal;
                count++;
            }

            if (count > 0)
            {
                avgNormal /= count;
                if (avgNormal.sqrMagnitude > 0.0001f)
                {
                    tiltTargetRotation = Quaternion.FromToRotation(transform.up, avgNormal) * transform.rotation;
                    hasTiltTarget = true;
                }
            }
        }

        if (!hasTiltTarget)
            return;

        bodyMesh.rotation = Quaternion.Slerp(bodyMesh.rotation, tiltTargetRotation, Time.deltaTime * tiltSmoothing);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (restPoints != null)
        {
            Gizmos.color = Color.green;
            foreach (var rest in restPoints)
            {
                if (rest != null)
                    Gizmos.DrawWireSphere(rest.position, 0.08f);
            }
        }

        if (Application.isPlaying && restPoints != null)
        {
            Gizmos.color = Color.magenta;
            foreach (var rest in restPoints)
            {
                if (rest != null)
                    Gizmos.DrawWireSphere(GetSmoothedHome(rest), 0.1f);
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, horizontalVelocity);
        }

        if (legPairs == null)
            return;

        Gizmos.color = Color.cyan;
        foreach (var pair in legPairs)
        {
            if (pair.footA != null) Gizmos.DrawSphere(pair.footA.FootPosition, 0.08f);
            if (pair.footB != null) Gizmos.DrawSphere(pair.footB.FootPosition, 0.08f);
        }
    }
#endif
}
