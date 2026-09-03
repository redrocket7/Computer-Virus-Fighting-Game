using System.Collections;
using UnityEngine;

/// <summary>
/// Blocking wall that alternates between open and closed on a fixed timer.
/// Opens by lowering into the floor, closes by rising back up.
/// </summary>
public class FirewallTrap : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] float cycleInterval = 10f;
    [SerializeField] bool startsClosed = true;
    [SerializeField] float transitionDuration = 0.35f;

    [Header("Blocking")]
    [SerializeField] Collider blockingCollider;
    [SerializeField] Renderer blockingRenderer;
    [Tooltip("How far the wall drops in local space when open.")]
    [SerializeField] float openDropDistance = 6f;

    Coroutine cycleRoutine;
    bool isClosed;
    Vector3 closedLocalPosition;
    Vector3 openLocalPosition;

    void Awake()
    {
        if (blockingCollider == null)
            blockingCollider = GetComponent<Collider>();

        if (blockingRenderer == null)
            blockingRenderer = GetComponent<Renderer>();

        CachePositions();
    }

    void CachePositions()
    {
        closedLocalPosition = transform.localPosition;
        openLocalPosition = closedLocalPosition + Vector3.down * Mathf.Max(0f, openDropDistance);
    }

    void OnEnable()
    {
        CachePositions();

        if (cycleRoutine != null)
            StopCoroutine(cycleRoutine);

        isClosed = startsClosed;
        ApplyStateImmediate(isClosed);
        cycleRoutine = StartCoroutine(CycleRoutine());
    }

    void OnDisable()
    {
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }
    }

    IEnumerator CycleRoutine()
    {
        float wait = Mathf.Max(0.1f, cycleInterval);
        while (enabled)
        {
            yield return new WaitForSeconds(wait);
            yield return TransitionTo(!isClosed);
        }
    }

    IEnumerator TransitionTo(bool closed)
    {
        Vector3 fromPosition = transform.localPosition;
        Vector3 targetPosition = closed ? closedLocalPosition : openLocalPosition;
        float duration = Mathf.Max(0.01f, transitionDuration);
        float elapsed = 0f;

        if (!closed && blockingCollider != null)
            blockingCollider.enabled = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);
            transform.localPosition = Vector3.Lerp(fromPosition, targetPosition, smooth);
            yield return null;
        }

        transform.localPosition = targetPosition;
        isClosed = closed;

        if (blockingCollider != null)
            blockingCollider.enabled = closed;

        if (blockingRenderer != null)
            blockingRenderer.enabled = true;
    }

    void ApplyStateImmediate(bool closed)
    {
        isClosed = closed;
        transform.localPosition = closed ? closedLocalPosition : openLocalPosition;

        if (blockingCollider != null)
            blockingCollider.enabled = closed;

        if (blockingRenderer != null)
            blockingRenderer.enabled = true;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        cycleInterval = Mathf.Max(0.1f, cycleInterval);
        transitionDuration = Mathf.Max(0.01f, transitionDuration);
        openDropDistance = Mathf.Max(0f, openDropDistance);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 closedPos = Application.isPlaying
            ? transform.parent != null
                ? transform.parent.TransformPoint(closedLocalPosition)
                : closedLocalPosition
            : transform.position;

        Vector3 openPos = Application.isPlaying
            ? transform.parent != null
                ? transform.parent.TransformPoint(openLocalPosition)
                : openLocalPosition
            : transform.position + Vector3.down * openDropDistance;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(closedPos, transform.lossyScale);
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawLine(closedPos, openPos);
        Gizmos.DrawWireCube(openPos, transform.lossyScale * 0.85f);
    }
#endif
}
