using UnityEngine;

/// <summary>
/// Red crosshair telegraph for Boot Loop reinforcement spawn points.
/// </summary>
public class BootLoopSpawnMarker : MonoBehaviour
{
    [SerializeField] Color markerColor = new Color(1f, 0.15f, 0.12f, 0.7f);
    [SerializeField] float pulseSpeed = 6f;
    [SerializeField] float ringRadius = 1.1f;

    Material ringMaterial;
    Material crossMaterial;
    Color baseRingColor;
    Color baseCrossColor;
    float phase;

    public static BootLoopSpawnMarker Create(Vector3 worldPosition, float radius)
    {
        var root = new GameObject("Boot Loop Spawn Marker");
        root.transform.position = worldPosition;
        BootLoopSpawnMarker marker = root.AddComponent<BootLoopSpawnMarker>();
        marker.ringRadius = Mathf.Clamp(radius, 0.7f, 2.2f);
        marker.BuildVisuals();
        return marker;
    }

    void OnDestroy()
    {
        if (ringMaterial != null)
            Destroy(ringMaterial);
        if (crossMaterial != null)
            Destroy(crossMaterial);
    }

    void LateUpdate()
    {
        float pulse = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed + phase));
        if (ringMaterial != null)
            ApplyColor(ringMaterial, baseRingColor, pulse);
        if (crossMaterial != null)
            ApplyColor(crossMaterial, baseCrossColor, 0.55f + pulse * 0.45f);

        float scalePulse = 1f + 0.08f * Mathf.Sin(Time.time * pulseSpeed + phase);
        transform.localScale = Vector3.one * scalePulse;
    }

    void BuildVisuals()
    {
        phase = Random.Range(0f, Mathf.PI * 2f);
        baseRingColor = markerColor;
        baseCrossColor = new Color(markerColor.r, markerColor.g, markerColor.b, Mathf.Min(1f, markerColor.a + 0.2f));
        ringMaterial = CreateTransparentMaterial(baseRingColor, "BootLoopRing");
        crossMaterial = CreateTransparentMaterial(baseCrossColor, "BootLoopCross");

        var ring = new GameObject("Ring");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
        MeshRenderer ringRenderer = ring.AddComponent<MeshRenderer>();
        ringFilter.sharedMesh = BuildRingMesh(ringRadius, ringRadius * 0.72f, 28);
        ringRenderer.sharedMaterial = ringMaterial;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;

        var cross = new GameObject("Crosshair");
        cross.transform.SetParent(transform, false);
        cross.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        cross.transform.localScale = Vector3.one * (ringRadius * 1.15f);
        MeshFilter crossFilter = cross.AddComponent<MeshFilter>();
        MeshRenderer crossRenderer = cross.AddComponent<MeshRenderer>();
        crossFilter.sharedMesh = BuildCrosshairMesh();
        crossRenderer.sharedMaterial = crossMaterial;
        crossRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        crossRenderer.receiveShadows = false;
    }

    static void ApplyColor(Material material, Color baseColor, float alphaScale)
    {
        Color color = baseColor;
        color.a = Mathf.Clamp01(baseColor.a * alphaScale);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * 1.6f);
    }

    static Material CreateTransparentMaterial(Color color, string name)
    {
        return RuntimeEffectMaterials.CreateTransparent(color, emissive: true, materialName: name);
    }

    static Mesh BuildRingMesh(float outerRadius, float innerRadius, int segments)
    {
        segments = Mathf.Max(8, segments);
        innerRadius = Mathf.Clamp(innerRadius, 0.05f, outerRadius - 0.05f);

        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            vertices[i * 2] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
            vertices[i * 2 + 1] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
        }

        int tri = 0;
        for (int i = 0; i < segments; i++)
        {
            int i0 = i * 2;
            int i1 = i0 + 1;
            int i2 = i0 + 2;
            int i3 = i0 + 3;

            triangles[tri++] = i0;
            triangles[tri++] = i2;
            triangles[tri++] = i1;
            triangles[tri++] = i1;
            triangles[tri++] = i2;
            triangles[tri++] = i3;
        }

        var mesh = new Mesh
        {
            name = "BootLoopRing",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildCrosshairMesh()
    {
        // Flat + with open center, reads as a spawn crosshair on the floor.
        const float arm = 0.12f;
        const float half = 0.55f;
        const float gap = 0.14f;

        var vertices = new[]
        {
            // Vertical top
            new Vector3(-arm, 0f, gap),
            new Vector3(arm, 0f, gap),
            new Vector3(arm, 0f, half),
            new Vector3(-arm, 0f, half),
            // Vertical bottom
            new Vector3(-arm, 0f, -half),
            new Vector3(arm, 0f, -half),
            new Vector3(arm, 0f, -gap),
            new Vector3(-arm, 0f, -gap),
            // Horizontal right
            new Vector3(gap, 0f, -arm),
            new Vector3(half, 0f, -arm),
            new Vector3(half, 0f, arm),
            new Vector3(gap, 0f, arm),
            // Horizontal left
            new Vector3(-half, 0f, -arm),
            new Vector3(-gap, 0f, -arm),
            new Vector3(-gap, 0f, arm),
            new Vector3(-half, 0f, arm)
        };

        var triangles = new[]
        {
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11,
            12, 13, 14, 12, 14, 15
        };

        var mesh = new Mesh
        {
            name = "BootLoopCrosshair",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
