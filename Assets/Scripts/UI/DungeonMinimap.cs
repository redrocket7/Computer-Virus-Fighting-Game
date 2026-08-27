using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right schematic minimap that follows the player and shows nearby rooms.
/// Built at runtime onto the Player HUD canvas.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class DungeonMinimap : MonoBehaviour
{
    [SerializeField] Vector2 mapSize = new Vector2(260f, 260f);
    [SerializeField] Vector2 mapMargin = new Vector2(24f, 24f);

    [Header("View")]
    [Tooltip("World-space width and height shown around the player. Larger values zoom the minimap out.")]
    [SerializeField, Min(8f)] float viewSize = 300f;
    [SerializeField] Color frameColor = new Color(0.08f, 0.09f, 0.11f, 0.88f);
    [SerializeField] Color roomColor = new Color(0.42f, 0.46f, 0.5f, 0.95f);
    [SerializeField] Color startRoomColor = new Color(0.28f, 0.62f, 0.4f, 0.95f);
    [SerializeField] Color passageColor = new Color(0.24f, 0.26f, 0.29f, 0.95f);
    [SerializeField] Color playerColor = new Color(0.35f, 0.95f, 0.45f, 1f);

    struct MapTile
    {
        public RectTransform Rect;
        public Rect WorldRect;
        public RoomDefinition Room;
        public PassagewayChunk Passage;
    }

    DungeonGenerator dungeon;
    PlayerController player;
    DungeonMapOverlay mapOverlay;
    RectTransform mapRoot;
    RectTransform mapContent;
    RectTransform playerMarker;
    RectTransform playerHeading;
    readonly List<MapTile> tiles = new List<MapTile>();
    readonly List<RoomEncounter> watchedEncounters = new List<RoomEncounter>();
    readonly HashSet<PassagewayChunk> revealedPassages = new HashSet<PassagewayChunk>();
    Vector2 innerSize;
    Vector2 mapCenter;
    float mapScale = 1f;
    float appliedViewSize = -1f;
    bool hasMap;
    bool revealDirty = true;

    public float ViewSize
    {
        get => viewSize;
        set
        {
            viewSize = Mathf.Max(8f, value);
            appliedViewSize = -1f;
            ApplyViewSize();
        }
    }

    void Awake()
    {
        BuildFrame();
    }

    void OnEnable()
    {
        dungeon = FindAnyObjectByType<DungeonGenerator>();
        player = FindAnyObjectByType<PlayerController>();
        mapOverlay = GetComponent<DungeonMapOverlay>();

        if (dungeon != null)
            dungeon.Generated += Rebuild;
    }

    void Start()
    {
        if (!hasMap && dungeon != null && dungeon.PlacedRoomCount > 0)
            Rebuild();
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.Generated -= Rebuild;

        UnsubscribeEncounters();
    }

    void LateUpdate()
    {
        if (!hasMap || player == null)
            return;

        ApplyViewSize();
        RefreshView();
        UpdateVisibility();
    }

    void Rebuild()
    {
        hasMap = false;
        tiles.Clear();
        UnsubscribeEncounters();
        if (dungeon == null || mapContent == null)
            return;

        for (int i = mapContent.childCount - 1; i >= 0; i--)
            Destroy(mapContent.GetChild(i).gameObject);

        if (dungeon.PlacedRoomCount == 0)
            return;

        mapCenter = innerSize * 0.5f;
        ApplyViewSize();

        for (int i = 0; i < dungeon.PlacedPassages.Count; i++)
        {
            PassagewayChunk passage = dungeon.PlacedPassages[i];
            CreateTile(passage.GetWorldFootprint(), passageColor, "Passage", null, passage);
        }

        for (int i = 0; i < dungeon.PlacedRooms.Count; i++)
        {
            RoomDefinition room = dungeon.PlacedRooms[i];
            Color color = i == 0 ? startRoomColor : roomColor;
            IReadOnlyList<Rect> roomRects = room.GetMinimapRects();
            for (int r = 0; r < roomRects.Count; r++)
                CreateTile(roomRects[r], color, i == 0 ? "Start Room" : "Room", room);
            WatchEncounter(room);
        }

        BuildPlayerMarker();
        hasMap = true;
        revealDirty = true;
        RefreshView();
        UpdateVisibility();
    }

    void WatchEncounter(RoomDefinition room)
    {
        RoomEncounter encounter = DungeonMapLayout.GetEncounter(room);
        if (encounter == null || watchedEncounters.Contains(encounter))
            return;

        encounter.Started += MarkRevealDirty;
        encounter.Cleared += MarkRevealDirty;
        watchedEncounters.Add(encounter);
    }

    void MarkRevealDirty()
    {
        revealDirty = true;
        UpdateVisibility();
    }

    void UnsubscribeEncounters()
    {
        for (int i = 0; i < watchedEncounters.Count; i++)
        {
            RoomEncounter encounter = watchedEncounters[i];
            if (encounter == null)
                continue;

            encounter.Started -= MarkRevealDirty;
            encounter.Cleared -= MarkRevealDirty;
        }

        watchedEncounters.Clear();
    }

    void UpdateVisibility()
    {
        if (mapRoot == null)
            return;

        bool inCombat = IsPlayerInCombat();
        bool overlayOpen = mapOverlay != null && mapOverlay.IsOpen;
        mapRoot.gameObject.SetActive(hasMap && !inCombat && !overlayOpen);
        if (inCombat || overlayOpen || !revealDirty)
            return;

        revealDirty = false;
        RefreshRevealedPassages();

        for (int i = 0; i < tiles.Count; i++)
        {
            MapTile tile = tiles[i];
            if (tile.Rect == null)
                continue;

            tile.Rect.gameObject.SetActive(IsTileRevealed(tile));
        }
    }

    bool IsPlayerInCombat()
    {
        for (int i = 0; i < watchedEncounters.Count; i++)
        {
            RoomEncounter encounter = watchedEncounters[i];
            if (encounter != null && encounter.IsInProgress)
                return true;
        }

        return false;
    }

    bool IsTileRevealed(MapTile tile)
    {
        if (tile.Room != null)
            return DungeonMapLayout.IsRoomRevealed(tile.Room);

        return tile.Passage != null && revealedPassages.Contains(tile.Passage);
    }

    void RefreshRevealedPassages()
    {
        DungeonMapLayout.CollectRevealedPassages(dungeon, revealedPassages);
    }

    void ApplyViewSize()
    {
        float size = Mathf.Max(8f, viewSize);
        if (Mathf.Approximately(appliedViewSize, size))
            return;

        appliedViewSize = size;
        if (innerSize.x > 0.01f)
            mapScale = innerSize.x / size;
    }

    void RefreshView()
    {
        Vector3 playerPosition = player.transform.position;

        for (int i = 0; i < tiles.Count; i++)
        {
            MapTile tile = tiles[i];
            if (tile.Rect == null)
                continue;

            Rect worldRect = tile.WorldRect;
            tile.Rect.anchoredPosition = WorldToMap(new Vector3(worldRect.xMin, 0f, worldRect.yMin), playerPosition);
            tile.Rect.sizeDelta = new Vector2(worldRect.width * mapScale, worldRect.height * mapScale);
        }

        if (playerMarker == null)
            return;

        playerMarker.anchoredPosition = mapCenter;
        playerMarker.SetAsLastSibling();
        playerMarker.localEulerAngles = new Vector3(0f, 0f, -player.transform.eulerAngles.y);
        if (playerHeading != null)
            playerHeading.localEulerAngles = Vector3.zero;
    }

    void CreateTile(
        Rect worldRect,
        Color color,
        string name,
        RoomDefinition room,
        PassagewayChunk passage = null)
    {
        Image image = CreateImage(name, mapContent, color);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.sizeDelta = new Vector2(worldRect.width * mapScale, worldRect.height * mapScale);

        tiles.Add(new MapTile
        {
            Rect = rect,
            WorldRect = worldRect,
            Room = room,
            Passage = passage
        });
    }

    Vector2 WorldToMap(Vector3 worldPosition, Vector3 playerPosition)
    {
        float x = (worldPosition.x - playerPosition.x) * mapScale;
        float y = (worldPosition.z - playerPosition.z) * mapScale;
        return mapCenter + new Vector2(x, y);
    }

    void BuildFrame()
    {
        innerSize = mapSize - new Vector2(8f, 8f);

        mapRoot = CreateImage("Minimap", transform, frameColor).rectTransform;
        mapRoot.anchorMin = new Vector2(1f, 1f);
        mapRoot.anchorMax = new Vector2(1f, 1f);
        mapRoot.pivot = new Vector2(1f, 1f);
        mapRoot.anchoredPosition = new Vector2(-mapMargin.x, -mapMargin.y);
        mapRoot.sizeDelta = mapSize;

        Image inner = CreateImage("Inner", mapRoot, new Color(0.04f, 0.045f, 0.05f, 0.95f));
        RectTransform innerRect = inner.rectTransform;
        innerRect.anchorMin = Vector2.zero;
        innerRect.anchorMax = Vector2.one;
        innerRect.offsetMin = new Vector2(4f, 4f);
        innerRect.offsetMax = new Vector2(-4f, -4f);
        inner.gameObject.AddComponent<RectMask2D>();

        mapContent = new GameObject("Map", typeof(RectTransform)).GetComponent<RectTransform>();
        mapContent.SetParent(innerRect, false);
        mapContent.anchorMin = Vector2.zero;
        mapContent.anchorMax = Vector2.one;
        mapContent.offsetMin = Vector2.zero;
        mapContent.offsetMax = Vector2.zero;
        mapContent.pivot = Vector2.zero;
    }

    void BuildPlayerMarker()
    {
        Image marker = CreateImage("Player", mapContent, playerColor);
        playerMarker = marker.rectTransform;
        playerMarker.anchorMin = Vector2.zero;
        playerMarker.anchorMax = Vector2.zero;
        playerMarker.pivot = new Vector2(0.5f, 0.5f);
        playerMarker.sizeDelta = new Vector2(10f, 10f);
        playerMarker.SetAsLastSibling();

        Image heading = CreateImage("Heading", playerMarker, playerColor);
        playerHeading = heading.rectTransform;
        playerHeading.anchorMin = new Vector2(0.5f, 0.5f);
        playerHeading.anchorMax = new Vector2(0.5f, 0.5f);
        playerHeading.pivot = new Vector2(0.5f, 0f);
        playerHeading.anchoredPosition = Vector2.zero;
        playerHeading.sizeDelta = new Vector2(4f, 10f);
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
