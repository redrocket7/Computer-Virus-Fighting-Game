using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places a fog lid over a passageway until a connected room is revealed.
/// </summary>
[DisallowMultipleComponent]
public class PassageFogCover : MonoBehaviour
{
    const string CoverObjectName = "Passage Fog Cover";

    static readonly List<PassageFogCover> ActiveCovers = new List<PassageFogCover>();

    [Header("Appearance")]
    [SerializeField] Material coverMaterial;
    [SerializeField] Color coverColor = Color.black;
    [SerializeField] bool overrideMaterialColor = true;

    [Header("Layout")]
    [SerializeField] float coverHeight = 6f;
    [SerializeField] float footprintInset = 0f;
    [SerializeField] float coverThickness = 0.15f;

    PassagewayChunk passage;
    DungeonGenerator dungeon;
    GameObject coverObject;
    Material coverInstance;

    void Awake()
    {
        passage = GetComponent<PassagewayChunk>();
    }

    void OnEnable()
    {
        if (!ActiveCovers.Contains(this))
            ActiveCovers.Add(this);

        RoomFogCover.RevealChanged += RefreshVisibility;
    }

    void Start()
    {
        if (dungeon == null)
            dungeon = GetComponentInParent<DungeonGenerator>();

        RefreshVisibility();
    }

    void OnDisable()
    {
        ActiveCovers.Remove(this);
        RoomFogCover.RevealChanged -= RefreshVisibility;
    }

    void OnDestroy()
    {
        ActiveCovers.Remove(this);
        RoomFogCover.RevealChanged -= RefreshVisibility;
        FogCoverVisual.DestroyMaterial(ref coverInstance);

        if (coverObject != null)
        {
            if (Application.isPlaying)
                Destroy(coverObject);
            else
                DestroyImmediate(coverObject);
            coverObject = null;
        }
    }

    public void Configure(
        DungeonGenerator generator,
        Material material,
        Color color,
        bool overrideColor,
        float height,
        float inset,
        float thickness)
    {
        dungeon = generator;
        coverMaterial = material;
        coverColor = color;
        overrideMaterialColor = overrideColor;
        coverHeight = height;
        footprintInset = inset;
        coverThickness = thickness;

        if (coverObject != null)
            ApplyAppearance();
    }

    public void RefreshVisibility()
    {
        EnsureCoverObject();
        if (coverObject == null)
            return;

        bool revealed = IsPassageFogRevealed();
        coverObject.SetActive(!revealed);
        if (!revealed)
            UpdateCoverTransform();
    }

    public static void RefreshAll()
    {
        for (int i = 0; i < ActiveCovers.Count; i++)
        {
            PassageFogCover cover = ActiveCovers[i];
            if (cover != null)
                cover.RefreshVisibility();
        }
    }

    bool IsPassageFogRevealed()
    {
        if (passage == null)
            passage = GetComponent<PassagewayChunk>();
        if (passage == null)
            return true;

        if (FogCoverVisual.IsRoomFogRevealed(passage.ConnectedRoomA) ||
            FogCoverVisual.IsRoomFogRevealed(passage.ConnectedRoomB))
        {
            return true;
        }

        // Fallback if this chunk was not tagged during generation.
        if (dungeon == null)
            dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon == null)
            return false;

        IReadOnlyList<RoomDefinition> rooms = dungeon.PlacedRooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomDefinition room = rooms[i];
            if (!FogCoverVisual.IsRoomFogRevealed(room))
                continue;

            if (DungeonMapLayout.TouchesRoom(passage, room))
                return true;
        }

        return false;
    }

    void EnsureCoverObject()
    {
        if (coverObject != null)
            return;

        if (passage == null)
            passage = GetComponent<PassagewayChunk>();
        if (passage == null)
            return;

        if (dungeon == null)
            dungeon = GetComponentInParent<DungeonGenerator>();

        Transform parent = dungeon != null ? dungeon.transform : transform;
        coverObject = FogCoverVisual.EnsureCoverObject(parent, CoverObjectName + " " + GetEntityId(), gameObject.layer);
        ApplyAppearance();
        UpdateCoverTransform();
    }

    void UpdateCoverTransform()
    {
        if (coverObject == null || passage == null)
            return;

        Rect footprint = passage.GetWorldFootprint(-Mathf.Max(0f, footprintInset));
        float width = Mathf.Max(0.5f, footprint.width);
        float depth = Mathf.Max(0.5f, footprint.height);
        Vector3 worldCenter = new Vector3(
            footprint.xMin + footprint.width * 0.5f,
            coverHeight,
            footprint.yMin + footprint.height * 0.5f);

        coverObject.transform.position = worldCenter;
        coverObject.transform.rotation = Quaternion.identity;
        coverObject.transform.localScale = new Vector3(width, Mathf.Max(0.05f, coverThickness), depth);
    }

    void ApplyAppearance()
    {
        if (coverObject == null)
            return;

        var renderer = coverObject.GetComponent<MeshRenderer>();
        FogCoverVisual.ApplyAppearance(
            renderer,
            coverMaterial,
            coverColor,
            overrideMaterialColor,
            ref coverInstance);
    }
}
