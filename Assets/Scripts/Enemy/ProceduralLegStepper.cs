using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to each leg's "foot" GameObject.
/// When the foot drifts farther than stepDistance from its rest point,
/// it steps through/past the rest point and overshoots to the opposite side.
/// </summary>
public class ProceduralLegStepper : MonoBehaviour
{
    [Tooltip("The position the foot ideally wants to rest at, in world space (set by SpiderBody at runtime).")]
    public Vector3 HomePosition { get; set; }

    [Header("Step Settings")]
    [Tooltip("How far the foot can wander from its home before a step is triggered.")]
    [SerializeField] float stepDistance = 1f;
    [Tooltip("How long a single step takes in seconds.")]
    [SerializeField] float stepDuration = 0.15f;
    [Tooltip("How high the foot lifts off the ground during a step.")]
    [SerializeField] float stepHeight = 0.3f;
    [Tooltip("1 = land mirrored on the other side of the rest point (double the original travel). 0 = land exactly on rest point.")]
    [SerializeField] float overshootMultiplier = 1f;
    [Tooltip("Layer mask for the ground raycast.")]
    [SerializeField] LayerMask groundMask = ~0;
    [Tooltip("Distance to cast downward to find the ground.")]
    [SerializeField] float groundRayLength = 5f;

    public bool IsStepping { get; private set; }
    public float StepProgress { get; private set; }

    public Vector3 FootPosition { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;

    void Awake()
    {
        FootPosition = transform.position;
        HomePosition = transform.position;
        GroundNormal = Vector3.up;
        FootPosition = SnapToGround(FootPosition);
    }

    void LateUpdate()
    {
        transform.position = FootPosition;
    }

    public bool NeedsStep()
    {
        float distSqr = stepDistance * stepDistance;
        return !IsStepping &&
               (FootPosition - HomePosition).sqrMagnitude > distSqr;
    }

    public void TryStep()
    {
        if (IsStepping) return;
        StartCoroutine(PerformStep());
    }

    IEnumerator PerformStep()
    {
        IsStepping = true;
        StepProgress = 0f;

        Vector3 startPosition = FootPosition;
        Vector3 offsetFromHome = startPosition - HomePosition;
        Vector3 desiredLanding = HomePosition - offsetFromHome * overshootMultiplier;
        Vector3 targetPosition = SnapToGround(desiredLanding);

        float elapsed = 0f;
        while (elapsed < stepDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stepDuration);
            StepProgress = t;
            float smooth = t * t * (3f - 2f * t);
            float arc = Mathf.Sin(t * Mathf.PI) * stepHeight;

            FootPosition = Vector3.Lerp(startPosition, targetPosition, smooth) + Vector3.up * arc;
            yield return null;
        }

        FootPosition = targetPosition;
        StepProgress = 1f;
        IsStepping = false;
    }

    Vector3 SnapToGround(Vector3 worldPos)
    {
        Ray ray = new Ray(worldPos + Vector3.up * (groundRayLength * 0.5f), Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, groundRayLength, groundMask))
        {
            GroundNormal = hit.normal;
            return hit.point;
        }

        return worldPos;
    }
}
