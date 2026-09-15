using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runtime-built main menu: Play, Quit, room-modifier / enemy catalogs, and Settings.
/// Matches the infection-theme uGUI style used by <see cref="TutorialTipsUI"/>.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class MainMenuUI : MonoBehaviour
{
    public const string SceneName = "MainMenu";
    public const string GameplaySceneName = "Gameplay";

    static readonly Color Accent = new Color(0.45f, 0.95f, 0.7f);
    static readonly Color PanelBg = new Color(0.06f, 0.08f, 0.09f, 0.96f);
    static readonly Color DimBg = new Color(0.02f, 0.04f, 0.045f, 0.92f);
    static readonly Color ButtonPrimary = new Color(0.12f, 0.45f, 0.32f, 0.98f);
    static readonly Color ButtonSecondary = new Color(0.14f, 0.28f, 0.38f, 0.98f);
    static readonly Color ButtonMuted = new Color(0.16f, 0.18f, 0.2f, 0.95f);
    static readonly Color BodyText = new Color(0.9f, 0.93f, 0.9f);

    Font font;

    GameObject mainTab;
    GameObject modifiersTab;
    GameObject enemiesTab;
    GameObject settingsTab;

    Button playButton;
    Button modifiersBackButton;
    Button enemiesBackButton;
    Button settingsBackButton;

    Text modifierDetailTitle;
    Text modifierDetailBody;
    Text enemyDetailTitle;
    Text enemyDetailBody;
    readonly List<Button> modifierNameButtons = new List<Button>();
    readonly List<Button> enemyNameButtons = new List<Button>();
    int selectedModifierIndex;
    int selectedEnemyIndex;

    GameObject settingsControlsPanel;
    GameObject settingsDisplayPanel;
    GameObject settingsQualityPanel;
    Button settingsControlsTabButton;
    Button settingsDisplayTabButton;
    Button settingsQualityTabButton;
    Text controlsBody;
    Text resolutionLabel;
    Text fullscreenLabel;
    Text qualityLabel;
    readonly List<Resolution> uniqueResolutions = new List<Resolution>();
    int resolutionIndex;

    enum View
    {
        Main,
        Modifiers,
        Enemies,
        Settings
    }

    View currentView = View.Main;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != SceneName)
            return;

        EnsureExists();
    }

    public static void EnsureExists()
    {
        if (FindAnyObjectByType<MainMenuUI>() != null)
            return;

        var go = new GameObject("Main Menu UI", typeof(Canvas));
        go.AddComponent<MainMenuUI>();
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        CacheResolutions();
        BuildMainTab();
        BuildModifiersTab();
        BuildEnemiesTab();
        BuildSettingsTab();
        ShowMain();
    }

    void Update()
    {
        if (!WasCancelPressed())
            return;

        switch (currentView)
        {
            case View.Modifiers:
            case View.Enemies:
            case View.Settings:
                ShowMain();
                break;
        }
    }

    void PlayGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(GameplaySceneName);
    }

    void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void ShowMain()
    {
        currentView = View.Main;
        SetTabActive(mainTab, true);
        SetTabActive(modifiersTab, false);
        SetTabActive(enemiesTab, false);
        SetTabActive(settingsTab, false);
        SelectPrimaryButton(playButton);
    }

    void ShowModifiers()
    {
        currentView = View.Modifiers;
        SetTabActive(mainTab, false);
        SetTabActive(modifiersTab, true);
        SetTabActive(enemiesTab, false);
        SetTabActive(settingsTab, false);
        SelectModifier(0);
        if (modifierNameButtons.Count > 0)
            SelectPrimaryButton(modifierNameButtons[0]);
        else
            SelectPrimaryButton(modifiersBackButton);
    }

    void ShowEnemies()
    {
        currentView = View.Enemies;
        SetTabActive(mainTab, false);
        SetTabActive(modifiersTab, false);
        SetTabActive(enemiesTab, true);
        SetTabActive(settingsTab, false);
        SelectEnemy(0);
        if (enemyNameButtons.Count > 0)
            SelectPrimaryButton(enemyNameButtons[0]);
        else
            SelectPrimaryButton(enemiesBackButton);
    }

    void ShowSettings()
    {
        currentView = View.Settings;
        SetTabActive(mainTab, false);
        SetTabActive(modifiersTab, false);
        SetTabActive(enemiesTab, false);
        SetTabActive(settingsTab, true);
        ShowSettingsSection(0);
        RefreshSettingsLabels();
        SelectPrimaryButton(settingsControlsTabButton);
    }

    void ShowSettingsSection(int section)
    {
        SetTabActive(settingsControlsPanel, section == 0);
        SetTabActive(settingsDisplayPanel, section == 1);
        SetTabActive(settingsQualityPanel, section == 2);
        HighlightCatalogButtons(
            new List<Button> { settingsControlsTabButton, settingsDisplayTabButton, settingsQualityTabButton },
            section);
        if (section == 0)
            RefreshControlsText();
    }

    void SelectModifier(int index)
    {
        if (modifierDetailTitle == null || modifierDetailBody == null)
            return;

        selectedModifierIndex = Mathf.Clamp(index, 0, GameCatalogData.Modifiers.Length - 1);
        GameCatalogData.ModifierInfo info = GameCatalogData.Modifiers[selectedModifierIndex];
        modifierDetailTitle.text = info.Name.ToUpperInvariant();
        modifierDetailBody.text = info.Description;
        HighlightCatalogButtons(modifierNameButtons, selectedModifierIndex);
    }

    void SelectEnemy(int index)
    {
        if (enemyDetailTitle == null || enemyDetailBody == null)
            return;

        selectedEnemyIndex = Mathf.Clamp(index, 0, GameCatalogData.Enemies.Length - 1);
        GameCatalogData.EnemyInfo info = GameCatalogData.Enemies[selectedEnemyIndex];
        enemyDetailTitle.text = info.Name.ToUpperInvariant();
        enemyDetailBody.text = info.Description;
        HighlightCatalogButtons(enemyNameButtons, selectedEnemyIndex);
    }

    void CacheResolutions()
    {
        uniqueResolutions.Clear();
        Resolution[] all = Screen.resolutions;
        var seen = new HashSet<string>();
        for (int i = 0; i < all.Length; i++)
        {
            Resolution r = all[i];
            string key = $"{r.width}x{r.height}";
            if (!seen.Add(key))
                continue;
            uniqueResolutions.Add(r);
        }

        if (uniqueResolutions.Count == 0)
        {
            uniqueResolutions.Add(new Resolution
            {
                width = Screen.width,
                height = Screen.height,
                refreshRateRatio = Screen.currentResolution.refreshRateRatio
            });
        }

        resolutionIndex = 0;
        for (int i = 0; i < uniqueResolutions.Count; i++)
        {
            Resolution r = uniqueResolutions[i];
            if (r.width == Screen.width && r.height == Screen.height)
            {
                resolutionIndex = i;
                break;
            }
        }
    }

    void CycleResolution(int delta)
    {
        if (uniqueResolutions.Count == 0)
            return;

        resolutionIndex = (resolutionIndex + delta + uniqueResolutions.Count) % uniqueResolutions.Count;
        Resolution r = uniqueResolutions[resolutionIndex];
        int refresh = Mathf.RoundToInt((float)r.refreshRateRatio.value);
        if (refresh <= 0)
            refresh = GameSettingsStore.GetCurrentRefreshRate();
        GameSettingsStore.SetResolution(r.width, r.height, refresh);
        RefreshSettingsLabels();
    }

    void ToggleFullscreen()
    {
        bool next = !GameSettingsStore.GetSavedFullscreen();
        GameSettingsStore.SetFullscreen(next);
        RefreshSettingsLabels();
    }

    void CycleQuality(int delta)
    {
        string[] names = QualitySettings.names;
        if (names == null || names.Length == 0)
            return;

        int index = (GameSettingsStore.GetSavedQuality() + delta + names.Length) % names.Length;
        GameSettingsStore.SetQuality(index);
        RefreshSettingsLabels();
    }

    void RefreshSettingsLabels()
    {
        if (resolutionLabel != null)
        {
            if (uniqueResolutions.Count > 0)
            {
                Resolution r = uniqueResolutions[Mathf.Clamp(resolutionIndex, 0, uniqueResolutions.Count - 1)];
                resolutionLabel.text = $"RESOLUTION  {r.width} x {r.height}";
            }
            else
            {
                resolutionLabel.text = $"RESOLUTION  {Screen.width} x {Screen.height}";
            }
        }

        if (fullscreenLabel != null)
            fullscreenLabel.text = GameSettingsStore.GetSavedFullscreen() ? "MODE  FULLSCREEN" : "MODE  WINDOWED";

        if (qualityLabel != null)
        {
            string[] names = QualitySettings.names;
            int q = GameSettingsStore.GetSavedQuality();
            string name = names != null && q >= 0 && q < names.Length ? names[q] : "Default";
            qualityLabel.text = $"QUALITY  {name.ToUpperInvariant()}";
        }
    }

    void RefreshControlsText()
    {
        if (controlsBody == null)
            return;

        var sb = new StringBuilder(512);
        sb.AppendLine("CURRENT BINDINGS");
        sb.AppendLine();

        bool appendedFromInput = TryAppendInputSystemBindings(sb);
        if (!appendedFromInput)
        {
            sb.AppendLine("Move - WASD / Left Stick");
            sb.AppendLine("Aim - Mouse / Right Stick");
            sb.AppendLine("Shoot - Left Click / Right Shoulder");
            sb.AppendLine("Dash - Left Shift / Right Trigger");
        }

        sb.AppendLine();
        sb.AppendLine("ALSO IN-GAME");
        sb.AppendLine("Weapon Switch - Scroll Wheel / 1-4 / D-pad");
        sb.AppendLine("Map - Tab / Select");
        sb.AppendLine("Help - H");
        sb.AppendLine("Pause - Esc / Start");
        sb.AppendLine();
        sb.Append("Rebinding is not available in this build - bindings follow the Input System asset and gameplay extras above.");

        controlsBody.text = sb.ToString();
    }

    static bool TryAppendInputSystemBindings(StringBuilder sb)
    {
        InputActionAsset actions = InputSystem.actions;
        if (actions == null)
            return false;

        InputActionMap map = actions.FindActionMap("Player", throwIfNotFound: false);
        if (map == null)
            return false;

        string[] focus = { "Move", "Look", "Attack", "Dash" };
        string[] labels = { "Move", "Aim / Look", "Shoot / Attack", "Dash" };
        bool any = false;
        for (int i = 0; i < focus.Length; i++)
        {
            InputAction action = map.FindAction(focus[i], throwIfNotFound: false);
            if (action == null)
                continue;

            string bindings = action.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontUseShortDisplayNames);
            if (string.IsNullOrWhiteSpace(bindings))
                bindings = action.GetBindingDisplayString();
            if (string.IsNullOrWhiteSpace(bindings))
                continue;

            sb.Append(labels[i]).Append(" - ").AppendLine(bindings);
            any = true;
        }

        return any;
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

        Image backdrop = CreateImage("Backdrop", transform, DimBg);
        backdrop.raycastTarget = true;
        StretchFull(backdrop.rectTransform);
    }

    void BuildMainTab()
    {
        Image panel = CreateImage("Main Tab", transform, PanelBg);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 620f);
        mainTab = panel.gameObject;

        Text brand = CreateText("Brand", panelRect, "COMPUTER VIRUS", 46, Accent, TextAnchor.UpperCenter);
        brand.fontStyle = FontStyle.Bold;
        RectTransform brandRect = brand.rectTransform;
        brandRect.anchorMin = new Vector2(0f, 1f);
        brandRect.anchorMax = new Vector2(1f, 1f);
        brandRect.pivot = new Vector2(0.5f, 1f);
        brandRect.anchoredPosition = new Vector2(0f, -36f);
        brandRect.sizeDelta = new Vector2(-40f, 52f);

        Text subtitle = CreateText("Subtitle", panelRect, "FIGHTING GAME", 28, new Color(0.75f, 0.9f, 0.82f), TextAnchor.UpperCenter);
        subtitle.fontStyle = FontStyle.Bold;
        RectTransform subRect = subtitle.rectTransform;
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.anchoredPosition = new Vector2(0f, -88f);
        subRect.sizeDelta = new Vector2(-40f, 34f);

        Text tagline = CreateText(
            "Tagline",
            panelRect,
            "Terminate hostile processes.\nClear the infected host.",
            20,
            new Color(1f, 1f, 1f, 0.65f),
            TextAnchor.UpperCenter);
        RectTransform tagRect = tagline.rectTransform;
        tagRect.anchorMin = new Vector2(0f, 1f);
        tagRect.anchorMax = new Vector2(1f, 1f);
        tagRect.pivot = new Vector2(0.5f, 1f);
        tagRect.anchoredPosition = new Vector2(0f, -128f);
        tagRect.sizeDelta = new Vector2(-48f, 56f);

        playButton = CreateMenuButton(panelRect, "Play", "PLAY", ButtonPrimary, 0);
        playButton.onClick.AddListener(PlayGame);

        Button modifiersButton = CreateMenuButton(panelRect, "Modifiers", "ROOM MODIFIERS", ButtonSecondary, 1);
        modifiersButton.onClick.AddListener(ShowModifiers);

        Button enemiesButton = CreateMenuButton(panelRect, "Enemies", "ENEMIES", ButtonSecondary, 2);
        enemiesButton.onClick.AddListener(ShowEnemies);

        Button settingsButton = CreateMenuButton(panelRect, "Settings", "SETTINGS", ButtonSecondary, 3);
        settingsButton.onClick.AddListener(ShowSettings);

        Button quitButton = CreateMenuButton(panelRect, "Quit", "QUIT", ButtonMuted, 4);
        quitButton.onClick.AddListener(QuitGame);

        Text hint = CreateText(
            "Hint",
            panelRect,
            "Esc closes catalog / settings panels",
            16,
            new Color(1f, 1f, 1f, 0.4f),
            TextAnchor.LowerCenter);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 18f);
        hintRect.sizeDelta = new Vector2(-40f, 24f);
    }

    Button CreateMenuButton(RectTransform parent, string name, string label, Color color, int index)
    {
        Button button = CreateButton(name, parent, label, color, Color.white);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -210f - index * 68f);
        rect.sizeDelta = new Vector2(360f, 54f);
        return button;
    }

    void BuildModifiersTab()
    {
        Image panel = CreateImage("Modifiers Tab", transform, PanelBg);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(920f, 560f);
        modifiersTab = panel.gameObject;

        Text title = CreateText("Title", modifiersTab.transform, "ROOM MODIFIERS", 36, Accent, TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        titleRect.sizeDelta = new Vector2(-40f, 44f);

        Image listPanel = CreateImage("Name List", modifiersTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform listRect = listPanel.rectTransform;
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(0f, 1f);
        listRect.pivot = new Vector2(0f, 0.5f);
        listRect.anchoredPosition = new Vector2(24f, -8f);
        listRect.sizeDelta = new Vector2(280f, -100f);
        listRect.offsetMin = new Vector2(24f, 70f);
        listRect.offsetMax = new Vector2(304f, -72f);

        Image detailPanel = CreateImage("Description", modifiersTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform detailRect = detailPanel.rectTransform;
        detailRect.offsetMin = new Vector2(324f, 70f);
        detailRect.offsetMax = new Vector2(-24f, -72f);

        modifierDetailTitle = CreateText("Detail Title", detailPanel.transform, string.Empty, 28, Accent, TextAnchor.UpperLeft);
        modifierDetailTitle.fontStyle = FontStyle.Bold;
        RectTransform detailTitleRect = modifierDetailTitle.rectTransform;
        detailTitleRect.anchorMin = new Vector2(0f, 1f);
        detailTitleRect.anchorMax = new Vector2(1f, 1f);
        detailTitleRect.pivot = new Vector2(0f, 1f);
        detailTitleRect.anchoredPosition = new Vector2(20f, -18f);
        detailTitleRect.sizeDelta = new Vector2(-40f, 36f);

        modifierDetailBody = CreateText("Detail Body", detailPanel.transform, string.Empty, 22, BodyText, TextAnchor.UpperLeft);
        modifierDetailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        modifierDetailBody.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform detailBodyRect = modifierDetailBody.rectTransform;
        detailBodyRect.anchorMin = Vector2.zero;
        detailBodyRect.anchorMax = Vector2.one;
        detailBodyRect.offsetMin = new Vector2(20f, 16f);
        detailBodyRect.offsetMax = new Vector2(-20f, -64f);

        modifierNameButtons.Clear();
        float y = -16f;
        for (int i = 0; i < GameCatalogData.Modifiers.Length; i++)
        {
            int index = i;
            Button button = CreateButton(
                $"modifier_{i}",
                listPanel.transform,
                GameCatalogData.Modifiers[i].Name.ToUpperInvariant(),
                ButtonSecondary,
                Color.white);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(-20f, 44f);
            button.onClick.AddListener(() => SelectModifier(index));
            modifierNameButtons.Add(button);
            y -= 52f;
        }

        modifiersBackButton = CreateButton("Back", modifiersTab.transform, "BACK", ButtonPrimary, Color.white);
        RectTransform backRect = modifiersBackButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 18f);
        backRect.sizeDelta = new Vector2(180f, 44f);
        modifiersBackButton.onClick.AddListener(ShowMain);

        modifiersTab.SetActive(false);
    }

    void BuildEnemiesTab()
    {
        Image panel = CreateImage("Enemies Tab", transform, PanelBg);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(960f, 600f);
        enemiesTab = panel.gameObject;

        Text title = CreateText("Title", enemiesTab.transform, "ENEMY PROCESSES", 36, Accent, TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        titleRect.sizeDelta = new Vector2(-40f, 44f);

        Image listPanel = CreateImage("Name List", enemiesTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform listRect = listPanel.rectTransform;
        listRect.offsetMin = new Vector2(24f, 70f);
        listRect.offsetMax = new Vector2(320f, -72f);

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(listPanel.transform, false);
        Image viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        StretchFull(viewport.GetComponent<RectTransform>());
        Mask mask = viewport.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);
        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var scroll = listPanel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        Image detailPanel = CreateImage("Description", enemiesTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform detailRect = detailPanel.rectTransform;
        detailRect.offsetMin = new Vector2(340f, 70f);
        detailRect.offsetMax = new Vector2(-24f, -72f);

        enemyDetailTitle = CreateText("Detail Title", detailPanel.transform, string.Empty, 28, Accent, TextAnchor.UpperLeft);
        enemyDetailTitle.fontStyle = FontStyle.Bold;
        RectTransform detailTitleRect = enemyDetailTitle.rectTransform;
        detailTitleRect.anchorMin = new Vector2(0f, 1f);
        detailTitleRect.anchorMax = new Vector2(1f, 1f);
        detailTitleRect.pivot = new Vector2(0f, 1f);
        detailTitleRect.anchoredPosition = new Vector2(20f, -18f);
        detailTitleRect.sizeDelta = new Vector2(-40f, 36f);

        enemyDetailBody = CreateText("Detail Body", detailPanel.transform, string.Empty, 22, BodyText, TextAnchor.UpperLeft);
        enemyDetailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        enemyDetailBody.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform detailBodyRect = enemyDetailBody.rectTransform;
        detailBodyRect.anchorMin = Vector2.zero;
        detailBodyRect.anchorMax = Vector2.one;
        detailBodyRect.offsetMin = new Vector2(20f, 16f);
        detailBodyRect.offsetMax = new Vector2(-20f, -64f);

        enemyNameButtons.Clear();
        string lastCategory = null;
        for (int i = 0; i < GameCatalogData.Enemies.Length; i++)
        {
            GameCatalogData.EnemyInfo info = GameCatalogData.Enemies[i];
            if (info.Category != lastCategory)
            {
                lastCategory = info.Category;
                Text header = CreateText(
                    $"cat_{info.Category}",
                    content.transform,
                    info.Category,
                    16,
                    new Color(1f, 1f, 1f, 0.45f),
                    TextAnchor.MiddleLeft);
                var headerLayout = header.gameObject.AddComponent<LayoutElement>();
                headerLayout.minHeight = 24f;
                headerLayout.preferredHeight = 24f;
            }

            int index = i;
            Button button = CreateButton(
                $"enemy_{i}",
                content.transform,
                info.Name.ToUpperInvariant(),
                ButtonSecondary,
                Color.white);
            var buttonLayout = button.gameObject.AddComponent<LayoutElement>();
            buttonLayout.minHeight = 40f;
            buttonLayout.preferredHeight = 40f;
            button.onClick.AddListener(() => SelectEnemy(index));
            enemyNameButtons.Add(button);
        }

        enemiesBackButton = CreateButton("Back", enemiesTab.transform, "BACK", ButtonPrimary, Color.white);
        RectTransform backRect = enemiesBackButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 18f);
        backRect.sizeDelta = new Vector2(180f, 44f);
        enemiesBackButton.onClick.AddListener(ShowMain);

        enemiesTab.SetActive(false);
    }

    void BuildSettingsTab()
    {
        Image panel = CreateImage("Settings Tab", transform, PanelBg);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(860f, 560f);
        settingsTab = panel.gameObject;

        Text title = CreateText("Title", settingsTab.transform, "SETTINGS", 36, Accent, TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        titleRect.sizeDelta = new Vector2(-40f, 44f);

        settingsControlsTabButton = CreateButton("Controls Tab", settingsTab.transform, "CONTROLS", ButtonSecondary, Color.white);
        PlaceSettingsSectionTab(settingsControlsTabButton, -280f);
        settingsControlsTabButton.onClick.AddListener(() => ShowSettingsSection(0));

        settingsDisplayTabButton = CreateButton("Display Tab", settingsTab.transform, "DISPLAY", ButtonSecondary, Color.white);
        PlaceSettingsSectionTab(settingsDisplayTabButton, 0f);
        settingsDisplayTabButton.onClick.AddListener(() => ShowSettingsSection(1));

        settingsQualityTabButton = CreateButton("Quality Tab", settingsTab.transform, "QUALITY", ButtonSecondary, Color.white);
        PlaceSettingsSectionTab(settingsQualityTabButton, 280f);
        settingsQualityTabButton.onClick.AddListener(() => ShowSettingsSection(2));

        settingsControlsPanel = CreateSettingsContentPanel("Controls Panel");
        controlsBody = CreateText("Controls Body", settingsControlsPanel.transform, string.Empty, 22, BodyText, TextAnchor.UpperLeft);
        controlsBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        controlsBody.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform controlsRect = controlsBody.rectTransform;
        controlsRect.anchorMin = Vector2.zero;
        controlsRect.anchorMax = Vector2.one;
        controlsRect.offsetMin = new Vector2(24f, 16f);
        controlsRect.offsetMax = new Vector2(-24f, -16f);

        settingsDisplayPanel = CreateSettingsContentPanel("Display Panel");
        resolutionLabel = CreateText("Resolution Label", settingsDisplayPanel.transform, "RESOLUTION", 24, BodyText, TextAnchor.MiddleCenter);
        PlaceCenteredLabel(resolutionLabel, 70f);

        Button resPrev = CreateButton("Res Prev", settingsDisplayPanel.transform, "<", ButtonSecondary, Color.white);
        PlaceSideButton(resPrev, -220f, 70f, 64f);
        resPrev.onClick.AddListener(() => CycleResolution(-1));

        Button resNext = CreateButton("Res Next", settingsDisplayPanel.transform, ">", ButtonSecondary, Color.white);
        PlaceSideButton(resNext, 220f, 70f, 64f);
        resNext.onClick.AddListener(() => CycleResolution(1));

        fullscreenLabel = CreateText("Fullscreen Label", settingsDisplayPanel.transform, "MODE", 24, BodyText, TextAnchor.MiddleCenter);
        PlaceCenteredLabel(fullscreenLabel, -20f);

        Button fullscreenToggle = CreateButton("Fullscreen Toggle", settingsDisplayPanel.transform, "TOGGLE FULLSCREEN / WINDOWED", ButtonPrimary, Color.white);
        RectTransform fsRect = fullscreenToggle.GetComponent<RectTransform>();
        fsRect.anchorMin = new Vector2(0.5f, 0.5f);
        fsRect.anchorMax = new Vector2(0.5f, 0.5f);
        fsRect.pivot = new Vector2(0.5f, 0.5f);
        fsRect.anchoredPosition = new Vector2(0f, -80f);
        fsRect.sizeDelta = new Vector2(420f, 48f);
        fullscreenToggle.onClick.AddListener(ToggleFullscreen);

        settingsQualityPanel = CreateSettingsContentPanel("Quality Panel");
        qualityLabel = CreateText("Quality Label", settingsQualityPanel.transform, "QUALITY", 26, BodyText, TextAnchor.MiddleCenter);
        PlaceCenteredLabel(qualityLabel, 40f);

        Button qPrev = CreateButton("Quality Prev", settingsQualityPanel.transform, "<", ButtonSecondary, Color.white);
        PlaceSideButton(qPrev, -220f, 40f, 64f);
        qPrev.onClick.AddListener(() => CycleQuality(-1));

        Button qNext = CreateButton("Quality Next", settingsQualityPanel.transform, ">", ButtonSecondary, Color.white);
        PlaceSideButton(qNext, 220f, 40f, 64f);
        qNext.onClick.AddListener(() => CycleQuality(1));

        Text qualityHint = CreateText(
            "Quality Hint",
            settingsQualityPanel.transform,
            "Cycles Unity quality levels (URP pipeline asset follows the active quality tier).",
            18,
            new Color(1f, 1f, 1f, 0.5f),
            TextAnchor.MiddleCenter);
        PlaceCenteredLabel(qualityHint, -50f, 420f, 60f);

        settingsBackButton = CreateButton("Back", settingsTab.transform, "BACK", ButtonPrimary, Color.white);
        RectTransform backRect = settingsBackButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 18f);
        backRect.sizeDelta = new Vector2(180f, 44f);
        settingsBackButton.onClick.AddListener(ShowMain);

        settingsTab.SetActive(false);
    }

    GameObject CreateSettingsContentPanel(string name)
    {
        Image panel = CreateImage(name, settingsTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform rect = panel.rectTransform;
        rect.offsetMin = new Vector2(24f, 70f);
        rect.offsetMax = new Vector2(-24f, -120f);
        return panel.gameObject;
    }

    static void PlaceSettingsSectionTab(Button button, float x)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, -74f);
        rect.sizeDelta = new Vector2(200f, 40f);
    }

    static void PlaceCenteredLabel(Text label, float y, float width = 420f, float height = 36f)
    {
        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void PlaceSideButton(Button button, float x, float y, float size)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(size, 48f);
    }

    static void SetTabActive(GameObject tab, bool active)
    {
        if (tab != null)
            tab.SetActive(active);
    }

    static void HighlightCatalogButtons(List<Button> buttons, int selectedIndex)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null || button.targetGraphic == null)
                continue;
            button.targetGraphic.color = i == selectedIndex ? ButtonPrimary : ButtonSecondary;
        }
    }

    static void SelectPrimaryButton(Button button)
    {
        if (button == null)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(button.gameObject);
    }

    static bool WasCancelPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame))
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    Button CreateButton(string name, Transform parent, string label, Color backgroundColor, Color labelColor)
    {
        Image background = CreateImage(name, parent, backgroundColor);
        background.raycastTarget = true;

        var button = background.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.selectedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        button.colors = colors;
        button.targetGraphic = background;

        Text text = CreateText("Label", background.transform, label, 22, labelColor, TextAnchor.MiddleCenter);
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
