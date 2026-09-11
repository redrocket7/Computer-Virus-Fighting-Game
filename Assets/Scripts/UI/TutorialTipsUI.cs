using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// First-run tutorial, Esc pause menu (Continue / Room Modifiers / Enemies / Quit),
/// and H help overlay. Built at runtime like <see cref="PlayerHUD"/>.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class TutorialTipsUI : MonoBehaviour
{
    const string PrefsTutorialDone = "cvfg.tutorial.completed";

    [Header("Timing")]
    [SerializeField] float startupDelay = 0.35f;

    static TutorialTipsUI instance;

    struct TutorialPage
    {
        public string Title;
        public string Body;
    }

    static readonly TutorialPage[] Pages =
    {
        new TutorialPage
        {
            Title = "SYSTEM BOOT",
            Body =
                "You are an antivirus agent inside an infected machine.\n\n" +
                "Clear rooms, push deeper, and survive the processes trying to crash you."
        },
        new TutorialPage
        {
            Title = "CONTROLS",
            Body =
                "Move - WASD / Left Stick\n" +
                "Aim - Mouse / Right Stick\n" +
                "Shoot - Left Click / RT\n" +
                "Dash - Shift / Right Button\n" +
                "Weapon Switch - Scroll Wheel / 1-4 / D-pad\n" +
                "Map - Tab / Select\n" +
                "Help - H\n" +
                "Pause - Esc"
        },
        new TutorialPage
        {
            Title = "COMBAT ROOMS",
            Body =
                "Entering a hostile room locks the doors.\n\n" +
                "Terminate every process to unlock exits.\n" +
                "Watch for room modifiers - RAID, Boot Loop, Packet Loss, Corrupted Save, and Fork Bomb change the fight."
        },
        new TutorialPage
        {
            Title = "UPGRADES",
            Body =
                "Pickups drop after clears:\n" +
                "- Weapons expand your loadout\n" +
                "- USB Dash phases through firewalls\n" +
                "- Goat Dash rams and grants brief i-frames\n\n" +
                "Open the map (Tab) to teleport between revealed pads."
        },
        new TutorialPage
        {
            Title = "READY",
            Body =
                "Prioritize supports (Repair / Overclock) marked in combat.\n" +
                "Keep moving - contact damage and explosions hurt.\n\n" +
                "Press H for help, Esc to pause.\n" +
                "Good luck, agent."
        }
    };

    Font font;
    PlayerController player;
    bool boundToPlayer;

    GameObject tutorialRoot;
    Text tutorialTitle;
    Text tutorialBody;
    Text tutorialProgress;
    Button nextButton;
    Button skipButton;
    Text nextButtonLabel;
    int pageIndex;
    bool tutorialOpen;

    GameObject pauseRoot;
    GameObject pauseMainTab;
    GameObject pauseModifiersTab;
    GameObject pauseEnemiesTab;
    Button continueButton;
    Button modifiersBackButton;
    Button enemiesBackButton;
    Text modifierDetailTitle;
    Text modifierDetailBody;
    Text enemyDetailTitle;
    Text enemyDetailBody;
    readonly List<Button> modifierNameButtons = new List<Button>();
    readonly List<Button> enemyNameButtons = new List<Button>();
    int selectedModifierIndex;
    int selectedEnemyIndex;
    bool pauseOpen;
    bool pauseModifiersOpen;
    bool pauseEnemiesOpen;
    float timeScaleBeforePause = 1f;

    struct ModifierInfo
    {
        public string Name;
        public string Description;
    }

    static readonly ModifierInfo[] ModifierInfos =
    {
        new ModifierInfo
        {
            Name = "RAID Array",
            Description =
                "Guarantees Repair, Overclock, and Shielder supports in the room.\n\n" +
                "Supports buff or protect other enemies - take them out early when you can."
        },
        new ModifierInfo
        {
            Name = "Boot Loop",
            Description =
                "After the first pack dies, another enemy wave spawns.\n\n" +
                "Don't relax after the first clear - the doors stay locked until the reinforcement wave is gone too."
        },
        new ModifierInfo
        {
            Name = "Packet Loss",
            Description =
                "While the fight is active, some of your shots may fizzle before they hit.\n\n" +
                "Keep firing. Missed packets are normal in these rooms."
        },
        new ModifierInfo
        {
            Name = "Corrupted Save",
            Description =
                "One enemy respawns once after death - weaker, but faster.\n\n" +
                "Expect a second engagement with the corrupted copy before the room unlocks."
        },
        new ModifierInfo
        {
            Name = "Fork Bomb",
            Description =
                "Tiny enemies keep spawning until all non-tiny enemies are dead.\n\n" +
                "Focus the larger threats first to stop the drip, then clean up leftover tinies."
        }
    };

    struct EnemyInfo
    {
        public string Name;
        public string Description;
    }

    static readonly EnemyInfo[] EnemyInfos =
    {
        new EnemyInfo
        {
            Name = "Repair",
            Description =
                "Support unit that stays near allies and heals damaged processes in range.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Overclock",
            Description =
                "Support unit that buffs random allies - damage, health, speed, fire rate, and specialty stats.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Name = "Shielder",
            Description =
                "Support unit that starts shielded and periodically grants shields to unprotected allies.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Name = "Transfer",
            Description =
                "Redirects incoming damage to the lowest-HP ally in the room until its transfer meter overloads.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Name = "Link Gun",
            Description =
                "Ranged Transfer variant. Fires predicted three-round bursts while keeping its distance.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Name = "Small",
            Description =
                "Basic chase process. Low health, closes distance, and deals contact damage.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Name = "Fat",
            Description =
                "Tanky chase process with high health and low speed.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Name = "Long",
            Description =
                "Elongated chase process with medium health and high speed.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Fork",
            Description =
                "Chase process spawned by Branch enemies.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Name = "Tiny",
            Description =
                "Small pack hunters that try to stick together in groups of about 3-4.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Name = "Dodge",
            Description =
                "Melee chaser that sidesteps incoming player bullets.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Name = "Ram",
            Description =
                "Chases up close. From longer range it winds up, then charges in a straight ram.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Bomb",
            Description =
                "Chases normally, then arms a short fuse when close or when damaged.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Name = "Zip Bomb",
            Description =
                "Absorbs your damage into a growing body. When storage is full, the next hit detonates it.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Name = "Branch",
            Description =
                "Chase enemy that slowly spawns Fork processes while alive.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Trojanspawn",
            Description =
                "Chase enemy that releases 2-4 Tiny enemies on death (3 is most common).\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Name = "Gun",
            Description =
                "Ranged enemy that keeps mid-range and fires single projectiles.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Name = "Bounce Gun",
            Description =
                "Gunner that fires bouncing shots and sidesteps incoming bullets like Dodge.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Machine Gun",
            Description =
                "Keeps distance and sprays a rapid stream of bullets.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Name = "Shotgun",
            Description =
                "Holds closer range and fires a short spread of pellets.\n\n" +
                "Difficulty: 5"
        },
        new EnemyInfo
        {
            Name = "Split Gun",
            Description =
                "Slow, heavy ranged attacker. Its shots split into fragments when they hit walls.\n\n" +
                "Difficulty: 5"
        },
        new EnemyInfo
        {
            Name = "Homing Rocket",
            Description =
                "Keeps distance and fires missiles that home toward you.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Name = "Cannon",
            Description =
                "Slow ranged enemy that lobbs large cannonballs from long range.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Mortar",
            Description =
                "Keeps distance and arcs explosive shells that detonate on arrival.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Name = "Gun Absorb",
            Description =
                "Absorbs your bullet damage into storage, then spends it on charged shots.\n\n" +
                "Difficulty: 5"
        },
        new EnemyInfo
        {
            Name = "Mega Explosive",
            Description =
                "Larger version of the mortar that fires shells in short bursts.\n\n" +
                "On death it starts a countdown, then explodes."
        },
        new EnemyInfo
        {
            Name = "Mega Trojanspawn",
            Description =
                "Huge trojanspawn that spawns Tiny enemies while alive, then splits into 2-3 Trojanspawns on death.\n\n" +
                "Has a chance to split into a shotgun process with 2 Trojanspawns on death."
        }
    };

    GameObject helpRoot;
    bool helpOpen;

    public static TutorialTipsUI Instance => instance;

    public static void EnsureExists()
    {
        if (FindAnyObjectByType<TutorialTipsUI>() != null)
            return;

        if (FindAnyObjectByType<PlayerController>() == null)
            return;

        var go = new GameObject("Tutorial Tips UI", typeof(Canvas));
        go.AddComponent<TutorialTipsUI>();
    }

    void Awake()
    {
        instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        BuildTutorialPanel();
        BuildPauseMenu();
        BuildHelpOverlay();
    }

    void OnEnable()
    {
        BindToPlayer();
    }

    void Start()
    {
        BindToPlayer();
        if (!IsTutorialCompleted())
            Invoke(nameof(OpenTutorial), Mathf.Max(0f, startupDelay));
    }

    void OnDisable()
    {
        if (pauseOpen)
            SetPauseOpen(false);

        UnbindFromPlayer();
        if (instance == this)
            instance = null;
    }

    void OnDestroy()
    {
        if (pauseOpen)
            Time.timeScale = 1f;

        if (instance == this)
            instance = null;
    }

    void Update()
    {
        if (player != null && player.IsDead)
        {
            if (tutorialOpen)
                CloseTutorial(markComplete: false);
            if (helpOpen)
                SetHelpOpen(false);
            if (pauseOpen)
                SetPauseOpen(false);
            return;
        }

        if (tutorialOpen)
        {
            if (WasAdvancePressed())
                AdvanceTutorial();
            else if (WasCancelPressed())
                CloseTutorial(markComplete: true);
            return;
        }

        if (WasHelpTogglePressed() && !pauseOpen)
            SetHelpOpen(!helpOpen);

        if (helpOpen)
        {
            if (WasCancelPressed() || WasHelpTogglePressed())
                SetHelpOpen(false);
            return;
        }

        if (WasPauseTogglePressed())
        {
            if (pauseOpen && (pauseModifiersOpen || pauseEnemiesOpen))
                ShowPauseMainTab();
            else
                SetPauseOpen(!pauseOpen);
        }

        if (pauseOpen && !pauseModifiersOpen && !pauseEnemiesOpen && WasConfirmContinuePressed())
            SetPauseOpen(false);
    }

    void BindToPlayer()
    {
        PlayerController found = FindAnyObjectByType<PlayerController>();
        if (found == null)
            return;

        if (boundToPlayer && player == found)
            return;

        UnbindFromPlayer();
        player = found;
        boundToPlayer = true;
    }

    void UnbindFromPlayer()
    {
        boundToPlayer = false;
        player = null;
    }

    void OpenTutorial()
    {
        if (tutorialOpen || player == null || player.IsDead)
            return;

        pageIndex = 0;
        tutorialOpen = true;
        SetHelpOpen(false);
        SetPauseOpen(false);
        player.SetControlsLocked(true);
        if (tutorialRoot != null)
            tutorialRoot.SetActive(true);
        RefreshTutorialPage();
        SelectPrimaryButton(nextButton);
    }

    void AdvanceTutorial()
    {
        if (!tutorialOpen)
            return;

        if (pageIndex >= Pages.Length - 1)
        {
            CloseTutorial(markComplete: true);
            return;
        }

        pageIndex++;
        RefreshTutorialPage();
    }

    void CloseTutorial(bool markComplete)
    {
        if (!tutorialOpen)
            return;

        tutorialOpen = false;
        if (tutorialRoot != null)
            tutorialRoot.SetActive(false);

        if (markComplete)
            PlayerPrefs.SetInt(PrefsTutorialDone, 1);

        if (player != null && !player.IsDead && !pauseOpen && !helpOpen)
            player.SetControlsLocked(false);
    }

    void RefreshTutorialPage()
    {
        TutorialPage page = Pages[Mathf.Clamp(pageIndex, 0, Pages.Length - 1)];
        if (tutorialTitle != null)
            tutorialTitle.text = page.Title;
        if (tutorialBody != null)
            tutorialBody.text = page.Body;
        if (tutorialProgress != null)
            tutorialProgress.text = $"{pageIndex + 1} / {Pages.Length}";
        if (nextButtonLabel != null)
            nextButtonLabel.text = pageIndex >= Pages.Length - 1 ? "START RUN" : "NEXT";
    }

    void SetPauseOpen(bool open)
    {
        if (pauseOpen == open)
            return;

        if (open)
        {
            if (tutorialOpen || (player != null && player.IsDead))
                return;

            SetHelpOpen(false);
            timeScaleBeforePause = Time.timeScale <= 0f ? 1f : Time.timeScale;
            Time.timeScale = 0f;
            if (player != null)
                player.SetControlsLocked(true);
        }
        else
        {
            Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;
            if (player != null && !player.IsDead && !tutorialOpen && !helpOpen)
                player.SetControlsLocked(false);
        }

        pauseOpen = open;
        if (pauseRoot != null)
            pauseRoot.SetActive(open);

        if (open)
        {
            ShowPauseMainTab();
            SelectPrimaryButton(continueButton);
        }
        else
        {
            pauseModifiersOpen = false;
            pauseEnemiesOpen = false;
        }
    }

    void ShowPauseMainTab()
    {
        pauseModifiersOpen = false;
        pauseEnemiesOpen = false;
        if (pauseMainTab != null)
            pauseMainTab.SetActive(true);
        if (pauseModifiersTab != null)
            pauseModifiersTab.SetActive(false);
        if (pauseEnemiesTab != null)
            pauseEnemiesTab.SetActive(false);
        SelectPrimaryButton(continueButton);
    }

    void ShowPauseModifiersTab()
    {
        pauseModifiersOpen = true;
        pauseEnemiesOpen = false;
        if (pauseMainTab != null)
            pauseMainTab.SetActive(false);
        if (pauseEnemiesTab != null)
            pauseEnemiesTab.SetActive(false);
        if (pauseModifiersTab != null)
            pauseModifiersTab.SetActive(true);
        SelectModifier(0);
        if (modifierNameButtons.Count > 0)
            SelectPrimaryButton(modifierNameButtons[0]);
        else
            SelectPrimaryButton(modifiersBackButton);
    }

    void ShowPauseEnemiesTab()
    {
        pauseEnemiesOpen = true;
        pauseModifiersOpen = false;
        if (pauseMainTab != null)
            pauseMainTab.SetActive(false);
        if (pauseModifiersTab != null)
            pauseModifiersTab.SetActive(false);
        if (pauseEnemiesTab != null)
            pauseEnemiesTab.SetActive(true);
        SelectEnemy(0);
        if (enemyNameButtons.Count > 0)
            SelectPrimaryButton(enemyNameButtons[0]);
        else
            SelectPrimaryButton(enemiesBackButton);
    }

    void SelectModifier(int index)
    {
        if (modifierDetailTitle == null || modifierDetailBody == null)
            return;

        selectedModifierIndex = Mathf.Clamp(index, 0, ModifierInfos.Length - 1);
        ModifierInfo info = ModifierInfos[selectedModifierIndex];
        modifierDetailTitle.text = info.Name.ToUpperInvariant();
        modifierDetailBody.text = info.Description;
        HighlightCatalogButtons(modifierNameButtons, selectedModifierIndex);
    }

    void SelectEnemy(int index)
    {
        if (enemyDetailTitle == null || enemyDetailBody == null)
            return;

        selectedEnemyIndex = Mathf.Clamp(index, 0, EnemyInfos.Length - 1);
        EnemyInfo info = EnemyInfos[selectedEnemyIndex];
        enemyDetailTitle.text = info.Name.ToUpperInvariant();
        enemyDetailBody.text = info.Description;
        HighlightCatalogButtons(enemyNameButtons, selectedEnemyIndex);
    }

    static void HighlightCatalogButtons(List<Button> buttons, int selectedIndex)
    {
        Color idle = new Color(0.14f, 0.28f, 0.38f, 0.98f);
        Color selected = new Color(0.12f, 0.45f, 0.32f, 0.98f);
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null || button.targetGraphic == null)
                continue;
            button.targetGraphic.color = i == selectedIndex ? selected : idle;
        }
    }

    void SetHelpOpen(bool open)
    {
        if (helpOpen == open)
            return;

        helpOpen = open;
        if (helpRoot != null)
            helpRoot.SetActive(open);

        if (open)
        {
            if (player != null && !player.IsDead)
                player.SetControlsLocked(true);
        }
        else if (player != null && !player.IsDead && !tutorialOpen && !pauseOpen)
        {
            player.SetControlsLocked(false);
        }
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

    static bool IsTutorialCompleted() => PlayerPrefs.GetInt(PrefsTutorialDone, 0) != 0;

    static bool WasAdvancePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame ||
             keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.dKey.wasPressedThisFrame ||
             keyboard.rightArrowKey.wasPressedThisFrame))
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null &&
               (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame);
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

    static bool WasPauseTogglePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.startButton.wasPressedThisFrame;
    }

    static bool WasConfirmContinuePressed()
    {
        // Start already toggles pause; South confirms Continue when paused.
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
    }

    static bool WasHelpTogglePressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.hKey.wasPressedThisFrame;
    }

    static void SelectPrimaryButton(Button button)
    {
        if (button == null)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(button.gameObject);
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
        canvas.sortingOrder = 120;

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

    void BuildTutorialPanel()
    {
        Image dim = CreateImage("Tutorial", transform, new Color(0f, 0f, 0f, 0.78f));
        dim.raycastTarget = true;
        StretchFull(dim.rectTransform);

        Image panel = CreateImage("Panel", dim.rectTransform, new Color(0.06f, 0.08f, 0.09f, 0.96f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(720f, 460f);

        tutorialTitle = CreateText("Title", panelRect, "SYSTEM BOOT", 42, new Color(0.45f, 0.95f, 0.7f), TextAnchor.UpperCenter);
        tutorialTitle.fontStyle = FontStyle.Bold;
        RectTransform titleRect = tutorialTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -28f);
        titleRect.sizeDelta = new Vector2(-48f, 52f);

        tutorialBody = CreateText("Body", panelRect, string.Empty, 24, new Color(0.9f, 0.93f, 0.9f), TextAnchor.UpperLeft);
        tutorialBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        tutorialBody.verticalOverflow = VerticalWrapMode.Overflow;
        tutorialBody.lineSpacing = 1.08f;
        RectTransform bodyRect = tutorialBody.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(36f, 96f);
        bodyRect.offsetMax = new Vector2(-36f, -92f);

        tutorialProgress = CreateText("Progress", panelRect, "1 / 5", 18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.LowerLeft);
        RectTransform progressRect = tutorialProgress.rectTransform;
        progressRect.anchorMin = new Vector2(0f, 0f);
        progressRect.anchorMax = new Vector2(0f, 0f);
        progressRect.pivot = new Vector2(0f, 0f);
        progressRect.anchoredPosition = new Vector2(36f, 28f);
        progressRect.sizeDelta = new Vector2(120f, 28f);

        skipButton = CreateButton("Skip", panelRect, "SKIP", new Color(0.16f, 0.18f, 0.2f, 0.95f), new Color(1f, 1f, 1f, 0.75f));
        RectTransform skipRect = skipButton.GetComponent<RectTransform>();
        skipRect.anchorMin = new Vector2(1f, 0f);
        skipRect.anchorMax = new Vector2(1f, 0f);
        skipRect.pivot = new Vector2(1f, 0f);
        skipRect.anchoredPosition = new Vector2(-220f, 22f);
        skipRect.sizeDelta = new Vector2(120f, 48f);
        skipButton.onClick.AddListener(() => CloseTutorial(markComplete: true));

        nextButton = CreateButton("Next", panelRect, "NEXT", new Color(0.12f, 0.45f, 0.32f, 0.98f), Color.white);
        RectTransform nextRect = nextButton.GetComponent<RectTransform>();
        nextRect.anchorMin = new Vector2(1f, 0f);
        nextRect.anchorMax = new Vector2(1f, 0f);
        nextRect.pivot = new Vector2(1f, 0f);
        nextRect.anchoredPosition = new Vector2(-28f, 22f);
        nextRect.sizeDelta = new Vector2(170f, 48f);
        nextButton.onClick.AddListener(AdvanceTutorial);
        nextButtonLabel = nextButton.GetComponentInChildren<Text>();

        Text hint = CreateText(
            "Hint",
            panelRect,
            "Enter / Space / A  |  Esc to skip",
            16,
            new Color(1f, 1f, 1f, 0.45f),
            TextAnchor.LowerCenter);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 78f);
        hintRect.sizeDelta = new Vector2(-40f, 24f);

        tutorialRoot = dim.gameObject;
        tutorialRoot.SetActive(false);
    }

    void BuildPauseMenu()
    {
        Image dim = CreateImage("Pause Menu", transform, new Color(0f, 0f, 0f, 0.72f));
        dim.raycastTarget = true;
        StretchFull(dim.rectTransform);

        Image panel = CreateImage("Panel", dim.rectTransform, new Color(0.06f, 0.08f, 0.09f, 0.96f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(920f, 560f);

        BuildPauseMainTab(panelRect);
        BuildPauseModifiersTab(panelRect);
        BuildPauseEnemiesTab(panelRect);

        pauseRoot = dim.gameObject;
        pauseRoot.SetActive(false);
        ShowPauseMainTab();
    }

    void BuildPauseMainTab(RectTransform panelRect)
    {
        pauseMainTab = new GameObject("Main Tab", typeof(RectTransform));
        pauseMainTab.transform.SetParent(panelRect, false);
        StretchFull(pauseMainTab.GetComponent<RectTransform>());

        Text title = CreateText("Title", pauseMainTab.transform, "PAUSED", 44, new Color(0.45f, 0.95f, 0.7f), TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -36f);
        titleRect.sizeDelta = new Vector2(-40f, 52f);

        continueButton = CreateButton(
            "Continue",
            pauseMainTab.transform,
            "CONTINUE",
            new Color(0.12f, 0.45f, 0.32f, 0.98f),
            Color.white);
        RectTransform continueRect = continueButton.GetComponent<RectTransform>();
        continueRect.anchorMin = new Vector2(0.5f, 0.5f);
        continueRect.anchorMax = new Vector2(0.5f, 0.5f);
        continueRect.pivot = new Vector2(0.5f, 0.5f);
        continueRect.anchoredPosition = new Vector2(0f, 96f);
        continueRect.sizeDelta = new Vector2(280f, 54f);
        continueButton.onClick.AddListener(() => SetPauseOpen(false));

        Button modifiersButton = CreateButton(
            "Room Modifiers",
            pauseMainTab.transform,
            "ROOM MODIFIERS",
            new Color(0.14f, 0.28f, 0.38f, 0.98f),
            Color.white);
        RectTransform modifiersRect = modifiersButton.GetComponent<RectTransform>();
        modifiersRect.anchorMin = new Vector2(0.5f, 0.5f);
        modifiersRect.anchorMax = new Vector2(0.5f, 0.5f);
        modifiersRect.pivot = new Vector2(0.5f, 0.5f);
        modifiersRect.anchoredPosition = new Vector2(0f, 32f);
        modifiersRect.sizeDelta = new Vector2(280f, 54f);
        modifiersButton.onClick.AddListener(ShowPauseModifiersTab);

        Button enemiesButton = CreateButton(
            "Enemies",
            pauseMainTab.transform,
            "ENEMIES",
            new Color(0.14f, 0.28f, 0.38f, 0.98f),
            Color.white);
        RectTransform enemiesRect = enemiesButton.GetComponent<RectTransform>();
        enemiesRect.anchorMin = new Vector2(0.5f, 0.5f);
        enemiesRect.anchorMax = new Vector2(0.5f, 0.5f);
        enemiesRect.pivot = new Vector2(0.5f, 0.5f);
        enemiesRect.anchoredPosition = new Vector2(0f, -32f);
        enemiesRect.sizeDelta = new Vector2(280f, 54f);
        enemiesButton.onClick.AddListener(ShowPauseEnemiesTab);

        Button quitButton = CreateButton(
            "Quit",
            pauseMainTab.transform,
            "QUIT",
            new Color(0.45f, 0.16f, 0.16f, 0.98f),
            Color.white);
        RectTransform quitRect = quitButton.GetComponent<RectTransform>();
        quitRect.anchorMin = new Vector2(0.5f, 0.5f);
        quitRect.anchorMax = new Vector2(0.5f, 0.5f);
        quitRect.pivot = new Vector2(0.5f, 0.5f);
        quitRect.anchoredPosition = new Vector2(0f, -96f);
        quitRect.sizeDelta = new Vector2(280f, 54f);
        quitButton.onClick.AddListener(QuitGame);

        Text hint = CreateText(
            "Hint",
            pauseMainTab.transform,
            "Esc / Start to resume",
            16,
            new Color(1f, 1f, 1f, 0.45f),
            TextAnchor.LowerCenter);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 22f);
        hintRect.sizeDelta = new Vector2(-40f, 24f);
    }

    void BuildPauseModifiersTab(RectTransform panelRect)
    {
        pauseModifiersTab = new GameObject("Modifiers Tab", typeof(RectTransform));
        pauseModifiersTab.transform.SetParent(panelRect, false);
        StretchFull(pauseModifiersTab.GetComponent<RectTransform>());
        modifierNameButtons.Clear();

        Text title = CreateText(
            "Title",
            pauseModifiersTab.transform,
            "ROOM MODIFIERS",
            36,
            new Color(0.45f, 0.95f, 0.7f),
            TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -22f);
        titleRect.sizeDelta = new Vector2(-40f, 44f);

        Image listPanel = CreateImage("Name List", pauseModifiersTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform listRect = listPanel.rectTransform;
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(0f, 1f);
        listRect.offsetMin = new Vector2(28f, 78f);
        listRect.offsetMax = new Vector2(308f, -78f);

        Image detailPanel = CreateImage("Description", pauseModifiersTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform detailRect = detailPanel.rectTransform;
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 1f);
        detailRect.offsetMin = new Vector2(324f, 78f);
        detailRect.offsetMax = new Vector2(-28f, -78f);

        modifierDetailTitle = CreateText(
            "Detail Title",
            detailRect,
            "RAID ARRAY",
            28,
            new Color(0.55f, 0.95f, 0.78f),
            TextAnchor.UpperLeft);
        modifierDetailTitle.fontStyle = FontStyle.Bold;
        RectTransform detailTitleRect = modifierDetailTitle.rectTransform;
        detailTitleRect.anchorMin = new Vector2(0f, 1f);
        detailTitleRect.anchorMax = new Vector2(1f, 1f);
        detailTitleRect.pivot = new Vector2(0f, 1f);
        detailTitleRect.anchoredPosition = new Vector2(22f, -18f);
        detailTitleRect.sizeDelta = new Vector2(-44f, 36f);

        modifierDetailBody = CreateText(
            "Detail Body",
            detailRect,
            string.Empty,
            22,
            new Color(0.9f, 0.93f, 0.9f),
            TextAnchor.UpperLeft);
        modifierDetailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        modifierDetailBody.verticalOverflow = VerticalWrapMode.Overflow;
        modifierDetailBody.lineSpacing = 1.08f;
        RectTransform detailBodyRect = modifierDetailBody.rectTransform;
        detailBodyRect.anchorMin = Vector2.zero;
        detailBodyRect.anchorMax = Vector2.one;
        detailBodyRect.offsetMin = new Vector2(22f, 18f);
        detailBodyRect.offsetMax = new Vector2(-22f, -64f);

        float buttonHeight = 48f;
        float buttonGap = 10f;
        float startY = -18f;
        for (int i = 0; i < ModifierInfos.Length; i++)
        {
            int index = i;
            Button button = CreateButton(
                $"Modifier_{i}",
                listRect,
                ModifierInfos[i].Name.ToUpperInvariant(),
                new Color(0.14f, 0.28f, 0.38f, 0.98f),
                Color.white);
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.anchoredPosition = new Vector2(0f, startY - i * (buttonHeight + buttonGap));
            buttonRect.sizeDelta = new Vector2(-24f, buttonHeight);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 18;
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform labelRect = label.rectTransform;
                labelRect.offsetMin = new Vector2(14f, 0f);
                labelRect.offsetMax = new Vector2(-8f, 0f);
            }

            button.onClick.AddListener(() => SelectModifier(index));
            modifierNameButtons.Add(button);
        }

        modifiersBackButton = CreateButton(
            "Back",
            pauseModifiersTab.transform,
            "BACK",
            new Color(0.12f, 0.45f, 0.32f, 0.98f),
            Color.white);
        RectTransform backRect = modifiersBackButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 18f);
        backRect.sizeDelta = new Vector2(200f, 48f);
        modifiersBackButton.onClick.AddListener(ShowPauseMainTab);

        SelectModifier(0);
        pauseModifiersTab.SetActive(false);
    }

    void BuildPauseEnemiesTab(RectTransform panelRect)
    {
        pauseEnemiesTab = new GameObject("Enemies Tab", typeof(RectTransform));
        pauseEnemiesTab.transform.SetParent(panelRect, false);
        StretchFull(pauseEnemiesTab.GetComponent<RectTransform>());
        enemyNameButtons.Clear();

        Text title = CreateText(
            "Title",
            pauseEnemiesTab.transform,
            "ENEMIES",
            36,
            new Color(0.45f, 0.95f, 0.7f),
            TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -22f);
        titleRect.sizeDelta = new Vector2(-40f, 44f);

        Image listPanel = CreateImage("Name List", pauseEnemiesTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        listPanel.raycastTarget = true;
        RectTransform listRect = listPanel.rectTransform;
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(0f, 1f);
        listRect.offsetMin = new Vector2(28f, 78f);
        listRect.offsetMax = new Vector2(308f, -78f);

        ScrollRect scroll = listPanel.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 42f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;

        Image viewportImage = CreateImage("Viewport", listRect, new Color(1f, 1f, 1f, 0.01f));
        viewportImage.raycastTarget = true;
        RectTransform viewportRect = viewportImage.rectTransform;
        StretchFull(viewportRect);
        viewportRect.offsetMin = new Vector2(8f, 8f);
        viewportRect.offsetMax = new Vector2(-8f, -8f);
        viewportImage.gameObject.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportRect, false);
        RectTransform contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        Image detailPanel = CreateImage("Description", pauseEnemiesTab.transform, new Color(0.04f, 0.055f, 0.06f, 0.95f));
        RectTransform detailRect = detailPanel.rectTransform;
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 1f);
        detailRect.offsetMin = new Vector2(324f, 78f);
        detailRect.offsetMax = new Vector2(-28f, -78f);

        enemyDetailTitle = CreateText(
            "Detail Title",
            detailRect,
            "REPAIR",
            28,
            new Color(0.55f, 0.95f, 0.78f),
            TextAnchor.UpperLeft);
        enemyDetailTitle.fontStyle = FontStyle.Bold;
        RectTransform detailTitleRect = enemyDetailTitle.rectTransform;
        detailTitleRect.anchorMin = new Vector2(0f, 1f);
        detailTitleRect.anchorMax = new Vector2(1f, 1f);
        detailTitleRect.pivot = new Vector2(0f, 1f);
        detailTitleRect.anchoredPosition = new Vector2(22f, -18f);
        detailTitleRect.sizeDelta = new Vector2(-44f, 36f);

        enemyDetailBody = CreateText(
            "Detail Body",
            detailRect,
            string.Empty,
            22,
            new Color(0.9f, 0.93f, 0.9f),
            TextAnchor.UpperLeft);
        enemyDetailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        enemyDetailBody.verticalOverflow = VerticalWrapMode.Overflow;
        enemyDetailBody.lineSpacing = 1.08f;
        RectTransform detailBodyRect = enemyDetailBody.rectTransform;
        detailBodyRect.anchorMin = Vector2.zero;
        detailBodyRect.anchorMax = Vector2.one;
        detailBodyRect.offsetMin = new Vector2(22f, 18f);
        detailBodyRect.offsetMax = new Vector2(-22f, -64f);

        float buttonHeight = 44f;
        float buttonGap = 8f;
        float topPadding = 6f;
        float bottomPadding = 6f;
        for (int i = 0; i < EnemyInfos.Length; i++)
        {
            int index = i;
            Button button = CreateButton(
                $"Enemy_{i}",
                contentRect,
                EnemyInfos[i].Name.ToUpperInvariant(),
                new Color(0.14f, 0.28f, 0.38f, 0.98f),
                Color.white);
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.anchoredPosition = new Vector2(0f, -(topPadding + i * (buttonHeight + buttonGap)));
            buttonRect.sizeDelta = new Vector2(0f, buttonHeight);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 17;
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform labelRect = label.rectTransform;
                labelRect.offsetMin = new Vector2(12f, 0f);
                labelRect.offsetMax = new Vector2(-8f, 0f);
            }

            button.onClick.AddListener(() => SelectEnemy(index));
            enemyNameButtons.Add(button);
        }

        float contentHeight = topPadding +
                              EnemyInfos.Length * buttonHeight +
                              Mathf.Max(0, EnemyInfos.Length - 1) * buttonGap +
                              bottomPadding;
        contentRect.sizeDelta = new Vector2(0f, contentHeight);

        enemiesBackButton = CreateButton(
            "Back",
            pauseEnemiesTab.transform,
            "BACK",
            new Color(0.12f, 0.45f, 0.32f, 0.98f),
            Color.white);
        RectTransform backRect = enemiesBackButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 18f);
        backRect.sizeDelta = new Vector2(200f, 48f);
        enemiesBackButton.onClick.AddListener(ShowPauseMainTab);

        SelectEnemy(0);
        pauseEnemiesTab.SetActive(false);
    }

    void BuildHelpOverlay()
    {
        Image dim = CreateImage("Help", transform, new Color(0f, 0f, 0f, 0.72f));
        dim.raycastTarget = true;
        StretchFull(dim.rectTransform);

        Image panel = CreateImage("Panel", dim.rectTransform, new Color(0.06f, 0.08f, 0.09f, 0.96f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(700f, 480f);

        Text title = CreateText("Title", panelRect, "AGENT HELP", 40, new Color(0.45f, 0.95f, 0.7f), TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -24f);
        titleRect.sizeDelta = new Vector2(-40f, 48f);

        var body = new System.Text.StringBuilder(512);
        for (int i = 0; i < Pages.Length; i++)
        {
            if (i > 0)
                body.AppendLine().AppendLine();
            body.Append(Pages[i].Title).AppendLine();
            body.Append(Pages[i].Body);
        }

        Text content = CreateText("Body", panelRect, body.ToString(), 20, new Color(0.9f, 0.93f, 0.9f), TextAnchor.UpperLeft);
        content.horizontalOverflow = HorizontalWrapMode.Wrap;
        content.verticalOverflow = VerticalWrapMode.Overflow;
        content.lineSpacing = 1.05f;
        RectTransform contentRect = content.rectTransform;
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(32f, 70f);
        contentRect.offsetMax = new Vector2(-32f, -84f);

        Button close = CreateButton("Close", panelRect, "CLOSE (H / Esc)", new Color(0.12f, 0.45f, 0.32f, 0.98f), Color.white);
        RectTransform closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(0.5f, 0f);
        closeRect.anchorMax = new Vector2(0.5f, 0f);
        closeRect.pivot = new Vector2(0.5f, 0f);
        closeRect.anchoredPosition = new Vector2(0f, 18f);
        closeRect.sizeDelta = new Vector2(240f, 44f);
        close.onClick.AddListener(() => SetHelpOpen(false));

        helpRoot = dim.gameObject;
        helpRoot.SetActive(false);
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
