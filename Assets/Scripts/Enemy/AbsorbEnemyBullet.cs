using UnityEngine;

/// <summary>
/// Enemy projectile whose damage, color, and scale are set from absorbed damage.
/// Green = low damage, red = high damage.
/// </summary>
public class AbsorbEnemyBullet : Projectile
{
    [Header("Absorb Visuals")]
    [SerializeField] Color lowDamageColor = new Color(0.25f, 0.95f, 0.35f, 1f);
    [SerializeField] Color highDamageColor = new Color(0.95f, 0.2f, 0.15f, 1f);
    [Tooltip("Damage treated as 0% on the color/size ramp.")]
    [SerializeField] float visualMinDamage = 1f;
    [Tooltip("Damage treated as 100% on the color/size ramp.")]
    [SerializeField] float visualMaxDamage = 8f;
    [SerializeField] Vector3 minScale = new Vector3(0.55f, 0.55f, 0.55f);
    [SerializeField] Vector3 maxScale = new Vector3(1.75f, 1.75f, 1.75f);
    [SerializeField]     Renderer[] tintRenderers;
    MaterialPropertyBlock propertyBlock;

    float configuredDamage = 1f;

    /// <summary>
    /// Fires this bullet with the given absorbed damage and applies green→red / size visuals.
    /// </summary>
    public void LaunchAbsorbed(Vector3 fireDirection, Transform source, float absorbedDamage)
    {
        configuredDamage = Mathf.Max(0.01f, absorbedDamage);
        // Launch first so owner/ignore-collision is set before the scaled collider overlaps the shooter.
        LaunchAsEnemy(fireDirection, source, configuredDamage);
        ApplyAbsorbVisuals(configuredDamage);
    }

    public void ApplyAbsorbVisuals(float absorbedDamage)
    {
        float t = GetVisualT(absorbedDamage);
        Color color = Color.Lerp(lowDamageColor, highDamageColor, t);
        transform.localScale = Vector3.Lerp(minScale, maxScale, t);

        if (tintRenderers == null || tintRenderers.Length == 0)
            tintRenderers = GetComponentsInChildren<Renderer>(true);

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Renderer renderer = tintRenderers[i];
            if (renderer == null)
                continue;

            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor"))
                propertyBlock.SetColor("_BaseColor", color);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
                propertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    float GetVisualT(float absorbedDamage)
    {
        float min = Mathf.Max(0.01f, visualMinDamage);
        float max = Mathf.Max(min + 0.01f, visualMaxDamage);
        return Mathf.Clamp01(Mathf.InverseLerp(min, max, absorbedDamage));
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (visualMaxDamage < visualMinDamage)
            visualMaxDamage = visualMinDamage;
    }
#endif
}
