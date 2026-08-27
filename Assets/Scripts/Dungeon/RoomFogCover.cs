using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places a fog lid over a room until the player enters it (or it has no encounter).
/// </summary>
[DisallowMultipleComponent]
public class RoomFogCover : MonoBehaviour
{
    const string CoverObjectName = "Room Fog Cover";

    public static event System.Action RevealChanged;

    [Header("Appearance")]
    [SerializeField] Material coverMaterial;
    [SerializeField] Color coverColor = Color.black;
    [Tooltip("When enabled, Cover Color overwrites the material base color.")]
    [SerializeField] bool overrideMaterialColor = true;

    [Header("Layout")]
    [SerializeField] float coverHeight = 6f;
    [SerializeField] float footprintInset = 0f;
    [SerializeField] float coverThickness = 0.15f;

    RoomDefinition room;
    RoomEncounter encounter;
    GameObject coverObject;
    Material coverInstance;
    bool eventsBound;

    public Material CoverMaterial => coverMaterial;

    void Awake()
    {
        room = GetComponent<RoomDefinition>();
        encounter = GetComponent<RoomEncounter>();
        if (encounter == null && room != null)
            encounter = room.Encounter;
    }

    void OnEnable()
    {
        BindEvents();
    }

    void Start()
    {
        BindEvents();
        EnsureCoverObject();
        RefreshVisibility();
    }

    void OnDisable()
    {
        UnbindEvents();
    }

    void OnDestroy()
    {
        UnbindEvents();
        FogCoverVisual.DestroyMaterial(ref coverInstance);
    }

    /// <summary>Apply shared dungeon fog settings (used when the component is added at runtime).</summary>
    public void Configure(
        Material material,
        Color color,
        bool overrideColor,
        float height,
        float inset,
        float thickness)
    {
        coverMaterial = material;
        coverColor = color;
        overrideMaterialColor = overrideColor;
        coverHeight = height;
        footprintInset = inset;
        coverThickness = thickness;

        if (coverObject != null)
            ApplyAppearance();
    }

    void BindEvents()
    {
        if (eventsBound || encounter == null)
            return;

        encounter.Started += HandleRevealChanged;
        encounter.Cleared += HandleRevealChanged;
        eventsBound = true;
    }

    void UnbindEvents()
    {
        if (!eventsBound || encounter == null)
            return;

        encounter.Started -= HandleRevealChanged;
        encounter.Cleared -= HandleRevealChanged;
        eventsBound = false;
    }

    void HandleRevealChanged()
    {
        RefreshVisibility();
        RevealChanged?.Invoke();
        PassageFogCover.RefreshAll();
    }

    bool ShouldShowCover()
    {
        return !FogCoverVisual.IsRoomFogRevealed(room);
    }

    public void RefreshVisibility()
    {
        if (coverObject == null)
            EnsureCoverObject();

        if (coverObject == null)
            return;

        bool show = ShouldShowCover();
        coverObject.SetActive(show);
        if (show)
            UpdateCoverTransform();
    }

    void EnsureCoverObject()
    {
        if (room == null)
            room = GetComponent<RoomDefinition>();
        if (room == null)
            return;

        coverObject = FogCoverVisual.EnsureCoverObject(transform, CoverObjectName, gameObject.layer);
        ApplyAppearance();
        UpdateCoverTransform();
    }

    void UpdateCoverTransform()
    {
        if (coverObject == null || room == null)
            return;

        Vector2 size = room.FootprintSize;
        float inset = Mathf.Max(0f, footprintInset) * 2f;
        float width = Mathf.Max(0.5f, size.x - inset);
        float depth = Mathf.Max(0.5f, size.y - inset);

        Vector3 localCenter = room.FootprintCenter;
        float localY = transform.InverseTransformPoint(new Vector3(0f, coverHeight, 0f)).y;

        coverObject.transform.localPosition = new Vector3(localCenter.x, localY, localCenter.z);
        coverObject.transform.localRotation = Quaternion.identity;
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
