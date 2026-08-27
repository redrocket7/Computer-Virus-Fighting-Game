using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// See-through dome shield. Absorbs damage until broken.
/// Damage is routed through the owning <see cref="EnemyAI"/>.
/// </summary>
[DisallowMultipleComponent]
public class EnemyShield : MonoBehaviour
{
    [SerializeField] float maxShieldHealth = 4f;
    [SerializeField] Color domeColor = new Color(0.25f, 0.75f, 1f, 0.28f);

    EnemyAI owner;
    float currentHealth;
    MeshRenderer domeRenderer;
    Material domeMaterial;
    SphereCollider hitCollider;

    public bool IsActive => currentHealth > 0f;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxShieldHealth;

    public void Initialize(EnemyAI enemy, float health, float radius)
    {
        owner = enemy;
        maxShieldHealth = Mathf.Max(0.1f, health);
        currentHealth = maxShieldHealth;
        BuildVisuals(Mathf.Max(0.75f, radius));
    }

    public void AbsorbDamage(float amount)
    {
        if (!IsActive || amount <= 0f)
            return;

        currentHealth -= amount;
        if (currentHealth <= 0f)
            Break();
    }

    void Break()
    {
        currentHealth = 0f;
        owner?.NotifyShieldBroken();
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (domeMaterial != null)
            Destroy(domeMaterial);
    }

    void BuildVisuals(float radius)
    {
        var shell = new GameObject("Dome Shell");
        shell.transform.SetParent(transform, false);

        MeshFilter shellFilter = shell.AddComponent<MeshFilter>();
        domeRenderer = shell.AddComponent<MeshRenderer>();
        shellFilter.sharedMesh = SmoothDomeMesh.Build(radius, longitudeSegments: 24, latitudeSegments: 12);
        domeMaterial = CreateTransparentMaterial(domeColor);
        domeRenderer.sharedMaterial = domeMaterial;
        domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        domeRenderer.receiveShadows = false;

        hitCollider = gameObject.AddComponent<SphereCollider>();
        hitCollider.radius = radius;
        hitCollider.center = Vector3.zero;
        hitCollider.isTrigger = true;
    }

    static Material CreateTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = "ShieldDomeRuntime",
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
        material.SetColor("_EmissionColor", color * 1.25f);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        return material;
    }
}

/// <summary>
/// Smooth hemisphere dome that dips slightly below the equator to cover the body.
/// </summary>
static class SmoothDomeMesh
{
    public static Mesh Build(float radius, int longitudeSegments, int latitudeSegments)
    {
        longitudeSegments = Mathf.Max(8, longitudeSegments);
        latitudeSegments = Mathf.Max(4, latitudeSegments);

        // Polar angle from +Y: 0 at top. Extend a bit past the equator so the body is covered.
        float minPolar = 0f;
        float maxPolar = Mathf.PI * 0.62f;

        var vertices = new List<Vector3>((longitudeSegments + 1) * (latitudeSegments + 1));
        var triangles = new List<int>(longitudeSegments * latitudeSegments * 6);

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = lat / (float)latitudeSegments;
            float polar = Mathf.Lerp(minPolar, maxPolar, v);
            float y = Mathf.Cos(polar) * radius;
            float ringRadius = Mathf.Sin(polar) * radius;

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = lon / (float)longitudeSegments;
                float azimuth = u * Mathf.PI * 2f;
                float x = Mathf.Cos(azimuth) * ringRadius;
                float z = Mathf.Sin(azimuth) * ringRadius;
                vertices.Add(new Vector3(x, y, z));
            }
        }

        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int current = lat * stride + lon;
                int next = current + stride;

                triangles.Add(current);
                triangles.Add(current + 1);
                triangles.Add(next);

                triangles.Add(current + 1);
                triangles.Add(next + 1);
                triangles.Add(next);
            }
        }

        var mesh = new Mesh { name = "SmoothShieldDome" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
