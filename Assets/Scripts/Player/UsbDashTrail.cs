using UnityEngine;

/// <summary>
/// Short-lived visual trail segment left by a USB Dash.
/// </summary>
public class UsbDashTrail : MonoBehaviour
{
    [SerializeField] float radius = 0.85f;
    [SerializeField] float height = 1.2f;
    [SerializeField] float lifetime = 0.35f;
    [SerializeField] Color trailColor = new Color(0.35f, 0.85f, 1f, 0.55f);

    float lifeRemaining;
    Material runtimeMaterial;

    public static UsbDashTrail Spawn(
        Vector3 position,
        Vector3 forward,
        float trailLifetime,
        float trailRadius)
    {
        var go = new GameObject("USB Dash Trail");
        go.transform.position = position;
        if (forward.sqrMagnitude > 0.001f)
            go.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        UsbDashTrail trail = go.AddComponent<UsbDashTrail>();
        trail.Configure(trailLifetime, trailRadius);
        trail.BuildVisual();
        return trail;
    }

    public void Configure(float trailLifetime, float trailRadius)
    {
        lifetime = Mathf.Max(0.05f, trailLifetime);
        radius = Mathf.Max(0.15f, trailRadius);
        lifeRemaining = lifetime;
    }

    void BuildVisual()
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(transform, false);
        // Upright capsule, slightly stretched along the dash direction.
        visual.transform.localPosition = Vector3.up * (height * 0.45f);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = new Vector3(radius * 0.95f, height * 0.42f, radius * 1.35f);

        // CreatePrimitive adds a collider; Destroy() is deferred one frame and will bump the player.
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            visualCollider.enabled = false;
            Destroy(visualCollider);
        }

        var renderer = visual.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            runtimeMaterial = CreateTrailMaterial(trailColor);
            renderer.sharedMaterial = runtimeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    void Update()
    {
        lifeRemaining -= Time.deltaTime;

        if (runtimeMaterial != null)
        {
            float alpha = Mathf.Clamp01(lifeRemaining / Mathf.Max(0.05f, lifetime)) * trailColor.a;
            Color color = trailColor;
            color.a = alpha;
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", color);
            if (runtimeMaterial.HasProperty("_Color"))
                runtimeMaterial.SetColor("_Color", color);
        }

        if (lifeRemaining <= 0f)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    static Material CreateTrailMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color");
        var material = new Material(shader)
        {
            name = "UsbDashTrailRuntime",
            renderQueue = 3000
        };

        if (shader != null && shader.name.Contains("Universal Render Pipeline"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
        }

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        return material;
    }
}
