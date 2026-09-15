using UnityEngine;

/// <summary>
/// Soft amber priority cue for support enemies (Repair / Overclock).
/// Floor ring + floating chevron so they read as "kill first" targets.
/// </summary>
[DisallowMultipleComponent]
public class SupportPriorityMarker : MonoBehaviour
{
    [SerializeField] Color markerColor = new Color(1f, 0.78f, 0.15f, 0.55f);
    [SerializeField] float ringRadius = 1.6f;
    [SerializeField] float chevronHeight = 3.2f;
    [SerializeField] float pulseSpeed = 2.4f;
    [SerializeField] float bobAmplitude = 0.18f;

    Transform chevronRoot;
    Material ringMaterial;
    Material chevronMaterial;
    Color baseRingColor;
    Color baseChevronColor;
    float bobPhase;

    public static SupportPriorityMarker EnsureOn(EnemyAI owner)
    {
        if (owner == null)
            return null;

        SupportPriorityMarker existing = owner.GetComponent<SupportPriorityMarker>();
        if (existing != null)
            return existing;

        SupportPriorityMarker marker = owner.gameObject.AddComponent<SupportPriorityMarker>();
        float radius = 1.6f;
        if (owner.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent) && agent.radius > 0.1f)
            radius = Mathf.Clamp(agent.radius * 2.9f, 1.3f, 3.6f);

        marker.ringRadius = radius;
        marker.BuildVisuals();
        return marker;
    }

    void OnDestroy()
    {
        if (ringMaterial != null)
            Destroy(ringMaterial);
        if (chevronMaterial != null)
            Destroy(chevronMaterial);
    }

    void LateUpdate()
    {
        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed));
        if (ringMaterial != null)
            ApplyColor(ringMaterial, baseRingColor, pulse * 0.85f);
        if (chevronMaterial != null)
            ApplyColor(chevronMaterial, baseChevronColor, pulse);

        if (chevronRoot == null)
            return;

        float bob = Mathf.Sin(Time.time * pulseSpeed + bobPhase) * bobAmplitude;
        chevronRoot.localPosition = new Vector3(0f, chevronHeight + bob, 0f);

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 toCam = cam.transform.position - chevronRoot.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            chevronRoot.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    void BuildVisuals()
    {
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        baseRingColor = markerColor;
        baseChevronColor = new Color(markerColor.r, markerColor.g, markerColor.b, Mathf.Min(1f, markerColor.a + 0.25f));

        ringMaterial = CreateTransparentMaterial(baseRingColor, "SupportPriorityRing");
        chevronMaterial = CreateTransparentMaterial(baseChevronColor, "SupportPriorityChevron");

        var ring = new GameObject("Priority Ring");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
        MeshRenderer ringRenderer = ring.AddComponent<MeshRenderer>();
        ringFilter.sharedMesh = BuildRingMesh(ringRadius, ringRadius * 0.72f, 28);
        ringRenderer.sharedMaterial = ringMaterial;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;

        chevronRoot = new GameObject("Priority Chevron").transform;
        chevronRoot.SetParent(transform, false);
        chevronRoot.localPosition = new Vector3(0f, chevronHeight, 0f);

        var chevron = new GameObject("Chevron");
        chevron.transform.SetParent(chevronRoot, false);
        chevron.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
        MeshFilter chevronFilter = chevron.AddComponent<MeshFilter>();
        MeshRenderer chevronRenderer = chevron.AddComponent<MeshRenderer>();
        chevronFilter.sharedMesh = BuildChevronMesh();
        chevronRenderer.sharedMaterial = chevronMaterial;
        chevronRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        chevronRenderer.receiveShadows = false;
    }

    static void ApplyColor(Material material, Color baseColor, float alphaScale)
    {
        Color color = baseColor;
        color.a = Mathf.Clamp01(baseColor.a * alphaScale);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
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
            name = "SupportPriorityRing",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildChevronMesh()
    {
        // Hollow-ish downward chevron (wide bar + tip).
        var vertices = new[]
        {
            new Vector3(-0.55f, 0.45f, 0f),
            new Vector3(0.55f, 0.45f, 0f),
            new Vector3(0.22f, 0.12f, 0f),
            new Vector3(-0.22f, 0.12f, 0f),
            new Vector3(-0.38f, 0.12f, 0f),
            new Vector3(0.38f, 0.12f, 0f),
            new Vector3(0f, -0.5f, 0f)
        };

        var triangles = new[]
        {
            0, 1, 2,
            0, 2, 3,
            4, 5, 6
        };

        var mesh = new Mesh
        {
            name = "SupportPriorityChevron",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
