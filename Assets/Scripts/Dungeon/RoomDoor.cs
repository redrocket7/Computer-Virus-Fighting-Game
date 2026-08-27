using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Physical + NavMesh blocker for a room exit, with a slide open/close animation.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NavMeshObstacle))]
public class RoomDoor : MonoBehaviour
{
    const float SealedDepth = 0.001f;

    [Header("Animation")]
    [SerializeField] float animDuration = 0.4f;
    [Tooltip("Local-space offset applied when the door is fully open (default: slide down into the floor).")]
    [SerializeField] Vector3 openLocalOffset = new Vector3(0f, -4.5f, 0f);
    [SerializeField] AnimationCurve animCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    BoxCollider blocker;
    NavMeshObstacle obstacle;
    MeshRenderer meshRenderer;
    bool permanentlySealed;
    bool restPoseCached;
    Vector3 closedLocalPosition;
    Vector3 openLocalPosition;
    Coroutine animRoutine;
    float animProgress; // 0 = closed, 1 = open

    public bool IsLocked { get; private set; }

    void Awake()
    {
        Cache();
        CacheRestPose();
        if (!permanentlySealed)
            SetLocked(false, instant: true);
    }

    void Cache()
    {
        if (blocker == null)
            blocker = GetComponent<BoxCollider>();
        if (obstacle == null)
            obstacle = GetComponent<NavMeshObstacle>();
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
    }

    void CacheRestPose()
    {
        if (restPoseCached)
            return;

        closedLocalPosition = transform.localPosition;
        openLocalPosition = closedLocalPosition + openLocalOffset;
        restPoseCached = true;
    }

    public void SetLocked(bool locked)
    {
        SetLocked(locked, instant: false);
    }

    public void SetLocked(bool locked, bool instant)
    {
        Cache();
        CacheRestPose();

        if (permanentlySealed)
            locked = true;

        IsLocked = locked;
        ApplyCollisionState(locked);

        float targetProgress = locked ? 0f : 1f;
        if (instant || !isActiveAndEnabled || animDuration <= 0.001f)
        {
            StopAnim();
            animProgress = targetProgress;
            ApplyVisualProgress(animProgress);
            return;
        }

        if (Mathf.Abs(animProgress - targetProgress) < 0.001f)
        {
            ApplyVisualProgress(animProgress);
            return;
        }

        StopAnim();
        animRoutine = StartCoroutine(AnimateTo(targetProgress));
    }

    public void SealPermanently()
    {
        permanentlySealed = true;
        Cache();
        CacheRestPose();

        // Return to the closed pose before flattening unused exits.
        StopAnim();
        animProgress = 0f;
        transform.localPosition = closedLocalPosition;

        Vector3 scale = transform.localScale;
        scale.z = SealedDepth;
        transform.localScale = scale;

        SetLocked(true, instant: true);
    }

    void ApplyCollisionState(bool locked)
    {
        if (blocker != null)
        {
            blocker.enabled = true;
            blocker.isTrigger = !locked;
        }

        if (obstacle != null)
        {
            obstacle.carving = locked;
            obstacle.enabled = locked;
        }
    }

    void ApplyVisualProgress(float progress)
    {
        float t = animCurve != null ? animCurve.Evaluate(progress) : progress;
        transform.localPosition = Vector3.LerpUnclamped(closedLocalPosition, openLocalPosition, t);

        if (meshRenderer != null)
        {
            // Hide only when fully open to avoid seeing a sunken door slab.
            meshRenderer.enabled = progress < 0.999f || permanentlySealed;
        }
    }

    IEnumerator AnimateTo(float targetProgress)
    {
        float start = animProgress;
        float duration = Mathf.Max(0.01f, animDuration);
        // Scale duration by remaining distance so mid-reverse stays snappy.
        duration *= Mathf.Clamp01(Mathf.Abs(targetProgress - start));
        if (duration < 0.01f)
            duration = 0.01f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            animProgress = Mathf.Lerp(start, targetProgress, u);
            ApplyVisualProgress(animProgress);
            yield return null;
        }

        animProgress = targetProgress;
        ApplyVisualProgress(animProgress);
        animRoutine = null;
    }

    void StopAnim()
    {
        if (animRoutine == null)
            return;

        StopCoroutine(animRoutine);
        animRoutine = null;
    }

    void OnDisable()
    {
        StopAnim();
    }
}
