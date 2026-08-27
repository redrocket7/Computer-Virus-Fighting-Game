using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Builds a textured ground plane around the dungeon with holes cut for rooms
/// (and optionally passageways) so the skybox is hidden without covering floors.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(DungeonGenerator))]
public class OutOfBoundsCover : MonoBehaviour
{
    const string CoverObjectName = "Out Of Bounds Cover";

    [Header("Appearance")]
    [SerializeField] Material coverMaterial;
    [SerializeField] Texture coverTexture;
    [SerializeField] Color coverColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [Tooltip("World units per texture tile.")]
    [SerializeField] float textureTileSize = 20f;

    [Header("Layout")]
    [Tooltip("World-space Y of the cover plane. Set this to the top of the room walls to hide the skybox.")]
    [SerializeField] float coverHeight = 6f;
    [Tooltip("How far the cover extends past the outermost room.")]
    [SerializeField] float padding = 80f;
    [Tooltip("How far corridor holes extend past entry/exit along their length (seals door seams).")]
    [SerializeField] float passageOvershoot = 0.5f;
    [Tooltip("Shrink corridor holes across their width. Keep near 0; use Hole Bleed to seal seams instead.")]
    [SerializeField] float passageWidthInset = 0f;
    [Tooltip("Expand every hole slightly so cover sits on wall tops instead of leaving hairline gaps.")]
    [SerializeField] float holeBleed = 0.02f;
    [SerializeField] bool cutHolesForPassageways = true;

    Mesh coverMesh;
    GameObject coverObject;
    Material coverInstance;

    void Start()
    {
        Rebuild();
    }

    void OnDestroy()
    {
        DestroyCoverMesh();
        DestroyCoverMaterial();
    }

    public void Rebuild()
    {
        var holes = new List<Rect>();
        CollectHoles(holes);
        if (holes.Count == 0)
        {
            ClearCover();
            return;
        }

        Rect bounds = holes[0];
        for (int i = 1; i < holes.Count; i++)
            bounds = Encapsulate(bounds, holes[i]);

        bounds.xMin -= padding;
        bounds.yMin -= padding;
        bounds.xMax += padding;
        bounds.yMax += padding;

        EnsureCoverObject();
        coverObject.SetActive(true);
        BuildMesh(bounds, holes);
        ApplyAppearance();
    }

    void CollectHoles(List<Rect> holes)
    {
        var generator = GetComponent<DungeonGenerator>();
        if (generator == null)
            return;

        foreach (var room in generator.PlacedRooms)
        {
            if (room == null)
                continue;

            holes.Add(ExpandRect(room.GetWorldFootprint(), Mathf.Max(0f, holeBleed)));
        }

        if (!cutHolesForPassageways)
            return;

        foreach (var passage in generator.PlacedPassages)
        {
            if (passage == null)
                continue;

            holes.Add(ExpandRect(BuildPassageHole(passage), Mathf.Max(0f, holeBleed)));
        }
    }

    Rect BuildPassageHole(PassagewayChunk passage)
    {
        Rect footprint = passage.GetWorldTileFootprint(0f);
        float overshoot = Mathf.Max(0f, passageOvershoot);
        float inset = Mathf.Max(0f, passageWidthInset);

        if (passage.Turn != PassagewayTurn.Straight)
            return ExpandRect(footprint, overshoot);

        // Stretch-aware corridor: extend only along travel so door seams seal
        // without widening past the walls (that left skybox beside corridors).
        Vector3 along = passage.ExitPosition - passage.EntryPosition;
        along.y = 0f;
        if (along.sqrMagnitude < 0.0001f)
            return ExpandRect(footprint, overshoot);

        bool alongX = Mathf.Abs(along.x) >= Mathf.Abs(along.z);
        if (alongX)
        {
            float yMin = footprint.yMin + inset;
            float yMax = footprint.yMax - inset;
            if (yMax <= yMin)
            {
                yMin = footprint.yMin;
                yMax = footprint.yMax;
            }

            return Rect.MinMaxRect(
                footprint.xMin - overshoot,
                yMin,
                footprint.xMax + overshoot,
                yMax);
        }

        float xMin = footprint.xMin + inset;
        float xMax = footprint.xMax - inset;
        if (xMax <= xMin)
        {
            xMin = footprint.xMin;
            xMax = footprint.xMax;
        }

        return Rect.MinMaxRect(
            xMin,
            footprint.yMin - overshoot,
            xMax,
            footprint.yMax + overshoot);
    }

    void EnsureCoverObject()
    {
        if (coverObject == null)
        {
            Transform existing = transform.Find(CoverObjectName);
            if (existing != null)
                coverObject = existing.gameObject;
            else
                coverObject = new GameObject(CoverObjectName);
        }

        // Mesh verts are authored in world XZ; keep the object unshifted so holes
        // line up with rooms/passages (manual offsets were only masking snap error).
        coverObject.transform.SetParent(transform, false);
        coverObject.transform.localPosition = Vector3.zero;
        coverObject.transform.localRotation = Quaternion.identity;
        coverObject.transform.localScale = Vector3.one;
        coverObject.layer = 0;

        if (coverObject.GetComponent<MeshFilter>() == null)
            coverObject.AddComponent<MeshFilter>();
        if (coverObject.GetComponent<MeshRenderer>() == null)
            coverObject.AddComponent<MeshRenderer>();

        if (!coverObject.TryGetComponent(out NavMeshModifier modifier))
            modifier = coverObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
    }

    const float SliverSize = 0.001f;
    const float CoverOverlap = 0.02f;

    void BuildMesh(Rect bounds, List<Rect> holes)
    {
        // Exact hole edges — snapping to a 0.05 grid was shifting the cover by
        // ~0.02 units (the offset you found by hand).
        var areas = new List<Rect> { bounds };
        for (int i = 0; i < holes.Count; i++)
            SubtractHole(areas, holes[i]);

        for (int i = areas.Count - 1; i >= 0; i--)
        {
            Rect area = areas[i];
            if (area.width < SliverSize || area.height < SliverSize)
                areas.RemoveAt(i);
        }

        MergeAdjacentRects(areas);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float tile = Mathf.Max(0.01f, textureTileSize);
        Transform coverTransform = coverObject.transform;

        foreach (Rect area in areas)
        {
            if (area.width < SliverSize || area.height < SliverSize)
                continue;

            Rect quad = ExpandRect(area, CoverOverlap);
            int index = vertices.Count;
            vertices.Add(WorldToCoverLocal(coverTransform, quad.xMin, quad.yMin));
            vertices.Add(WorldToCoverLocal(coverTransform, quad.xMin, quad.yMax));
            vertices.Add(WorldToCoverLocal(coverTransform, quad.xMax, quad.yMax));
            vertices.Add(WorldToCoverLocal(coverTransform, quad.xMax, quad.yMin));

            uvs.Add(new Vector2(quad.xMin / tile, quad.yMin / tile));
            uvs.Add(new Vector2(quad.xMin / tile, quad.yMax / tile));
            uvs.Add(new Vector2(quad.xMax / tile, quad.yMax / tile));
            uvs.Add(new Vector2(quad.xMax / tile, quad.yMin / tile));

            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }

        DestroyCoverMesh();
        coverMesh = new Mesh
        {
            name = CoverObjectName,
            indexFormat = vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        coverMesh.SetVertices(vertices);
        coverMesh.SetUVs(0, uvs);
        coverMesh.SetTriangles(triangles, 0);
        coverMesh.RecalculateNormals();
        coverMesh.RecalculateBounds();

        coverObject.GetComponent<MeshFilter>().sharedMesh = coverMesh;
    }

    Vector3 WorldToCoverLocal(Transform coverTransform, float worldX, float worldZ)
    {
        return coverTransform.InverseTransformPoint(new Vector3(worldX, coverHeight, worldZ));
    }

    void ApplyAppearance()
    {
        var renderer = coverObject.GetComponent<MeshRenderer>();
        Material source = coverMaterial;
        if (source == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            source = new Material(shader) { name = "Out Of Bounds Cover" };
        }

        if (coverInstance == null || coverInstance.shader != source.shader)
        {
            DestroyCoverMaterial();
            coverInstance = new Material(source) { name = "Out Of Bounds Cover Instance" };
        }
        else
        {
            coverInstance.CopyPropertiesFromMaterial(source);
        }

        if (coverMaterial == null)
            Destroy(source);

        if (coverTexture != null)
        {
            if (coverInstance.HasProperty("_BaseMap"))
                coverInstance.SetTexture("_BaseMap", coverTexture);
            if (coverInstance.HasProperty("_MainTex"))
                coverInstance.SetTexture("_MainTex", coverTexture);
        }

        if (coverInstance.HasProperty("_BaseColor"))
            coverInstance.SetColor("_BaseColor", coverColor);
        if (coverInstance.HasProperty("_Color"))
            coverInstance.SetColor("_Color", coverColor);

        renderer.sharedMaterial = coverInstance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void ClearCover()
    {
        if (coverObject != null)
            coverObject.SetActive(false);

        DestroyCoverMesh();
    }

    void DestroyCoverMaterial()
    {
        if (coverInstance == null)
            return;

        if (Application.isPlaying)
            Destroy(coverInstance);
        else
            DestroyImmediate(coverInstance);

        coverInstance = null;
    }

    void DestroyCoverMesh()
    {
        if (coverMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(coverMesh);
        else
            DestroyImmediate(coverMesh);

        coverMesh = null;
    }

    static void SubtractHole(List<Rect> areas, Rect hole)
    {
        var remaining = new List<Rect>(areas.Count + 4);
        foreach (Rect area in areas)
        {
            if (!area.Overlaps(hole))
            {
                remaining.Add(area);
                continue;
            }

            float cutLeft = Mathf.Max(area.xMin, hole.xMin);
            float cutRight = Mathf.Min(area.xMax, hole.xMax);
            float cutBottom = Mathf.Max(area.yMin, hole.yMin);
            float cutTop = Mathf.Min(area.yMax, hole.yMax);

            if (cutLeft > area.xMin)
                remaining.Add(Rect.MinMaxRect(area.xMin, area.yMin, cutLeft, area.yMax));
            if (cutRight < area.xMax)
                remaining.Add(Rect.MinMaxRect(cutRight, area.yMin, area.xMax, area.yMax));
            if (cutBottom > area.yMin)
                remaining.Add(Rect.MinMaxRect(cutLeft, area.yMin, cutRight, cutBottom));
            if (cutTop < area.yMax)
                remaining.Add(Rect.MinMaxRect(cutLeft, cutTop, cutRight, area.yMax));
        }

        areas.Clear();
        areas.AddRange(remaining);
    }

    static void MergeAdjacentRects(List<Rect> rects)
    {
        const float epsilon = 0.05f;
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (!TryMergeRects(rects[i], rects[j], epsilon, out Rect combined))
                        continue;

                    rects[i] = combined;
                    rects.RemoveAt(j);
                    merged = true;
                    break;
                }

                if (merged)
                    break;
            }
        }
    }

    static bool TryMergeRects(Rect a, Rect b, float epsilon, out Rect combined)
    {
        combined = default;
        bool overlapOrAbut =
            a.xMin <= b.xMax + epsilon &&
            b.xMin <= a.xMax + epsilon &&
            a.yMin <= b.yMax + epsilon &&
            b.yMin <= a.yMax + epsilon;
        if (!overlapOrAbut)
            return false;

        bool sameRow = Mathf.Abs(a.yMin - b.yMin) <= epsilon && Mathf.Abs(a.yMax - b.yMax) <= epsilon;
        bool sameColumn = Mathf.Abs(a.xMin - b.xMin) <= epsilon && Mathf.Abs(a.xMax - b.xMax) <= epsilon;
        if (!sameRow && !sameColumn)
            return false;

        combined = Encapsulate(a, b);
        return true;
    }

    static Rect ExpandRect(Rect rect, float amount)
    {
        return Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
    }

    static Rect Encapsulate(Rect a, Rect b)
    {
        float minX = Mathf.Min(a.xMin, b.xMin);
        float minZ = Mathf.Min(a.yMin, b.yMin);
        float maxX = Mathf.Max(a.xMax, b.xMax);
        float maxZ = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }
}
