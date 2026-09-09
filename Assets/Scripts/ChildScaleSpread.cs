using UnityEngine;

/// <summary>
/// Parent helper for a center piece plus two end pieces.
/// Sets the center child's scale on the chosen axis to <see cref="Scale"/>,
/// and places the ends at local ±(Scale + 1) / 2 on that axis.
/// </summary>
public class ChildScaleSpread : MonoBehaviour
{
    public enum Axis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    [SerializeField, Min(0.01f)] float scale = 3f;
    [SerializeField] Axis axis = Axis.X;

    [Tooltip("Gets localScale on the chosen axis set to Scale.")]
    [SerializeField] Transform scaleTarget;

    [Tooltip("Moved to +(Scale + 1) / 2 on the chosen local axis.")]
    [SerializeField] Transform positiveEnd;

    [Tooltip("Moved to -(Scale + 1) / 2 on the chosen local axis.")]
    [SerializeField] Transform negativeEnd;

    public float Scale
    {
        get => scale;
        set
        {
            scale = Mathf.Max(0.01f, value);
            Apply();
        }
    }

    public Axis StretchAxis
    {
        get => axis;
        set
        {
            axis = value;
            Apply();
        }
    }

    void OnEnable()
    {
        Apply();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        scale = Mathf.Max(0.01f, scale);
        Apply();
    }
#endif

    public void Apply()
    {
        float halfExtent = (scale + 1f) * 0.5f;
        int axisIndex = (int)axis;

        if (scaleTarget != null)
        {
            Vector3 localScale = scaleTarget.localScale;
            localScale[axisIndex] = scale;
            scaleTarget.localScale = localScale;
        }

        if (positiveEnd != null)
        {
            Vector3 localPosition = positiveEnd.localPosition;
            localPosition[axisIndex] = halfExtent;
            positiveEnd.localPosition = localPosition;
        }

        if (negativeEnd != null)
        {
            Vector3 localPosition = negativeEnd.localPosition;
            localPosition[axisIndex] = -halfExtent;
            negativeEnd.localPosition = localPosition;
        }
    }
}
