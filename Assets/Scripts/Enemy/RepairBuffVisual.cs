using UnityEngine;

/// <summary>
/// Green aura on an ally while a Repair enemy is actively healing it.
/// Refreshed each heal tick; fades out shortly after healing stops.
/// </summary>
[DisallowMultipleComponent]
public class RepairBuffVisual : MonoBehaviour
{
    [SerializeField] Color auraColor = new Color(0.25f, 0.95f, 0.45f, 0.55f);
    [SerializeField] float pulseSpeed = 3.4f;
    [SerializeField] float bobAmplitude = 0.1f;
    [SerializeField] float lingerDuration = 0.45f;

    Material ringMaterial;
    Material pingMaterial;
    Transform pingRoot;
    Color baseRingColor;
    Color basePingColor;
    float pingBaseHeight;
    float bobPhase;
    float remaining;

    public static void Refresh(EnemyAI owner)
    {
        if (owner == null || !owner.IsAlive)
            return;

        RepairBuffVisual visual = owner.GetComponentInChildren<RepairBuffVisual>();
        if (visual == null)
        {
            var root = new GameObject("Repair Buff Aura");
            root.transform.SetParent(owner.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            visual = root.AddComponent<RepairBuffVisual>();
            visual.BuildVisuals(owner);
        }

        visual.remaining = Mathf.Max(0.15f, visual.lingerDuration);
    }

    void OnDestroy()
    {
        if (ringMaterial != null)
            Destroy(ringMaterial);
        if (pingMaterial != null)
            Destroy(pingMaterial);
    }

    void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0f)
            Destroy(gameObject);
    }

    void LateUpdate()
    {
        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed));
        if (ringMaterial != null)
            ApplyColor(ringMaterial, baseRingColor, pulse);
        if (pingMaterial != null)
            ApplyColor(pingMaterial, basePingColor, 0.5f + pulse * 0.5f);

        if (pingRoot == null)
            return;

        float bob = Mathf.Sin(Time.time * pulseSpeed + bobPhase) * bobAmplitude;
        pingRoot.localPosition = new Vector3(0f, pingBaseHeight + bob, 0f);

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 toCam = cam.transform.position - pingRoot.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            pingRoot.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    void BuildVisuals(EnemyAI owner)
    {
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        GetBodyFit(owner, out float radius, out float height);

        baseRingColor = auraColor;
        basePingColor = new Color(auraColor.r, auraColor.g, auraColor.b, Mathf.Min(1f, auraColor.a + 0.25f));
        ringMaterial = CreateTransparentMaterial(baseRingColor, "RepairBuffRing");
        pingMaterial = CreateTransparentMaterial(basePingColor, "RepairBuffPing");

        var ring = new GameObject("Aura Ring");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
        MeshRenderer ringRenderer = ring.AddComponent<MeshRenderer>();
        ringFilter.sharedMesh = BuildRingMesh(radius, radius * 0.78f, 28);
        ringRenderer.sharedMaterial = ringMaterial;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;

        pingBaseHeight = Mathf.Max(2.2f, height + 0.85f);
        pingRoot = new GameObject("Aura Ping").transform;
        pingRoot.SetParent(transform, false);
        pingRoot.localPosition = new Vector3(0f, pingBaseHeight, 0f);

        var ping = new GameObject("Ping");
        ping.transform.SetParent(pingRoot, false);
        ping.transform.localScale = Vector3.one * Mathf.Clamp(radius * 0.5f, 0.65f, 1.25f);
        MeshFilter pingFilter = ping.AddComponent<MeshFilter>();
        MeshRenderer pingRenderer = ping.AddComponent<MeshRenderer>();
        pingFilter.sharedMesh = BuildPlusMesh();
        pingRenderer.sharedMaterial = pingMaterial;
        pingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pingRenderer.receiveShadows = false;
    }

    static void GetBodyFit(EnemyAI owner, out float radius, out float height)
    {
        radius = 1.4f;
        height = 2f;

        Renderer[] renderers = owner.GetComponentsInChildren<Renderer>();
        bool hasBounds = false;
        Bounds bounds = new Bounds(owner.transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (renderer.GetComponentInParent<OverclockBuffVisual>() != null ||
                renderer.GetComponentInParent<RepairBuffVisual>() != null ||
                renderer.GetComponentInParent<SupportPriorityMarker>() != null)
                continue;
            if (renderer is ParticleSystemRenderer)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        if (hasBounds)
        {
            float xz = Mathf.Max(bounds.extents.x, bounds.extents.z);
            radius = Mathf.Clamp(xz * 1.2f, 1f, 4f);
            height = Mathf.Max(1.2f, bounds.size.y);
            return;
        }

        if (owner.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent) && agent.radius > 0.1f)
        {
            radius = Mathf.Clamp(agent.radius * 1.35f, 1f, 4f);
            height = Mathf.Max(1.2f, agent.height);
        }
    }

    static void ApplyColor(Material material, Color baseColor, float alphaScale)
    {
        Color color = baseColor;
        color.a = Mathf.Clamp01(baseColor.a * alphaScale);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * 1.35f);
    }

    static Material CreateTransparentMaterial(Color color, string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = name,
            renderQueue = 3000
        };

        if (shader.name.Contains("Universal Render Pipeline"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * 1.35f);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        return material;
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
            name = "RepairBuffRing",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildPlusMesh()
    {
        // Medical-style plus / cross.
        const float arm = 0.18f;
        const float half = 0.5f;
        var vertices = new[]
        {
            new Vector3(-arm, half, 0f),
            new Vector3(arm, half, 0f),
            new Vector3(arm, arm, 0f),
            new Vector3(half, arm, 0f),
            new Vector3(half, -arm, 0f),
            new Vector3(arm, -arm, 0f),
            new Vector3(arm, -half, 0f),
            new Vector3(-arm, -half, 0f),
            new Vector3(-arm, -arm, 0f),
            new Vector3(-half, -arm, 0f),
            new Vector3(-half, arm, 0f),
            new Vector3(-arm, arm, 0f)
        };

        var triangles = new[]
        {
            0, 1, 2,
            0, 2, 11,
            11, 2, 5,
            11, 5, 8,
            8, 5, 6,
            8, 6, 7,
            10, 11, 8,
            10, 8, 9,
            2, 3, 4,
            2, 4, 5
        };

        var mesh = new Mesh
        {
            name = "RepairBuffPlus",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
