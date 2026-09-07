using UnityEngine;

/// <summary>
/// Parent helper: sets assigned child X/Z scales from two parameters.
/// Full-scale targets use <c>ScaleX</c>/<c>ScaleZ</c>;
/// reduced targets use those values minus 1.
/// Y scale is left unchanged.
/// </summary>
public class ChildScaleGroup : MonoBehaviour
{
    [SerializeField, Min(1f)] float scaleX = 3f;
    [SerializeField, Min(1f)] float scaleZ = 3f;

    [Tooltip("These objects get localScale.x/z = ScaleX / ScaleZ.")]
    [SerializeField] Transform[] fullScaleTargets;

    [Tooltip("These objects get localScale.x/z = ScaleX - 1 / ScaleZ - 1.")]
    [SerializeField] Transform[] reducedScaleTargets;

    public float ScaleX
    {
        get => scaleX;
        set
        {
            scaleX = Mathf.Max(1f, value);
            ApplyScale();
        }
    }

    public float ScaleZ
    {
        get => scaleZ;
        set
        {
            scaleZ = Mathf.Max(1f, value);
            ApplyScale();
        }
    }

    void OnEnable()
    {
        ApplyScale();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        scaleX = Mathf.Max(1f, scaleX);
        scaleZ = Mathf.Max(1f, scaleZ);
        ApplyScale();
    }
#endif

    public void ApplyScale()
    {
        float fullX = scaleX;
        float fullZ = scaleZ;
        float reducedX = Mathf.Max(0.01f, scaleX - 1f);
        float reducedZ = Mathf.Max(0.01f, scaleZ - 1f);

        ApplyXz(fullScaleTargets, fullX, fullZ);
        ApplyXz(reducedScaleTargets, reducedX, reducedZ);
    }

    static void ApplyXz(Transform[] targets, float x, float z)
    {
        if (targets == null)
            return;

        for (int i = 0; i < targets.Length; i++)
        {
            Transform target = targets[i];
            if (target == null)
                continue;

            Vector3 localScale = target.localScale;
            localScale.x = x;
            localScale.z = z;
            target.localScale = localScale;
        }
    }
}
