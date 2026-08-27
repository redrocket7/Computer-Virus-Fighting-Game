using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stylized low-poly explosion: faceted emissive orb, ground scorch ring, and blocky sparks.
/// Builds its own meshes/materials on first use so the prefab is a single root object.
/// </summary>
[DisallowMultipleComponent]
public class ExplosionEffect : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float duration = 0.85f;
    [SerializeField, Min(0.1f)] private float maxOrbRadius = 3.5f;
    [SerializeField, Min(0.1f)] private float maxRingRadius = 4.5f;
    [SerializeField] private Color brightColor = new Color(1f, 0.92f, 0.35f, 0.82f);
    [SerializeField] private Color deepColor = new Color(1f, 0.42f, 0.05f, 0.55f);
    [SerializeField] private Color ringColor = new Color(0.22f, 0.22f, 0.22f, 0.55f);

    private Transform orbRoot;
    private Transform ringRoot;
    private MeshRenderer orbRenderer;
    private MeshRenderer ringRenderer;
    private ParticleSystem sparks;
    private Material orbMaterial;
    private Material ringMaterial;
    private Coroutine playRoutine;
    private bool visualsBuilt;

    public float Duration => duration;

    private void Awake()
    {
        BuildVisualsIfNeeded();
    }

    private void OnEnable()
    {
        BuildVisualsIfNeeded();
        ResetVisuals();
        playRoutine = StartCoroutine(PlayRoutine());
    }

    private void OnDisable()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        ResetVisuals();
    }

    private IEnumerator PlayRoutine()
    {
        if (sparks != null)
            sparks.Play(withChildren: true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            ApplyFrame(t);
            yield return null;
        }

        ApplyFrame(1f);
    }

    private void ApplyFrame(float t)
    {
        if (orbRoot == null || ringRoot == null) return;

        float orbScale = Mathf.Lerp(0.25f, maxOrbRadius, EaseOut(t));
        orbRoot.localScale = Vector3.one * orbScale;

        float orbAlpha = Mathf.Lerp(brightColor.a, 0f, SmoothStep(0.35f, 1f, t));
        Color orbTint = Color.Lerp(deepColor, brightColor, 1f - t * 0.65f);
        orbTint.a = orbAlpha;
        if (orbMaterial != null)
        {
            orbMaterial.SetColor("_BaseColor", orbTint);
            orbMaterial.SetColor("_EmissionColor", orbTint * (2.2f * (1f - t * 0.7f)));
        }

        float ringScale = Mathf.Lerp(0.35f, maxRingRadius, EaseOut(t));
        ringRoot.localScale = new Vector3(ringScale, 1f, ringScale);

        Color ringTint = ringColor;
        ringTint.a = Mathf.Lerp(ringColor.a, 0f, SmoothStep(0.25f, 1f, t));
        if (ringMaterial != null)
            ringMaterial.SetColor("_BaseColor", ringTint);
    }

    private void ResetVisuals()
    {
        if (orbRoot != null)
            orbRoot.localScale = Vector3.one * 0.01f;
        if (ringRoot != null)
            ringRoot.localScale = new Vector3(0.01f, 1f, 0.01f);
    }

    private void BuildVisualsIfNeeded()
    {
        if (visualsBuilt) return;
        visualsBuilt = true;

        orbRoot = CreateChild("Orb", Vector3.up * (maxOrbRadius * 0.35f));
        ringRoot = CreateChild("GroundRing", Vector3.up * 0.05f);

        MeshFilter orbFilter = orbRoot.gameObject.AddComponent<MeshFilter>();
        orbRenderer = orbRoot.gameObject.AddComponent<MeshRenderer>();
        orbFilter.sharedMesh = LowPolyIcosphere.Create(1);

        MeshFilter ringFilter = ringRoot.gameObject.AddComponent<MeshFilter>();
        ringRenderer = ringRoot.gameObject.AddComponent<MeshRenderer>();
        ringFilter.sharedMesh = LowPolyIcosphere.CreateGroundRing();

        orbMaterial = CreateTransparentMaterial(brightColor, emissive: true);
        ringMaterial = CreateTransparentMaterial(ringColor, emissive: false);
        orbRenderer.sharedMaterial = orbMaterial;
        ringRenderer.sharedMaterial = ringMaterial;
        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orbRenderer.receiveShadows = false;
        ringRenderer.receiveShadows = false;

        sparks = CreateSparks(transform);
        ResetVisuals();
    }

    private Transform CreateChild(string childName, Vector3 localPosition)
    {
        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        return child.transform;
    }

    private static Material CreateTransparentMaterial(Color color, bool emissive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = emissive ? "ExplosionOrbRuntime" : "ExplosionRingRuntime",
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
        if (emissive)
            material.SetColor("_EmissionColor", color * 2f);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        return material;
    }

    private static ParticleSystem CreateSparks(Transform parent)
    {
        var sparksObject = new GameObject("Sparks");
        sparksObject.transform.SetParent(parent, false);
        sparksObject.transform.localPosition = Vector3.up * 0.2f;

        ParticleSystem ps = sparksObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.75f, 0.2f, 1f),
            new Color(1f, 0.4f, 0.05f, 1f));
        main.gravityModifier = 0.35f;
        main.maxParticles = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 14, 20)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;

        var renderer = sparksObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = CreateTransparentMaterial(new Color(1f, 0.6f, 0.1f, 1f), emissive: true);

        return ps;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Recommended pool lifetime when using ObjectPool.ReleaseDelayed.</summary>
    public static float GetPoolLifetime(GameObject instance)
    {
        if (instance != null && instance.TryGetComponent(out ExplosionEffect effect))
            return Mathf.Max(0.5f, effect.Duration + 0.15f);

        ParticleSystem ps = instance != null ? instance.GetComponentInChildren<ParticleSystem>() : null;
        if (ps != null)
        {
            var main = ps.main;
            return main.duration + main.startLifetime.constantMax + 0.15f;
        }

        return 2f;
    }
}

/// <summary>Shared low-poly meshes for stylized explosions.</summary>
internal static class LowPolyIcosphere
{
    private static Mesh icosphere;
    private static Mesh groundRing;

    public static Mesh Create(int subdivisions)
    {
        if (icosphere != null) return icosphere;

        var vertices = new List<Vector3>
        {
            new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
            new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f)
        };

        var triangles = new List<int>
        {
            0,4,2, 2,4,1, 1,4,3, 3,4,0,
            0,2,5, 2,1,5, 1,3,5, 3,0,5
        };

        for (int i = 0; i < subdivisions; i++)
            Subdivide(vertices, triangles);

        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = vertices[i].normalized;

        icosphere = new Mesh { name = "LowPolyIcosphere" };
        icosphere.SetVertices(vertices);
        icosphere.SetTriangles(triangles, 0);
        icosphere.RecalculateNormals();
        icosphere.RecalculateBounds();
        return icosphere;
    }

    public static Mesh CreateGroundRing()
    {
        if (groundRing != null) return groundRing;

        const int segments = 24;
        var vertices = new Vector3[segments + 1];
        var triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = i == segments - 1 ? 1 : i + 2;
        }

        groundRing = new Mesh { name = "ExplosionGroundRing" };
        groundRing.SetVertices(vertices);
        groundRing.SetTriangles(triangles, 0);
        groundRing.RecalculateNormals();
        groundRing.RecalculateBounds();
        return groundRing;
    }

    private static void Subdivide(List<Vector3> vertices, List<int> triangles)
    {
        var newTriangles = new List<int>(triangles.Count * 4);
        var midpointCache = new Dictionary<long, int>();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];

            int ab = GetMidpoint(a, b, vertices, midpointCache);
            int bc = GetMidpoint(b, c, vertices, midpointCache);
            int ca = GetMidpoint(c, a, vertices, midpointCache);

            newTriangles.AddRange(new[] { a, ab, ca });
            newTriangles.AddRange(new[] { b, bc, ab });
            newTriangles.AddRange(new[] { c, ca, bc });
            newTriangles.AddRange(new[] { ab, bc, ca });
        }

        triangles.Clear();
        triangles.AddRange(newTriangles);
    }

    private static int GetMidpoint(int indexA, int indexB, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = indexA < indexB
            ? ((long)indexA << 32) + indexB
            : ((long)indexB << 32) + indexA;

        if (cache.TryGetValue(key, out int index))
            return index;

        Vector3 midpoint = (vertices[indexA] + vertices[indexB]) * 0.5f;
        index = vertices.Count;
        vertices.Add(midpoint);
        cache[key] = index;
        return index;
    }
}
