using System.Collections;
using UnityEngine;

/// <summary>
/// Short digital glitch burst shown when a player shot fizzles in a Packet Loss room.
/// Built for a top-down camera: chromatic flashes, jittering scan bars, and pixel shards.
/// </summary>
[DisallowMultipleComponent]
public class PacketLossFizzleEffect : MonoBehaviour
{
    [SerializeField, Min(0.05f)] float duration = 0.4f;
    [SerializeField] Color cyanGlitch = new Color(0.2f, 0.95f, 1f, 1f);
    [SerializeField] Color magentaGlitch = new Color(1f, 0.15f, 0.85f, 1f);
    [SerializeField] Color yellowGlitch = new Color(1f, 0.95f, 0.25f, 1f);
    [SerializeField] Color whiteGlitch = new Color(1f, 1f, 1f, 1f);
    [SerializeField] float flashSize = 1.1f;
    [SerializeField] float barWidth = 1.6f;
    [SerializeField] float chromaticOffset = 0.28f;

    Transform cyanFlash;
    Transform magentaFlash;
    Transform whiteFlash;
    Material cyanFlashMaterial;
    Material magentaFlashMaterial;
    Material whiteFlashMaterial;

    Transform[] glitchBars;
    Material[] glitchMaterials;
    Vector3[] barBasePositions;

    ParticleSystem pixels;
    Coroutine playRoutine;
    bool visualsBuilt;
    Camera worldCamera;

    public float Duration => duration;

    public static void Play(Vector3 position, Vector3 direction)
    {
        var effectObject = new GameObject("Packet Loss Fizzle");
        effectObject.transform.position = position + Vector3.up * 0.35f;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude > 0.0001f)
            effectObject.transform.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);

        var effect = effectObject.AddComponent<PacketLossFizzleEffect>();
        Destroy(effectObject, effect.Duration + 0.15f);
    }

    void Awake()
    {
        worldCamera = Camera.main;
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

    void LateUpdate()
    {
        BillboardBarsToCamera();
    }

    IEnumerator PlayRoutine()
    {
        if (pixels != null)
            pixels.Play(withChildren: true);

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
        float punch = 1f - EaseOut(t);
        float offset = chromaticOffset * Mathf.Lerp(1f, 0.15f, t);

        if (cyanFlash != null)
        {
            cyanFlash.localPosition = new Vector3(-offset, 0f, 0f);
            cyanFlash.localScale = Vector3.one * (flashSize * Mathf.Lerp(1f, 0.15f, EaseOut(t)) * (0.85f + punch * 0.35f));
            SetMaterialColor(cyanFlashMaterial, WithAlpha(cyanGlitch, Mathf.Lerp(1f, 0f, SmoothStep(0.25f, 1f, t))));
        }

        if (magentaFlash != null)
        {
            magentaFlash.localPosition = new Vector3(offset, 0f, 0f);
            magentaFlash.localScale = Vector3.one * (flashSize * 0.92f * Mathf.Lerp(1f, 0.12f, EaseOut(t)));
            SetMaterialColor(magentaFlashMaterial, WithAlpha(magentaGlitch, Mathf.Lerp(0.95f, 0f, SmoothStep(0.2f, 1f, t))));
        }

        if (whiteFlash != null)
        {
            whiteFlash.localPosition = Vector3.zero;
            whiteFlash.localScale = Vector3.one * (flashSize * 0.55f * Mathf.Lerp(1.15f, 0.08f, EaseOut(t)));
            SetMaterialColor(whiteFlashMaterial, WithAlpha(whiteGlitch, Mathf.Lerp(1f, 0f, SmoothStep(0.05f, 0.75f, t))));
        }

        if (glitchBars == null || glitchMaterials == null)
            return;

        float jitter = Mathf.Lerp(0.45f, 0.04f, t);
        for (int i = 0; i < glitchBars.Length; i++)
        {
            Transform bar = glitchBars[i];
            Material material = glitchMaterials[i];
            if (bar == null || material == null)
                continue;

            Vector3 basePosition = barBasePositions[i];
            float lateral = Random.Range(-jitter, jitter);
            float vertical = Random.Range(-jitter * 0.35f, jitter * 0.35f);
            bar.localPosition = basePosition + new Vector3(lateral, vertical, Random.Range(-jitter * 0.25f, jitter * 0.25f));

            float width = barWidth * Mathf.Lerp(1.15f, 0.25f, t) * Random.Range(0.75f, 1.2f);
            float thickness = Mathf.Lerp(0.12f, 0.03f, t);
            bar.localScale = new Vector3(width, thickness, thickness * 0.35f);

            Color barColor = i switch
            {
                0 => cyanGlitch,
                1 => whiteGlitch,
                2 => magentaGlitch,
                3 => yellowGlitch,
                _ => cyanGlitch
            };
            SetMaterialColor(material, WithAlpha(barColor, Mathf.Lerp(1f, 0f, SmoothStep(0.1f, 1f, t))));
        }
    }

    void BillboardBarsToCamera()
    {
        if (glitchBars == null)
            return;

        if (worldCamera == null)
            worldCamera = Camera.main;
        if (worldCamera == null)
            return;

        Quaternion look = Quaternion.LookRotation(worldCamera.transform.forward, worldCamera.transform.up);
        for (int i = 0; i < glitchBars.Length; i++)
        {
            if (glitchBars[i] != null)
                glitchBars[i].rotation = look;
        }
    }

    void ResetVisuals()
    {
        if (cyanFlash != null)
            cyanFlash.localScale = Vector3.one * 0.01f;
        if (magentaFlash != null)
            magentaFlash.localScale = Vector3.one * 0.01f;
        if (whiteFlash != null)
            whiteFlash.localScale = Vector3.one * 0.01f;
    }

    void BuildVisualsIfNeeded()
    {
        if (visualsBuilt)
            return;

        visualsBuilt = true;

        cyanFlash = CreateSphere("CyanFlash", cyanGlitch, out cyanFlashMaterial);
        magentaFlash = CreateSphere("MagentaFlash", magentaGlitch, out magentaFlashMaterial);
        whiteFlash = CreateSphere("WhiteFlash", whiteGlitch, out whiteFlashMaterial);

        int barCount = 5;
        glitchBars = new Transform[barCount];
        glitchMaterials = new Material[barCount];
        barBasePositions = new Vector3[barCount];
        Color[] barColors = { cyanGlitch, whiteGlitch, magentaGlitch, yellowGlitch, cyanGlitch };

        for (int i = 0; i < barCount; i++)
        {
            float y = (i - (barCount - 1) * 0.5f) * 0.18f;
            barBasePositions[i] = new Vector3(0f, y, 0f);
            Transform bar = CreateChild($"GlitchBar{i}", barBasePositions[i]);
            MeshFilter barFilter = bar.gameObject.AddComponent<MeshFilter>();
            MeshRenderer barRenderer = bar.gameObject.AddComponent<MeshRenderer>();
            barFilter.sharedMesh = CreateQuadMesh();
            glitchMaterials[i] = CreateTransparentMaterial(barColors[i % barColors.Length], emissive: true);
            barRenderer.sharedMaterial = glitchMaterials[i];
            barRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            barRenderer.receiveShadows = false;
            glitchBars[i] = bar;
        }

        pixels = CreatePixelBurst(transform);
        ResetVisuals();
    }

    Transform CreateSphere(string childName, Color color, out Material material)
    {
        Transform root = CreateChild(childName, Vector3.zero);
        MeshFilter filter = root.gameObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = root.gameObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = LowPolyIcosphere.Create(1);
        material = CreateTransparentMaterial(color, emissive: true);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return root;
    }

    Transform CreateChild(string childName, Vector3 localPosition)
    {
        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        return child.transform;
    }

    static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh { name = "GlitchQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static ParticleSystem CreatePixelBurst(Transform parent)
    {
        var pixelObject = new GameObject("PixelBurst");
        pixelObject.transform.SetParent(parent, false);
        pixelObject.transform.localPosition = Vector3.zero;

        ParticleSystem ps = pixelObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.28f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.25f, 0.95f, 1f, 1f),
            new Color(1f, 0.2f, 0.9f, 1f));
        main.gravityModifier = 0f;
        main.maxParticles = 36;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 18, 28)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.4f, 0.9f, 1f), 0.45f),
                new GradientColorKey(new Color(1f, 0.2f, 0.85f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.85f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        var renderer = pixelObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = CreateTransparentMaterial(new Color(0.55f, 0.95f, 1f, 1f), emissive: true);

        return ps;
    }

    static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * Mathf.Lerp(0.4f, 3.5f, color.a));
    }

    static Material CreateTransparentMaterial(Color color, bool emissive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = emissive ? "PacketLossGlitchRuntime" : "PacketLossPixelRuntime",
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
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
        }

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (emissive && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.5f);
        }

        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        return material;
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
