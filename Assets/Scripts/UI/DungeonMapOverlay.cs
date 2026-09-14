using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Full dungeon map opened with Tab or gamepad Select. Hold right mouse to pan.
/// Controller: left stick pan, right stick cursor, A teleport, B close, triggers zoom.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class DungeonMapOverlay : MonoBehaviour
{
    [SerializeField] Vector2 panelSize = new Vector2(1400f, 860f);
    [SerializeField, Min(50f)] float viewSize = 700f;
    [SerializeField, Min(50f)] float minViewSize = 200f;
    [SerializeField, Min(50f)] float maxViewSize = 2500f;
    [SerializeField, Min(1.01f)] float zoomStep = 1.15f;

    [Header("Controller")]
    [SerializeField] float controllerPanSpeed = 420f;
    [SerializeField] float controllerCursorSpeed = 420f;
    [SerializeField] float controllerZoomSpeed = 2.5f;
    [SerializeField] float controllerStickDeadzone = 0.18f;
    [SerializeField] Color dimColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] Color panelColor = new Color(0.07f, 0.08f, 0.1f, 0.96f);
    [SerializeField] Color roomColor = new Color(0.42f, 0.46f, 0.5f, 0.95f);
    [SerializeField] Color startRoomColor = new Color(0.28f, 0.62f, 0.4f, 0.95f);
    [SerializeField] Color passageColor = new Color(0.24f, 0.26f, 0.29f, 0.95f);
    [SerializeField] Color playerColor = new Color(0.35f, 0.95f, 0.45f, 1f);
    [SerializeField] Color padColor = new Color(0.35f, 0.85f, 1f, 1f);

    struct MapTile
    {
        public RectTransform Rect;
        public Rect WorldRect;
        public RoomDefinition Room;
        public PassagewayChunk Passage;
    }

    DungeonGenerator dungeon;
    PlayerController player;
    Canvas canvas;
    Font font;
    GameObject overlayRoot;
    RectTransform mapFrame;
    RectTransform mapContent;
    RectTransform playerMarker;
    RectTransform playerHeading;
    RectTransform controllerCursor;
    readonly List<MapTile> tiles = new List<MapTile>();
    readonly List<RectTransform> padMarkers = new List<RectTransform>();
    readonly List<TeleportPad> markedPads = new List<TeleportPad>();
    readonly List<RoomEncounter> watchedEncounters = new List<RoomEncounter>();
    readonly HashSet<PassagewayChunk> revealedPassages = new HashSet<PassagewayChunk>();
    Vector2 innerSize;
    Vector2 mapCenter;
    Vector3 focusPoint;
    Vector3 controllerCursorWorld;
    Vector3 lastLayoutFocus;
    float mapScale = 1f;
    float lastLayoutScale = -1f;
    float currentViewSize;
    bool hasMap;
    bool isOpen;
    bool layoutDirty = true;

    public bool IsOpen => isOpen;

    void Awake()
    {
        canvas = GetComponent<Canvas>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        currentViewSize = viewSize;
        BuildOverlay();
        SetOpen(false);
    }

    void OnEnable()
    {
        dungeon = FindAnyObjectByType<DungeonGenerator>();
        player = PlayerRegistry.GetPrimary() ?? FindAnyObjectByType<PlayerController>();

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

        if (isOpen && PlayerRegistry.Count > 0)
        {
            IReadOnlyList<PlayerController> players = PlayerRegistry.All;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                    players[i].SetControlsLocked(false);
            }
        }
        else if (isOpen && player != null)
        {
            player.SetControlsLocked(false);
        }
    }

    void Update()
    {
        if (WasMapTogglePressed())
            SetOpen(!isOpen);

        if (!isOpen)
            return;

        HandlePan();
        HandleZoom();
        HandleController();
        HandleTeleportClick();
    }

    bool WasMapTogglePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            return true;

        for (int i = 0; i < Gamepad.all.Count; i++)
        {
            Gamepad gamepad = Gamepad.all[i];
            if (gamepad != null && gamepad.selectButton.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    void LateUpdate()
    {
        if (!isOpen || !hasMap || player == null)
            return;

        RefreshView();
    }

    void SetOpen(bool open)
    {
        isOpen = open;
        if (overlayRoot != null)
        {
            overlayRoot.SetActive(open);
            if (open)
                overlayRoot.transform.SetAsLastSibling();
        }

        if (player == null || player.IsDead)
            player = PlayerRegistry.GetPrimary() ?? FindAnyObjectByType<PlayerController>();

        if (player != null)
        {
            bool lockControls = open;
            IReadOnlyList<PlayerController> players = PlayerRegistry.All;
            if (players.Count > 0)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && !players[i].IsDead)
                        players[i].SetControlsLocked(lockControls);
                }
            }
            else
            {
                player.SetControlsLocked(lockControls);
            }
        }

        if (open)
        {
            if (player != null)
            {
                Vector3 focus = PlayerRegistry.Count > 1
                    ? PlayerRegistry.GetLivingCentroid()
                    : player.transform.position;
                focusPoint = focus;
                controllerCursorWorld = focus;
            }

            if (!hasMap && dungeon != null && dungeon.PlacedRoomCount > 0)
                Rebuild();
            else
            {
                layoutDirty = true;
                UpdateTileVisibility();
            }

            UpdateControllerCursorVisual();
        }
        else if (controllerCursor != null)
            controllerCursor.gameObject.SetActive(false);
    }

    void HandlePan()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed)
            return;

        Vector2 pixelDelta = mouse.delta.ReadValue();
        if (pixelDelta.sqrMagnitude < 0.01f)
            return;

        float scaleFactor = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
        Vector2 canvasDelta = pixelDelta / scaleFactor;
        if (mapScale <= 0.0001f)
            return;

        focusPoint.x -= canvasDelta.x / mapScale;
        focusPoint.z -= canvasDelta.y / mapScale;
        layoutDirty = true;
    }

    void HandleZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        ApplyZoom(scroll > 0f ? 1f / zoomStep : zoomStep);
    }

    void HandleController()
    {
        Gamepad gamepad = Gamepad.all.Count > 0 ? Gamepad.all[0] : Gamepad.current;
        if (gamepad == null)
        {
            if (controllerCursor != null)
                controllerCursor.gameObject.SetActive(false);
            return;
        }

        if (controllerCursor != null)
            controllerCursor.gameObject.SetActive(true);

        Vector2 pan = gamepad.leftStick.ReadValue();
        if (pan.sqrMagnitude >= controllerStickDeadzone * controllerStickDeadzone)
        {
            float speed = controllerPanSpeed * Time.deltaTime;
            focusPoint.x += pan.x * speed / mapScale;
            focusPoint.z += pan.y * speed / mapScale;
            layoutDirty = true;
        }

        Vector2 cursor = gamepad.rightStick.ReadValue();
        if (cursor.sqrMagnitude >= controllerStickDeadzone * controllerStickDeadzone)
        {
            float speed = controllerCursorSpeed * Time.deltaTime;
            controllerCursorWorld.x += cursor.x * speed / mapScale;
            controllerCursorWorld.z += cursor.y * speed / mapScale;
            UpdateControllerCursorVisual();
        }

        float zoomAxis = gamepad.rightTrigger.ReadValue() - gamepad.leftTrigger.ReadValue();
        if (Mathf.Abs(zoomAxis) > 0.05f)
            ApplyZoom(1f + zoomAxis * controllerZoomSpeed * Time.deltaTime);

        if (gamepad.buttonSouth.wasPressedThisFrame)
            TryTeleportAtMapPoint(WorldToMap(controllerCursorWorld));

        if (gamepad.buttonEast.wasPressedThisFrame)
            SetOpen(false);
    }

    void ApplyZoom(float factor)
    {
        if (Mathf.Abs(factor - 1f) < 0.0001f)
            return;

        float min = Mathf.Min(minViewSize, maxViewSize);
        float max = Mathf.Max(minViewSize, maxViewSize);
        currentViewSize = Mathf.Clamp(currentViewSize * factor, min, max);
        ApplyMapScale();
        layoutDirty = true;
    }

    void ApplyMapScale()
    {
        float size = Mathf.Max(50f, currentViewSize);
        if (innerSize.x > 0.01f)
            mapScale = innerSize.x / size;
    }

    void Rebuild()
    {
        hasMap = false;
        tiles.Clear();
        padMarkers.Clear();
        markedPads.Clear();
        UnsubscribeEncounters();
        if (dungeon == null || mapContent == null)
            return;

        for (int i = mapContent.childCount - 1; i >= 0; i--)
            Destroy(mapContent.GetChild(i).gameObject);

        if (dungeon.PlacedRoomCount == 0)
            return;

        mapCenter = innerSize * 0.5f;
        ApplyMapScale();

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
        BuildPadMarkers();
        hasMap = true;
        layoutDirty = true;

        if (player != null)
            focusPoint = player.transform.position;

        RefreshView();
        UpdateTileVisibility();
    }

    void WatchEncounter(RoomDefinition room)
    {
        RoomEncounter encounter = DungeonMapLayout.GetEncounter(room);
        if (encounter == null || watchedEncounters.Contains(encounter))
            return;

        encounter.Cleared += UpdateTileVisibility;
        watchedEncounters.Add(encounter);
    }

    void UnsubscribeEncounters()
    {
        for (int i = 0; i < watchedEncounters.Count; i++)
        {
            RoomEncounter encounter = watchedEncounters[i];
            if (encounter == null)
                continue;

            encounter.Cleared -= UpdateTileVisibility;
        }

        watchedEncounters.Clear();
    }

    void UpdateTileVisibility()
    {
        DungeonMapLayout.CollectRevealedPassages(dungeon, revealedPassages);

        for (int i = 0; i < tiles.Count; i++)
        {
            MapTile tile = tiles[i];
            if (tile.Rect == null)
                continue;

            bool revealed = tile.Room != null
                ? DungeonMapLayout.IsRoomRevealed(tile.Room)
                : tile.Passage != null && revealedPassages.Contains(tile.Passage);
            tile.Rect.gameObject.SetActive(revealed);
        }

        for (int i = 0; i < padMarkers.Count; i++)
        {
            RectTransform marker = padMarkers[i];
            TeleportPad pad = markedPads[i];
            if (marker == null)
                continue;

            bool revealed = pad != null && DungeonMapLayout.IsRoomRevealed(pad.Room);
            marker.gameObject.SetActive(revealed);
        }
    }

    void RefreshView()
    {
        bool rebuildLayout = layoutDirty ||
                             !Mathf.Approximately(lastLayoutScale, mapScale) ||
                             (focusPoint - lastLayoutFocus).sqrMagnitude > 0.0001f;

        if (rebuildLayout)
        {
            layoutDirty = false;
            lastLayoutScale = mapScale;
            lastLayoutFocus = focusPoint;

            for (int i = 0; i < tiles.Count; i++)
            {
                MapTile tile = tiles[i];
                if (tile.Rect == null)
                    continue;

                Rect worldRect = tile.WorldRect;
                tile.Rect.anchoredPosition = WorldToMap(new Vector3(worldRect.xMin, 0f, worldRect.yMin));
                tile.Rect.sizeDelta = new Vector2(worldRect.width * mapScale, worldRect.height * mapScale);
            }

            for (int i = 0; i < padMarkers.Count; i++)
            {
                RectTransform marker = padMarkers[i];
                TeleportPad pad = markedPads[i];
                if (marker == null || pad == null)
                    continue;

                marker.anchoredPosition = WorldToMap(pad.transform.position);
            }
        }

        if (playerMarker != null && player != null)
        {
            playerMarker.anchoredPosition = WorldToMap(player.transform.position);
            playerMarker.SetAsLastSibling();
            playerMarker.localEulerAngles = new Vector3(0f, 0f, -player.transform.eulerAngles.y);
            if (playerHeading != null)
                playerHeading.localEulerAngles = Vector3.zero;
        }

        for (int i = 0; i < padMarkers.Count; i++)
        {
            RectTransform marker = padMarkers[i];
            if (marker != null)
                marker.SetAsLastSibling();
        }

        if (playerMarker != null)
            playerMarker.SetAsLastSibling();

        UpdateControllerCursorVisual();
    }

    void UpdateControllerCursorVisual()
    {
        if (controllerCursor == null)
            return;

        controllerCursor.anchoredPosition = WorldToMap(controllerCursorWorld);
        controllerCursor.SetAsLastSibling();
        if (playerMarker != null)
            playerMarker.SetAsLastSibling();
    }

    Vector2 WorldToMap(Vector3 worldPosition)
    {
        float x = (worldPosition.x - focusPoint.x) * mapScale;
        float y = (worldPosition.z - focusPoint.z) * mapScale;
        return mapCenter + new Vector2(x, y);
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

    void BuildOverlay()
    {
        Image dim = CreateImage("Map Overlay", transform, dimColor);
        overlayRoot = dim.gameObject;
        RectTransform dimRect = dim.rectTransform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;
        dim.raycastTarget = true;

        Image panel = CreateImage("Panel", overlayRoot.transform, panelColor);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = panelSize;
        panel.raycastTarget = true;

        Text title = CreateText("Title", panelRect, "MAP", 36, Color.white, TextAnchor.UpperLeft);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(24f, -16f);
        titleRect.sizeDelta = new Vector2(-48f, 44f);

        Text hint = CreateText(
            "Hint",
            panelRect,
            "LMB teleport    RMB pan    Scroll zoom    Tab close\n"
            + "Select map    LS pan    RS cursor    A teleport    B close    LT/RT zoom",
            18,
            new Color(0.75f, 0.78f, 0.82f),
            TextAnchor.UpperRight);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = new Vector2(1f, 1f);
        hintRect.pivot = new Vector2(1f, 1f);
        hintRect.anchoredPosition = new Vector2(-24f, -24f);
        hintRect.sizeDelta = new Vector2(-48f, 52f);

        Image frameImage = CreateImage("Map Frame", panelRect, new Color(0.04f, 0.045f, 0.05f, 1f));
        mapFrame = frameImage.rectTransform;
        RectTransform frameRect = mapFrame;
        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = new Vector2(20f, 20f);
        frameRect.offsetMax = new Vector2(-20f, -68f);
        mapFrame.gameObject.AddComponent<RectMask2D>();

        innerSize = new Vector2(panelSize.x - 40f, panelSize.y - 88f);

        mapContent = new GameObject("Map", typeof(RectTransform)).GetComponent<RectTransform>();
        mapContent.SetParent(frameRect, false);
        mapContent.anchorMin = Vector2.zero;
        mapContent.anchorMax = Vector2.one;
        mapContent.offsetMin = Vector2.zero;
        mapContent.offsetMax = Vector2.zero;
        mapContent.pivot = Vector2.zero;

        BuildControllerCursor();
    }

    void BuildControllerCursor()
    {
        Image marker = CreateImage("Controller Cursor", mapContent, new Color(1f, 0.95f, 0.35f, 1f));
        controllerCursor = marker.rectTransform;
        controllerCursor.anchorMin = Vector2.zero;
        controllerCursor.anchorMax = Vector2.zero;
        controllerCursor.pivot = new Vector2(0.5f, 0.5f);
        controllerCursor.sizeDelta = new Vector2(18f, 18f);
        controllerCursor.gameObject.SetActive(false);
    }

    void HandleTeleportClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;
        if (mouse.rightButton.isPressed)
            return;
        if (player == null || mapFrame == null || mapContent == null)
            return;

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector2 screen = mouse.position.ReadValue();
        if (!RectTransformUtility.RectangleContainsScreenPoint(mapFrame, screen, eventCamera))
            return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapContent, screen, eventCamera, out Vector2 local))
            return;

        TryTeleportAtMapPoint(local);
    }

    void TryTeleportAtMapPoint(Vector2 localPoint)
    {
        TeleportPad pad = FindPadAtMapPoint(localPoint);
        if (pad == null || player == null)
            return;

        player.TeleportTo(pad.ArrivePosition);
        SetOpen(false);
    }

    TeleportPad FindPadAtMapPoint(Vector2 localPoint)
    {
        // Prefer the room under the cursor so zoomed-out pad markers don't steal the target.
        TeleportPad roomPad = FindPadForRoomAtMapPoint(localPoint);
        if (roomPad != null)
            return roomPad;

        const float padHitSize = 22f;
        TeleportPad bestPad = null;
        float bestDistSqr = padHitSize * padHitSize;

        for (int i = 0; i < padMarkers.Count; i++)
        {
            RectTransform marker = padMarkers[i];
            TeleportPad pad = markedPads[i];
            if (marker == null || pad == null || !marker.gameObject.activeSelf)
                continue;

            Vector2 delta = localPoint - marker.anchoredPosition;
            float distSqr = delta.sqrMagnitude;
            if (distSqr > bestDistSqr)
                continue;

            bestDistSqr = distSqr;
            bestPad = pad;
        }

        return bestPad;
    }

    TeleportPad FindPadForRoomAtMapPoint(Vector2 localPoint)
    {
        TeleportPad bestPad = null;
        float bestArea = float.MaxValue;

        for (int i = 0; i < tiles.Count; i++)
        {
            MapTile tile = tiles[i];
            if (tile.Rect == null || tile.Room == null || !tile.Rect.gameObject.activeSelf)
                continue;

            Vector2 pos = tile.Rect.anchoredPosition;
            Vector2 size = tile.Rect.sizeDelta;
            if (localPoint.x < pos.x || localPoint.x > pos.x + size.x ||
                localPoint.y < pos.y || localPoint.y > pos.y + size.y)
                continue;

            TeleportPad pad = TeleportPad.FindForRoom(tile.Room);
            if (pad == null)
                continue;

            // If several room rects overlap, pick the smallest containing one.
            float area = Mathf.Abs(size.x * size.y);
            if (area >= bestArea)
                continue;

            bestArea = area;
            bestPad = pad;
        }

        return bestPad;
    }

    void BuildPadMarkers()
    {
        padMarkers.Clear();
        markedPads.Clear();

        IReadOnlyList<TeleportPad> pads = TeleportPad.ActivePads;
        for (int i = 0; i < pads.Count; i++)
        {
            TeleportPad pad = pads[i];
            if (pad == null)
                continue;

            Image marker = CreateImage("Teleport Pad", mapContent, padColor);
            RectTransform rect = marker.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(12f, 12f);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            rect.anchoredPosition = WorldToMap(pad.transform.position);

            padMarkers.Add(rect);
            markedPads.Add(pad);
        }
    }

    void BuildPlayerMarker()
    {
        Image marker = CreateImage("Player", mapContent, playerColor);
        playerMarker = marker.rectTransform;
        playerMarker.anchorMin = Vector2.zero;
        playerMarker.anchorMax = Vector2.zero;
        playerMarker.pivot = new Vector2(0.5f, 0.5f);
        playerMarker.sizeDelta = new Vector2(14f, 14f);
        playerMarker.SetAsLastSibling();

        Image heading = CreateImage("Heading", playerMarker, playerColor);
        playerHeading = heading.rectTransform;
        playerHeading.anchorMin = new Vector2(0.5f, 0.5f);
        playerHeading.anchorMax = new Vector2(0.5f, 0.5f);
        playerHeading.pivot = new Vector2(0.5f, 0f);
        playerHeading.anchoredPosition = Vector2.zero;
        playerHeading.sizeDelta = new Vector2(5f, 14f);
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

    Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        return text;
    }
}
