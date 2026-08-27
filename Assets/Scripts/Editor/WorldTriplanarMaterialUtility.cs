#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts selected materials to Custom/World Triplanar Lit so textures keep a fixed world size.
/// </summary>
public static class WorldTriplanarMaterialUtility
{
    const string ShaderName = "Custom/World Triplanar Lit";

    [MenuItem("Assets/Create/Materials/Convert To World Triplanar Lit", false, 220)]
    static void ConvertSelected()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Could not find shader '{ShaderName}'.");
            return;
        }

        int converted = 0;
        Object[] selection = Selection.objects;
        for (int i = 0; i < selection.Length; i++)
        {
            Material source = selection[i] as Material;
            if (source == null)
                continue;

            string path = AssetDatabase.GetAssetPath(source);
            string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}_WorldTriplanar.mat");

            Material clone = new Material(source)
            {
                name = source.name + "_WorldTriplanar",
                shader = shader
            };

            // Keep maps/colors from the original Lit material where names match.
            CopyIfPresent(source, clone, "_BaseMap");
            CopyIfPresent(source, clone, "_BumpMap");
            CopyIfPresent(source, clone, "_MetallicGlossMap");
            CopyIfPresent(source, clone, "_SpecGlossMap");
            CopyIfPresent(source, clone, "_EmissionMap");

            if (source.HasProperty("_BaseColor") && clone.HasProperty("_BaseColor"))
                clone.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            if (source.HasProperty("_BumpScale") && clone.HasProperty("_BumpScale"))
                clone.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
            if (source.HasProperty("_Metallic") && clone.HasProperty("_Metallic"))
                clone.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Smoothness") && clone.HasProperty("_Smoothness"))
                clone.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
            if (source.HasProperty("_SpecColor") && clone.HasProperty("_SpecColor"))
                clone.SetColor("_SpecColor", source.GetColor("_SpecColor"));
            if (source.HasProperty("_EmissionColor") && clone.HasProperty("_EmissionColor"))
                clone.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));

            // Match Yughues / URP Lit specular setup.
            SetKeyword(clone, "_SPECULAR_SETUP", true);
            if (clone.HasProperty("_SpecularSetup"))
                clone.SetFloat("_SpecularSetup", 1f);
            if (clone.HasProperty("_WorkflowMode"))
                clone.SetFloat("_WorkflowMode", 0f);

            bool hasNormal = source.GetTexture("_BumpMap") != null;
            SetKeyword(clone, "_NORMALMAP", hasNormal);
            if (clone.HasProperty("_NormalMapOn"))
                clone.SetFloat("_NormalMapOn", hasNormal ? 1f : 0f);

            SetKeyword(clone, "_METALLICSPECGLOSSMAP", true);
            if (clone.HasProperty("_MetallicSpecMapOn"))
                clone.SetFloat("_MetallicSpecMapOn", 1f);

            bool hasEmission = source.IsKeywordEnabled("_EMISSION");
            SetKeyword(clone, "_EMISSION", hasEmission);
            if (clone.HasProperty("_EmissionOn"))
                clone.SetFloat("_EmissionOn", hasEmission ? 1f : 0f);

            if (clone.HasProperty("_Smoothness"))
            {
                float smoothness = source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 1f;
                clone.SetFloat("_Smoothness", smoothness);
            }

            // Sensible default: one tile every 4 world units.
            if (clone.HasProperty("_WorldTiling"))
                clone.SetFloat("_WorldTiling", 0.25f);
            if (clone.HasProperty("_BlendSharpness"))
                clone.SetFloat("_BlendSharpness", 4f);

            AssetDatabase.CreateAsset(clone, newPath);
            converted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(converted > 0
            ? $"Created {converted} world-triplanar material(s)."
            : "Select one or more Materials first.");
    }

    [MenuItem("Assets/Create/Materials/Convert To World Triplanar Lit", true)]
    static bool ConvertSelectedValidate()
    {
        Object[] selection = Selection.objects;
        for (int i = 0; i < selection.Length; i++)
        {
            if (selection[i] is Material)
                return true;
        }

        return false;
    }

    static void CopyIfPresent(Material source, Material dest, string property)
    {
        if (!source.HasProperty(property) || !dest.HasProperty(property))
            return;

        dest.SetTexture(property, source.GetTexture(property));
    }

    static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }
}
#endif
