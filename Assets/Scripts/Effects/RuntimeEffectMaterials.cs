using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Build-safe runtime materials for VFX.
/// Uses a project shader (Custom/RuntimeEffectUnlit) so player builds do not strip
/// URP Unlit via Shader.Find / unused transparent variants.
/// </summary>
public static class RuntimeEffectMaterials
{
    public const string EffectShaderName = "Custom/RuntimeEffectUnlit";

    static Shader effectShader;
    static bool loggedMissing;

    public static Shader EffectUnlit
    {
        get
        {
            if (effectShader == null)
                effectShader = Shader.Find(EffectShaderName);
            return effectShader;
        }
    }

    public static Material CreateTransparent(Color color, bool emissive = false, string materialName = "RuntimeEffect")
    {
        Shader shader = EffectUnlit;
        if (shader == null)
        {
            // Last-resort editor/dev fallbacks — these can be stripped in player builds.
            shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Unlit/Color");

            if (!loggedMissing)
            {
                loggedMissing = true;
                Debug.LogError(
                    $"RuntimeEffectMaterials: missing shader '{EffectShaderName}'. " +
                    "Effects may look wrong in player builds. Ensure Assets/Shaders/RuntimeEffectUnlit.shader is included.");
            }
        }

        if (shader == null)
            return new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Unlit/Color"))
            {
                name = materialName,
                color = color
            };

        var material = new Material(shader)
        {
            name = materialName,
            renderQueue = 3000,
            hideFlags = HideFlags.DontSave
        };

        ApplyTransparentUrpKeywordsIfNeeded(material, shader);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetColor("_EmissionColor", emissive
            ? new Color(color.r, color.g, color.b, 0f) * 1.5f
            : Color.black);
        material.SetInt("_Cull", (int)CullMode.Off);
        return material;
    }

    public static Material CreateOpaque(Color color, string materialName = "RuntimeOpaque")
    {
        Shader shader = EffectUnlit
                        ?? Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            return CreateTransparent(color, emissive: false, materialName);

        var material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave
        };

        Color opaque = color;
        opaque.a = 1f;
        material.SetColor("_BaseColor", opaque);
        material.SetColor("_Color", opaque);
        material.SetColor("_EmissionColor", Color.black);
        return material;
    }

    static void ApplyTransparentUrpKeywordsIfNeeded(Material material, Shader shader)
    {
        if (shader == null || !shader.name.Contains("Universal Render Pipeline"))
            return;

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Warmup()
    {
        // Touch the shader early so player builds resolve Custom/RuntimeEffectUnlit.
        _ = EffectUnlit;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        effectShader = null;
        loggedMissing = false;
    }
}
