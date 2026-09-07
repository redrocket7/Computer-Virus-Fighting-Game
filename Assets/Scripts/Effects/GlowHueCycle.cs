using UnityEngine;

/// <summary>
/// Cycles a material glow color through hue (HSV) over time.
/// Targets URP Lit <c>_EmissionColor</c> by default.
/// </summary>
[DisallowMultipleComponent]
public class GlowHueCycle : MonoBehaviour
{
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] Renderer targetRenderer;
    [Tooltip("Cycles per second. 0.1 = full rainbow every 10 seconds.")]
    [SerializeField] float hueCyclesPerSecond = 0.1f;
    [SerializeField] bool affectEmission = true;
    [SerializeField] bool affectBaseColor;
    [Tooltip("If true, edits a material instance so the shared Glow asset is not permanently changed.")]
    [SerializeField] bool useMaterialInstance = true;

    Material material;
    float emissionSaturation = 1f;
    float emissionValue = 1f;
    float emissionIntensity = 1f;
    float baseSaturation = 1f;
    float baseValue = 1f;
    float hue;

    void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null)
        {
            Debug.LogWarning($"{nameof(GlowHueCycle)} on '{name}' needs a Renderer.", this);
            enabled = false;
            return;
        }

        material = useMaterialInstance ? targetRenderer.material : targetRenderer.sharedMaterial;
        CacheStartingColors();
    }

    void OnDestroy()
    {
        if (useMaterialInstance && material != null)
            Destroy(material);
    }

    void CacheStartingColors()
    {
        if (material == null)
            return;

        if (affectEmission && material.HasProperty(EmissionColorId))
        {
            Color emission = material.GetColor(EmissionColorId);
            SplitHdrColor(emission, out hue, out emissionSaturation, out emissionValue, out emissionIntensity);
        }

        if (affectBaseColor && material.HasProperty(BaseColorId))
        {
            Color baseColor = material.GetColor(BaseColorId);
            Color.RGBToHSV(baseColor, out float baseHue, out baseSaturation, out baseValue);
            if (!affectEmission)
                hue = baseHue;
        }
    }

    void Update()
    {
        if (material == null || hueCyclesPerSecond == 0f)
            return;

        hue = Mathf.Repeat(hue + hueCyclesPerSecond * Time.deltaTime, 1f);

        if (affectEmission && material.HasProperty(EmissionColorId))
        {
            Color rgb = Color.HSVToRGB(hue, emissionSaturation, emissionValue);
            material.SetColor(EmissionColorId, rgb * emissionIntensity);
        }

        if (affectBaseColor && material.HasProperty(BaseColorId))
        {
            material.SetColor(BaseColorId, Color.HSVToRGB(hue, baseSaturation, baseValue));
        }
    }

    static void SplitHdrColor(
        Color color,
        out float h,
        out float s,
        out float v,
        out float intensity)
    {
        float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        intensity = Mathf.Max(1f, maxChannel);

        Color ldr = color;
        if (maxChannel > 1f)
            ldr = color / maxChannel;

        Color.RGBToHSV(ldr, out h, out s, out v);
    }
}
