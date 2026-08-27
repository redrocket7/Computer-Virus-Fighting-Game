using System.Collections;
using UnityEngine;

/// <summary>
/// Short-lived digital burst shown when a non-exploding enemy is destroyed.
/// </summary>
[DisallowMultipleComponent]
public class DeathEffect : MonoBehaviour
{
    [SerializeField, Min(0.1f)] float duration = 0.55f;
    [SerializeField, Min(0.1f)] float maxBurstRadius = 1.75f;
    [SerializeField] Color brightColor = new Color(0.35f, 0.95f, 1f, 0.75f);
    [SerializeField] Color deepColor = new Color(0.75f, 0.2f, 1f, 0.45f);

    Transform burstRoot;
    MeshRenderer burstRenderer;
    Material burstMaterial;
    ParticleSystem shards;
    Coroutine playRoutine;
    bool visualsBuilt;

    public float Duration => duration;

    void Awake()
    {
        BuildVisualsIfNeeded();
    }

    void OnEnable()
    {
        BuildVisualsIfNeeded();
        ResetVisuals();
        playRoutine = StartCoroutine(PlayRoutine());
    }

    void OnDisable()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        ResetVisuals();
    }

    IEnumerator PlayRoutine()
    {
        if (shards != null)
            shards.Play(withChildren: true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ApplyFrame(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        ApplyFrame(1f);
    }

    void ApplyFrame(float t)
    {
        if (burstRoot == null)
            return;

        float scale = Mathf.Lerp(0.2f, maxBurstRadius, EaseOut(t));
        burstRoot.localScale = Vector3.one * scale;

        Color tint = Color.Lerp(deepColor, brightColor, 1f - t * 0.7f);
        tint.a = Mathf.Lerp(brightColor.a, 0f, SmoothStep(0.25f, 1f, t));
        burstMaterial.SetColor("_BaseColor", tint);
        burstMaterial.SetColor("_Color", tint);
        burstMaterial.SetColor("_EmissionColor", tint * Mathf.Lerp(2f, 0f, t));
    }

    void ResetVisuals()
    {
        if (burstRoot != null)
            burstRoot.localScale = Vector3.one * 0.01f;
    }

    void BuildVisualsIfNeeded()
    {
        if (visualsBuilt)
            return;

        visualsBuilt = true;

        burstRoot = CreateChild("Burst", Vector3.up * 0.35f);
        MeshFilter filter = burstRoot.gameObject.AddComponent<MeshFilter>();
        burstRenderer = burstRoot.gameObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = LowPolyIcosphere.Create(1);

        burstMaterial = CreateTransparentMaterial(brightColor, emissive: true);
        burstRenderer.sharedMaterial = burstMaterial;
        burstRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        burstRenderer.receiveShadows = false;

        shards = CreateShards(transform);
        ResetVisuals();
    }

    Transform CreateChild(string childName, Vector3 localPosition)
    {
        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        return child.transform;
    }

    static Material CreateTransparentMaterial(Color color, bool emissive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = emissive ? "DeathBurstRuntime" : "DeathShardRuntime",
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

    static ParticleSystem CreateShards(Transform parent)
    {
        var shardObject = new GameObject("Shards");
        shardObject.transform.SetParent(parent, false);
        shardObject.transform.localPosition = Vector3.up * 0.25f;

        ParticleSystem ps = shardObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.45f, 0.95f, 1f, 1f),
            new Color(0.85f, 0.35f, 1f, 1f));
        main.gravityModifier = 0.15f;
        main.maxParticles = 18;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 10, 16)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        var renderer = shardObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = CreateTransparentMaterial(new Color(0.55f, 0.9f, 1f, 1f), emissive: true);

        return ps;
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    public static float GetPoolLifetime(GameObject instance)
    {
        if (instance != null && instance.TryGetComponent(out DeathEffect effect))
            return Mathf.Max(0.35f, effect.Duration + 0.15f);

        ParticleSystem ps = instance != null ? instance.GetComponentInChildren<ParticleSystem>() : null;
        if (ps != null)
        {
            var main = ps.main;
            return main.duration + main.startLifetime.constantMax + 0.15f;
        }

        return 1.5f;
    }
}
