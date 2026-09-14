using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Screen-space health / weapon panels and game over banner.
/// Supports one or two local players (shared-camera co-op).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Health Bar")]
    [SerializeField] Vector2 barSize = new Vector2(320f, 26f);
    [SerializeField] Vector2 barMargin = new Vector2(24f, 24f);
    [SerializeField] Color healthyColor = new Color(0.29f, 0.85f, 0.39f);
    [SerializeField] Color criticalColor = new Color(0.9f, 0.22f, 0.22f);

    [Header("Game Over")]
    [SerializeField] string gameOverMessage = "GAME OVER";
    [SerializeField] Color gameOverColor = new Color(0.95f, 0.26f, 0.26f);
    [SerializeField] string restartButtonLabel = "RESTART";
    [SerializeField] Color restartButtonColor = new Color(0.18f, 0.18f, 0.2f, 0.95f);
    [SerializeField] Color restartLabelColor = Color.white;

    [Header("Room Modifier Banner")]
    [SerializeField] float modifierBannerDuration = 3.25f;

    sealed class PlayerPanel
    {
        public PlayerController Player;
        public RectTransform HealthFill;
        public Image HealthFillImage;
        public Text HealthLabel;
        public Text WeaponLabel;
        public Text TitleLabel;
        public GameObject HealthRoot;
        public GameObject WeaponRoot;
        public System.Action<float, float> HealthHandler;
        public System.Action<string> WeaponHandler;
        public bool Bound;
    }

    readonly List<PlayerPanel> panels = new List<PlayerPanel>(4);
    Transform panelsRoot;
    Text infectionReportLabel;
    GameObject gameOverRoot;
    GameObject modifierBannerRoot;
    RectTransform modifierBannerRect;
    Text modifierBannerLabel;
    Text modifierBannerDetailLabel;
    Button restartButton;
    Font font;
    bool isGameOver;
    float modifierBannerHideAt = -1f;
    string pendingCriticalMegaName = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapAfterSceneLoad()
    {
        EnsureExists();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureExists();
    }

    public static void EnsureExists()
    {
        if (PlayerRegistry.Count == 0 && FindAnyObjectByType<PlayerController>() == null)
            return;

        if (FindAnyObjectByType<PlayerHUD>() == null)
        {
            var hud = new GameObject("Player HUD", typeof(Canvas));
            hud.AddComponent<PlayerHUD>();
            hud.AddComponent<DungeonMinimap>();
            hud.AddComponent<DungeonMapOverlay>();
        }

        TutorialTipsUI.EnsureExists();
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        panelsRoot = new GameObject("Player Panels", typeof(RectTransform)).transform;
        panelsRoot.SetParent(transform, false);
        StretchFull((RectTransform)panelsRoot);
        BuildModifierBanner();
        BuildGameOverBanner();
    }

    void OnEnable()
    {
        RebuildForAllPlayers();
        RoomEncounter.ModifierEncounterStarted -= OnModifierEncounterStarted;
        RoomEncounter.ModifierEncounterStarted += OnModifierEncounterStarted;
        RoomEncounter.CriticalProcessTelegraphStarted -= OnCriticalProcessTelegraphStarted;
        RoomEncounter.CriticalProcessTelegraphStarted += OnCriticalProcessTelegraphStarted;
        RoomEncounter.CriticalProcessTelegraphEnded -= OnCriticalProcessTelegraphEnded;
        RoomEncounter.CriticalProcessTelegraphEnded += OnCriticalProcessTelegraphEnded;
    }

    void Start()
    {
        RebuildForAllPlayers();
    }

    void OnDisable()
    {
        RoomEncounter.ModifierEncounterStarted -= OnModifierEncounterStarted;
        RoomEncounter.CriticalProcessTelegraphStarted -= OnCriticalProcessTelegraphStarted;
        RoomEncounter.CriticalProcessTelegraphEnded -= OnCriticalProcessTelegraphEnded;
        UnbindAllPanels();
    }

    public void RebuildForAllPlayers()
    {
        UnbindAllPanels();
        ClearPanelVisuals();

        IReadOnlyList<PlayerController> players = PlayerRegistry.All;
        if (players.Count == 0)
        {
            PlayerController fallback = FindAnyObjectByType<PlayerController>();
            if (fallback != null)
                CreatePanelForPlayer(fallback, 0);
        }
        else
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                    CreatePanelForPlayer(players[i], i);
            }
        }

        RefreshGameOverState();
    }

    void CreatePanelForPlayer(PlayerController player, int layoutIndex)
    {
        bool alignRight = layoutIndex > 0;
        float x = alignRight ? -barMargin.x : barMargin.x;
        float pivotX = alignRight ? 1f : 0f;
        float anchorX = alignRight ? 1f : 0f;
        // Keep P2 health under the top-right minimap.
        float healthY = alignRight ? -(barMargin.y + 276f) : -barMargin.y;

        var panel = new PlayerPanel { Player = player };

        RectTransform healthBg = CreateImage($"Health Bar P{player.PlayerIndex + 1}", panelsRoot, new Color(0f, 0f, 0f, 0.6f)).rectTransform;
        healthBg.anchorMin = new Vector2(anchorX, 1f);
        healthBg.anchorMax = new Vector2(anchorX, 1f);
        healthBg.pivot = new Vector2(pivotX, 1f);
        healthBg.anchoredPosition = new Vector2(x, healthY);
        healthBg.sizeDelta = barSize;
        panel.HealthRoot = healthBg.gameObject;

        panel.TitleLabel = CreateText("Title", healthBg, $"P{player.PlayerIndex + 1}", 14, new Color(1f, 1f, 1f, 0.75f),
            alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        RectTransform titleRect = panel.TitleLabel.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 0f);
        titleRect.anchoredPosition = new Vector2(0f, 2f);
        titleRect.sizeDelta = new Vector2(0f, 18f);
        titleRect.offsetMin = new Vector2(6f, titleRect.offsetMin.y);
        titleRect.offsetMax = new Vector2(-6f, titleRect.offsetMax.y);

        panel.HealthFillImage = CreateImage("Fill", healthBg, healthyColor);
        panel.HealthFill = panel.HealthFillImage.rectTransform;
        panel.HealthFill.anchorMin = Vector2.zero;
        panel.HealthFill.anchorMax = Vector2.one;
        panel.HealthFill.offsetMin = new Vector2(3f, 3f);
        panel.HealthFill.offsetMax = new Vector2(-3f, -3f);

        panel.HealthLabel = CreateText("Amount", healthBg, string.Empty, 18, Color.white, TextAnchor.MiddleCenter);
        StretchFull(panel.HealthLabel.rectTransform);

        RectTransform weaponBg = CreateImage($"Weapon P{player.PlayerIndex + 1}", panelsRoot, new Color(0f, 0f, 0f, 0.55f)).rectTransform;
        weaponBg.anchorMin = new Vector2(anchorX, 0f);
        weaponBg.anchorMax = new Vector2(anchorX, 0f);
        weaponBg.pivot = new Vector2(pivotX, 0f);
        weaponBg.anchoredPosition = new Vector2(x, barMargin.y);
        weaponBg.sizeDelta = new Vector2(280f, 36f);
        panel.WeaponRoot = weaponBg.gameObject;

        panel.WeaponLabel = CreateText("Name", weaponBg, string.Empty, 20, Color.white,
            alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        RectTransform weaponLabelRect = panel.WeaponLabel.rectTransform;
        StretchFull(weaponLabelRect);
        weaponLabelRect.offsetMin = new Vector2(12f, 0f);
        weaponLabelRect.offsetMax = new Vector2(-8f, 0f);

        panel.HealthHandler = (current, max) => OnPanelHealthChanged(panel, current, max);
        panel.WeaponHandler = weaponName => OnPanelWeaponChanged(panel, weaponName);
        player.HealthChanged += panel.HealthHandler;
        player.Died += OnAnyPlayerDied;
        player.WeaponChanged += panel.WeaponHandler;
        panel.Bound = true;

        OnPanelHealthChanged(panel, player.CurrentHealth, player.MaxHealth);
        OnPanelWeaponChanged(panel, player.CurrentWeaponName);
        panels.Add(panel);
    }

    void UnbindAllPanels()
    {
        for (int i = 0; i < panels.Count; i++)
        {
            PlayerPanel panel = panels[i];
            if (!panel.Bound || panel.Player == null)
                continue;

            if (panel.HealthHandler != null)
                panel.Player.HealthChanged -= panel.HealthHandler;
            if (panel.WeaponHandler != null)
                panel.Player.WeaponChanged -= panel.WeaponHandler;
            panel.Player.Died -= OnAnyPlayerDied;
            panel.Bound = false;
        }

        panels.Clear();
    }

    void ClearPanelVisuals()
    {
        if (panelsRoot == null)
            return;

        for (int i = panelsRoot.childCount - 1; i >= 0; i--)
            Destroy(panelsRoot.GetChild(i).gameObject);
    }

    void Update()
    {
        if (modifierBannerRoot != null &&
            modifierBannerRoot.activeSelf &&
            modifierBannerHideAt >= 0f &&
            Time.unscaledTime >= modifierBannerHideAt)
        {
            HideModifierBanner();
        }

        if (!isGameOver)
            return;

        if (WasRestartPressed())
            RestartRun();
    }

    void OnCriticalProcessTelegraphStarted(string megaName)
    {
        pendingCriticalMegaName = string.IsNullOrEmpty(megaName) ? "MEGA" : megaName;
        ShowModifierBanner(
            "CRITICAL PROCESS",
            pendingCriticalMegaName.ToUpperInvariant(),
            holdUntilHidden: true);
    }

    void OnCriticalProcessTelegraphEnded()
    {
        HideModifierBanner();
    }

    void OnModifierEncounterStarted(RoomModifierType modifier)
    {
        if (modifier == RoomModifierType.None || modifierBannerRoot == null || modifierBannerLabel == null)
            return;

        string title = RoomEncounter.GetModifierDisplayName(modifier).ToUpperInvariant();
        string detail = string.Empty;
        if (modifier == RoomModifierType.CriticalProcess && !string.IsNullOrEmpty(pendingCriticalMegaName))
            detail = pendingCriticalMegaName.ToUpperInvariant();

        ShowModifierBanner(title, detail, holdUntilHidden: false);
    }

    void ShowModifierBanner(string title, string detail, bool holdUntilHidden)
    {
        if (modifierBannerRoot == null || modifierBannerLabel == null)
            return;

        modifierBannerLabel.text = title ?? string.Empty;
        bool hasDetail = !string.IsNullOrEmpty(detail);
        if (modifierBannerDetailLabel != null)
        {
            modifierBannerDetailLabel.gameObject.SetActive(hasDetail);
            modifierBannerDetailLabel.text = hasDetail ? detail : string.Empty;
        }

        if (modifierBannerLabel != null)
        {
            RectTransform labelRect = modifierBannerLabel.rectTransform;
            if (hasDetail)
            {
                labelRect.anchorMin = new Vector2(0f, 0.42f);
                labelRect.anchorMax = new Vector2(1f, 1f);
            }
            else
            {
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
            }

            labelRect.offsetMin = new Vector2(16f, 0f);
            labelRect.offsetMax = new Vector2(-16f, 0f);
        }

        if (modifierBannerRect != null)
            modifierBannerRect.sizeDelta = new Vector2(460f, hasDetail ? 72f : 48f);

        modifierBannerRoot.SetActive(true);
        modifierBannerHideAt = holdUntilHidden
            ? -1f
            : Time.unscaledTime + Mathf.Max(0.5f, modifierBannerDuration);
    }

    void HideModifierBanner()
    {
        modifierBannerHideAt = -1f;
        if (modifierBannerRoot != null)
            modifierBannerRoot.SetActive(false);

        if (modifierBannerDetailLabel != null)
        {
            modifierBannerDetailLabel.text = string.Empty;
            modifierBannerDetailLabel.gameObject.SetActive(false);
        }

        if (modifierBannerRect != null)
            modifierBannerRect.sizeDelta = new Vector2(420f, 48f);
    }

    void OnPanelHealthChanged(PlayerPanel panel, float current, float max)
    {
        if (panel == null || panel.HealthFill == null || panel.HealthFillImage == null || panel.HealthLabel == null)
            return;

        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        panel.HealthFill.anchorMax = new Vector2(fraction, 1f);
        panel.HealthFillImage.enabled = fraction > 0f;
        panel.HealthFillImage.color = Color.Lerp(criticalColor, healthyColor, fraction);
        panel.HealthLabel.text = $"{FormatHealth(current)} / {FormatHealth(max)}";
    }

    static string FormatHealth(float value)
    {
        if (Mathf.Approximately(value, Mathf.Round(value)))
            return Mathf.RoundToInt(value).ToString();

        return value.ToString("0.#");
    }

    static void OnPanelWeaponChanged(PlayerPanel panel, string weaponName)
    {
        if (panel?.WeaponLabel == null)
            return;

        panel.WeaponLabel.text = string.IsNullOrEmpty(weaponName) ? string.Empty : weaponName.ToUpperInvariant();
    }

    void BuildModifierBanner()
    {
        Image background = CreateImage("Room Modifier Banner", transform, new Color(0.05f, 0.08f, 0.09f, 0.88f));
        modifierBannerRect = background.rectTransform;
        modifierBannerRect.anchorMin = new Vector2(0.5f, 1f);
        modifierBannerRect.anchorMax = new Vector2(0.5f, 1f);
        modifierBannerRect.pivot = new Vector2(0.5f, 1f);
        modifierBannerRect.anchoredPosition = new Vector2(0f, -22f);
        modifierBannerRect.sizeDelta = new Vector2(420f, 48f);

        modifierBannerLabel = CreateText(
            "Name",
            modifierBannerRect,
            string.Empty,
            26,
            new Color(0.55f, 0.95f, 0.78f),
            TextAnchor.MiddleCenter);
        modifierBannerLabel.fontStyle = FontStyle.Bold;
        RectTransform labelRect = modifierBannerLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.42f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = new Vector2(-16f, 0f);

        modifierBannerDetailLabel = CreateText(
            "Detail",
            modifierBannerRect,
            string.Empty,
            20,
            new Color(0.95f, 0.82f, 0.35f),
            TextAnchor.MiddleCenter);
        modifierBannerDetailLabel.fontStyle = FontStyle.Bold;
        RectTransform detailRect = modifierBannerDetailLabel.rectTransform;
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 0.5f);
        detailRect.offsetMin = new Vector2(16f, 4f);
        detailRect.offsetMax = new Vector2(-16f, 0f);
        modifierBannerDetailLabel.gameObject.SetActive(false);

        modifierBannerRoot = background.gameObject;
        modifierBannerRoot.SetActive(false);
    }

    void OnAnyPlayerDied()
    {
        RefreshGameOverState();
    }

    void RefreshGameOverState()
    {
        if (!PlayerRegistry.AllDead)
        {
            if (isGameOver)
                SetGameOverVisible(false);
            return;
        }

        HideModifierBanner();
        RefreshInfectionReport();
        SetGameOverVisible(true);
    }

    void RefreshInfectionReport()
    {
        if (infectionReportLabel == null)
            return;

        infectionReportLabel.text = InfectionReport.FinalizeAndFormat();
    }

    void SetGameOverVisible(bool visible)
    {
        isGameOver = visible;
        if (gameOverRoot != null)
            gameOverRoot.SetActive(visible);

        if (visible)
            RefreshInfectionReport();

        if (!visible || restartButton == null)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(restartButton.gameObject);
    }

    void RestartRun()
    {
        Time.timeScale = 1f;
        CleanupTransientUi();

        Scene active = SceneManager.GetActiveScene();
        if (active.buildIndex >= 0)
            SceneManager.LoadScene(active.buildIndex);
        else
            SceneManager.LoadScene(active.name);
    }

    static void CleanupTransientUi()
    {
        PacketLossCombatEffect.Reset();
        InfectionReport.BeginRun();

        EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < eventSystems.Length; i++)
        {
            if (eventSystems[i] != null)
                Destroy(eventSystems[i].gameObject);
        }
    }

    static bool WasRestartPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame ||
             keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.rKey.wasPressedThisFrame))
        {
            return true;
        }

        for (int i = 0; i < Gamepad.all.Count; i++)
        {
            Gamepad gamepad = Gamepad.all[i];
            if (gamepad != null &&
                (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame))
            {
                return true;
            }
        }

        return false;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    void BuildCanvas()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
    }

    void BuildGameOverBanner()
    {
        Image dim = CreateImage("Game Over", transform, new Color(0f, 0f, 0f, 0.72f));
        dim.raycastTarget = true;
        RectTransform dimRect = dim.rectTransform;
        StretchFull(dimRect);

        Text label = CreateText("Message", dimRect, gameOverMessage, 72, gameOverColor, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyle.Bold;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.72f);
        labelRect.anchorMax = new Vector2(1f, 0.9f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Image reportPanel = CreateImage("Infection Report Panel", dimRect, new Color(0.05f, 0.07f, 0.08f, 0.92f));
        RectTransform reportPanelRect = reportPanel.rectTransform;
        reportPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        reportPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        reportPanelRect.pivot = new Vector2(0.5f, 0.5f);
        reportPanelRect.sizeDelta = new Vector2(560f, 320f);
        reportPanelRect.anchoredPosition = new Vector2(0f, 28f);

        infectionReportLabel = CreateText(
            "Infection Report",
            reportPanelRect,
            "INFECTION REPORT",
            22,
            new Color(0.78f, 0.95f, 0.82f),
            TextAnchor.UpperLeft);
        infectionReportLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        infectionReportLabel.verticalOverflow = VerticalWrapMode.Overflow;
        infectionReportLabel.lineSpacing = 1.05f;
        RectTransform reportRect = infectionReportLabel.rectTransform;
        StretchFull(reportRect);
        reportRect.offsetMin = new Vector2(28f, 18f);
        reportRect.offsetMax = new Vector2(-28f, -22f);

        restartButton = CreateButton("Restart", dimRect, restartButtonLabel, restartButtonColor, restartLabelColor);
        RectTransform buttonRect = restartButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.18f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.18f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(280f, 64f);
        buttonRect.anchoredPosition = Vector2.zero;
        restartButton.onClick.AddListener(RestartRun);

        Text hint = CreateText(
            "Hint",
            dimRect,
            "Click Restart  ·  Enter / Space / A",
            22,
            new Color(1f, 1f, 1f, 0.7f),
            TextAnchor.MiddleCenter);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0.06f);
        hintRect.anchorMax = new Vector2(1f, 0.12f);
        hintRect.offsetMin = Vector2.zero;
        hintRect.offsetMax = Vector2.zero;

        gameOverRoot = dim.gameObject;
        gameOverRoot.SetActive(false);
    }

    Button CreateButton(string name, Transform parent, string label, Color backgroundColor, Color labelColor)
    {
        Image background = CreateImage(name, parent, backgroundColor);
        background.raycastTarget = true;

        var button = background.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.selectedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        button.colors = colors;
        button.targetGraphic = background;

        Text text = CreateText("Label", background.transform, label, 28, labelColor, TextAnchor.MiddleCenter);
        text.fontStyle = FontStyle.Bold;
        StretchFull(text.rectTransform);

        return button;
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

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
