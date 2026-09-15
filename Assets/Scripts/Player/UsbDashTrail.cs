using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Short-lived visual trail segment left by a USB Dash.
/// </summary>
public class UsbDashTrail : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static Shader cachedShader;
    static Material sharedTrailMaterial;
    static MaterialPropertyBlock propertyBlock;
    static bool sharedMaterialIsUrp;

    [SerializeField] float radius = 0.85f;
    [SerializeField] float height = 1.2f;
    [SerializeField] float lifetime = 0.35f;
    [SerializeField] Color trailColor = new Color(0.35f, 0.85f, 1f, 0.55f);

    float lifeRemaining;
    MeshRenderer meshRenderer;

    public static UsbDashTrail Spawn(
        Vector3 position,
        Vector3 forward,
        float trailDuration,
        float trailRadius)
    {
        var go = new GameObject("USB Dash Trail");
        go.transform.position = position;
        if (forward.sqrMagnitude > 0.001f)
            go.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        UsbDashTrail trail = go.AddComponent<UsbDashTrail>();
        trail.Configure(trailDuration, trailRadius);
        trail.BuildVisual();
        return trail;
    }

    public void Configure(float trailDuration, float trailRadius)
    {
        lifetime = Mathf.Max(0.05f, trailDuration);
        radius = Mathf.Max(0.15f, trailRadius);
        lifeRemaining = lifetime;
    }

    void BuildVisual()
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.up * (height * 0.45f);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = new Vector3(radius * 0.95f, height * 0.42f, radius * 1.35f);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            visualCollider.enabled = false;
            Destroy(visualCollider);
        }

        meshRenderer = visual.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            return;

        EnsureSharedMaterial();
        meshRenderer.sharedMaterial = sharedTrailMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        ApplyColor(trailColor);
    }

    void Update()
    {
        lifeRemaining -= Time.deltaTime;

        if (meshRenderer != null)
        {
            float alpha = Mathf.Clamp01(lifeRemaining / Mathf.Max(0.05f, lifetime)) * trailColor.a;
            Color color = trailColor;
            color.a = alpha;
            ApplyColor(color);
        }

        if (lifeRemaining <= 0f)
            Destroy(gameObject);
    }

    void ApplyColor(Color color)
    {
        if (meshRenderer == null || sharedTrailMaterial == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        meshRenderer.GetPropertyBlock(propertyBlock);
        if (sharedMaterialIsUrp)
            propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    static void EnsureSharedMaterial()
    {
        if (sharedTrailMaterial != null)
            return;

        sharedTrailMaterial = RuntimeEffectMaterials.CreateTransparent(
            new Color(0.35f, 0.85f, 1f, 0.65f),
            emissive: true,
            materialName: "UsbDashTrailShared");
        sharedTrailMaterial.hideFlags = HideFlags.HideAndDontSave;

        cachedShader = sharedTrailMaterial.shader;
        sharedMaterialIsUrp = cachedShader != null &&
                              (cachedShader.name.Contains("Universal Render Pipeline") ||
                               cachedShader.name.Contains("RuntimeEffectUnlit"));
    }
}
