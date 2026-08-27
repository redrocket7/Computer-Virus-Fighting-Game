using UnityEngine;
using Unity.AI.Navigation;

/// <summary>
/// Shared helpers for room/passage fog lids.
/// </summary>
public static class FogCoverVisual
{
    public static GameObject EnsureCoverObject(Transform parent, string objectName, int layer)
    {
        Transform existing = parent.Find(objectName);
        GameObject coverObject;
        if (existing != null)
        {
            coverObject = existing.gameObject;
        }
        else
        {
            coverObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            coverObject.name = objectName;
            Collider coverCollider = coverObject.GetComponent<Collider>();
            if (coverCollider != null)
                Object.Destroy(coverCollider);
        }

        coverObject.transform.SetParent(parent, false);
        coverObject.layer = layer;

        if (!coverObject.TryGetComponent(out NavMeshModifier modifier))
            modifier = coverObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;

        return coverObject;
    }

    public static Material ApplyAppearance(
        MeshRenderer renderer,
        Material coverMaterial,
        Color coverColor,
        bool applyColorOverride,
        ref Material coverInstance)
    {
        if (renderer == null)
            return coverInstance;

        Material source = coverMaterial;
        bool createdTemp = false;
        if (source == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            source = new Material(shader) { name = "Fog Cover Fallback" };
            createdTemp = true;
        }

        if (coverInstance == null || coverInstance.shader != source.shader)
        {
            if (coverInstance != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(coverInstance);
                else
                    Object.DestroyImmediate(coverInstance);
            }

            coverInstance = new Material(source) { name = "Fog Cover Instance" };
        }
        else
        {
            coverInstance.CopyPropertiesFromMaterial(source);
        }

        if (createdTemp)
            Object.Destroy(source);

        if (applyColorOverride)
        {
            if (coverInstance.HasProperty("_BaseColor"))
                coverInstance.SetColor("_BaseColor", coverColor);
            if (coverInstance.HasProperty("_Color"))
                coverInstance.SetColor("_Color", coverColor);
        }

        renderer.sharedMaterial = coverInstance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return coverInstance;
    }

    public static void DestroyMaterial(ref Material coverInstance)
    {
        if (coverInstance == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(coverInstance);
        else
            Object.DestroyImmediate(coverInstance);

        coverInstance = null;
    }

    /// <summary>True when the room should be visible (no fog lid).</summary>
    public static bool IsRoomFogRevealed(RoomDefinition room)
    {
        if (room == null)
            return true;

        RoomEncounter encounter = room.Encounter != null
            ? room.Encounter
            : room.GetComponent<RoomEncounter>();

        if (encounter == null || !encounter.enabled)
            return true;

        return encounter.IsCleared || encounter.IsInProgress;
    }
}
