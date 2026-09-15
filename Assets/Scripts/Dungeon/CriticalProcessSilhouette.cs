using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Non-interactive black silhouette used to preview a Critical Process mega before doors lock.
/// </summary>
public class CriticalProcessSilhouette : MonoBehaviour
{
    [SerializeField] float pulseSpeed = 3.2f;
    [SerializeField] float pulseScale = 0.06f;
    [SerializeField] Color silhouetteColor = new Color(0.02f, 0.02f, 0.04f, 0.72f);

    Vector3 baseScale;
    float phase;
    Material sharedMaterial;

    public static CriticalProcessSilhouette Create(GameObject megaPrefab, Vector3 worldPosition)
    {
        if (megaPrefab == null)
            return null;

        // Keep inactive until gameplay components are stripped so Start() never runs.
        var holder = new GameObject("Critical Process Telegraph Holder");
        holder.SetActive(false);

        GameObject ghost = Instantiate(megaPrefab, holder.transform);
        ghost.name = megaPrefab.name + " (Critical Process Telegraph)";
        ghost.transform.localPosition = Vector3.zero;
        ghost.transform.localRotation = Quaternion.identity;

        DisableGameplayComponents(ghost);

        CriticalProcessSilhouette silhouette = ghost.AddComponent<CriticalProcessSilhouette>();
        silhouette.ApplySilhouetteLook(ghost);
        silhouette.baseScale = ghost.transform.localScale;
        silhouette.phase = Random.Range(0f, Mathf.PI * 2f);

        holder.transform.position = worldPosition;
        holder.SetActive(true);

        // Flatten hierarchy so destroying the silhouette cleans up the holder too.
        ghost.transform.SetParent(null, true);
        Destroy(holder);

        return silhouette;
    }

    void OnDestroy()
    {
        if (sharedMaterial != null)
            Destroy(sharedMaterial);
    }

    void LateUpdate()
    {
        float pulse = 1f + pulseScale * Mathf.Sin(Time.time * pulseSpeed + phase);
        transform.localScale = baseScale * pulse;
    }

    static void DisableGameplayComponents(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null)
                behaviour.enabled = false;
        }

        NavMeshAgent[] agents = root.GetComponentsInChildren<NavMeshAgent>(true);
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null)
                agents[i].enabled = false;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];
            if (body == null)
                continue;

            body.isKinematic = true;
            body.detectCollisions = false;
        }
    }

    void ApplySilhouetteLook(GameObject root)
    {
        sharedMaterial = CreateSilhouetteMaterial(silhouetteColor);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            var materials = new Material[renderer.sharedMaterials.Length];
            for (int m = 0; m < materials.Length; m++)
                materials[m] = sharedMaterial;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    static Material CreateSilhouetteMaterial(Color color)
    {
        return RuntimeEffectMaterials.CreateTransparent(color, emissive: false, materialName: "CriticalProcessSilhouette");
    }
}
