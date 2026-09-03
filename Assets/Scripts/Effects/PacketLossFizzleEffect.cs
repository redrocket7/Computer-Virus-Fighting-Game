using System.Collections;
using UnityEngine;

/// <summary>
/// Short digital glitch burst shown when a player shot fizzles in a Packet Loss room.
/// </summary>
[DisallowMultipleComponent]
public class PacketLossFizzleEffect : MonoBehaviour
{
    [SerializeField, Min(0.05f)] float duration = 0.28f;
    [SerializeField] Color cyanGlitch = new Color(0.25f, 0.95f, 1f, 0.85f);
    [SerializeField] Color magentaGlitch = new Color(0.85f, 0.2f, 1f, 0.75f);
    [SerializeField] Color whiteGlitch = new Color(1f, 1f, 1f, 0.9f);

    Transform flashRoot;
    Material flashMaterial;
    Transform[] glitchBars;
    Material[] glitchMaterials;
    ParticleSystem pixels;
    Coroutine playRoutine;
    bool visualsBuilt;

    public float Duration => duration;

    public static void Play(Vector3 position, Vector3 direction)
    {
        var effectObject = new GameObject("Packet Loss Fizzle");
        effectObject.transform.position = position;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude > 0.0001f)
            effectObject.transform.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);

        var effect = effectObject.AddComponent<PacketLossFizzleEffect>();
        Destroy(effectObject, effect.Duration + 0.12f);
    }

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
        if (flashRoot != null)
        {
            float flashScale = Mathf.Lerp(0.35f, 0.05f, EaseOut(t));
            flashRoot.localScale = new Vector3(flashScale, flashScale * 0.35f, flashScale);

            Color flashColor = Color.Lerp(whiteGlitch, cyanGlitch, t * 0.65f);
            flashColor.a = Mathf.Lerp(whiteGlitch.a, 0f, SmoothStep(0.2f, 1f, t));
            SetMaterialColor(flashMaterial, flashColor);
        }

        if (glitchBars != null && glitchMaterials != null)
        {
            float jitter = Mathf.Lerp(0.22f, 0.02f, t);
            for (int i = 0; i < glitchBars.Length; i++)
            {
                Transform bar = glitchBars[i];
                Material material = glitchMaterials[i];
                if (bar == null || material == null)
                    continue;

                float offset = Random.Range(-jitter, jitter);
                bar.localPosition = new Vector3(offset, (i - 1) * 0.08f, 0f);
                bar.localScale = new Vector3(
                    Mathf.Lerp(0.55f, 0.18f, t),
                    Mathf.Lerp(0.035f, 0.01f, t),
                    Mathf.Lerp(0.035f, 0.01f, t));

                Color barColor = i switch
                {
                    0 => cyanGlitch,
                    1 => whiteGlitch,
                    _ => magentaGlitch
                };
                barColor.a = Mathf.Lerp(barColor.a, 0f, SmoothStep(0.15f, 1f, t));
                SetMaterialColor(material, barColor);
            }
        }
    }

    void ResetVisuals()
    {
        if (flashRoot != null)
            flashRoot.localScale = Vector3.one * 0.01f;
    }

    void BuildVisualsIfNeeded()
    {
        if (visualsBuilt)
            return;

        visualsBuilt = true;

        flashRoot = CreateChild("Flash", Vector3.up * 0.12f);
        MeshFilter flashFilter = flashRoot.gameObject.AddComponent<MeshFilter>();
        MeshRenderer flashRenderer = flashRoot.gameObject.AddComponent<MeshRenderer>();
        flashFilter.sharedMesh = LowPolyIcosphere.Create(1);
        flashMaterial = CreateTransparentMaterial(whiteGlitch, emissive: true);
        flashRenderer.sharedMaterial = flashMaterial;
        flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flashRenderer.receiveShadows = false;

        glitchBars = new Transform[3];
        glitchMaterials = new Material[3];
        Color[] barColors = { cyanGlitch, whiteGlitch, magentaGlitch };
        for (int i = 0; i < glitchBars.Length; i++)
        {
            Transform bar = CreateChild($"GlitchBar{i}", Vector3.up * 0.12f);
            MeshFilter barFilter = bar.gameObject.AddComponent<MeshFilter>();
            MeshRenderer barRenderer = bar.gameObject.AddComponent<MeshRenderer>();
            barFilter.sharedMesh = CreateQuadMesh();
            glitchMaterials[i] = CreateTransparentMaterial(barColors[i], emissive: true);
            barRenderer.sharedMaterial = glitchMaterials[i];
            barRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            barRenderer.receiveShadows = false;
            glitchBars[i] = bar;
        }

        pixels = CreatePixelBurst(transform);
        ResetVisuals();
    }

    static Transform CreateChild(Transform parent, string childName, Vector3 localPosition)
    {
        var child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        return child.transform;
    }

    Transform CreateChild(string childName, Vector3 localPosition)
    {
        return CreateChild(transform, childName, localPosition);
    }

    static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh { name = "GlitchQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0.01f),
            new Vector3(-0.5f, 0f, 0.01f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static ParticleSystem CreatePixelBurst(Transform parent)
    {
        var pixelObject = new GameObject("PixelBurst");
        pixelObject.transform.SetParent(parent, false);
        pixelObject.transform.localPosition = Vector3.up * 0.12f;

        ParticleSystem ps = pixelObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.35f, 0.95f, 1f, 1f),
            new Color(0.9f, 0.25f, 1f, 1f));
        main.gravityModifier = 0f;
        main.maxParticles = 14;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 8, 12)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        var renderer = pixelObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = CreateTransparentMaterial(new Color(0.55f, 0.95f, 1f, 1f), emissive: true);

        return ps;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetColor("_EmissionColor", color * Mathf.Lerp(2.2f, 0f, 1f - color.a));
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
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (emissive)
            material.SetColor("_EmissionColor", color * 2f);
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
