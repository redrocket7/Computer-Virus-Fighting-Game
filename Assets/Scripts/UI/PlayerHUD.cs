using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Screen-space health bar and game over banner.
/// The whole hierarchy is built at runtime, so the scene needs no UI objects.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Leave empty to find the player automatically.")]
    [SerializeField] PlayerController player;

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

    RectTransform healthFill;
    Image healthFillImage;
    Text healthLabel;
    Text weaponLabel;
    GameObject gameOverRoot;
    Button restartButton;
    Font font;
    bool isGameOver;
    bool boundToPlayer;

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
        if (FindAnyObjectByType<PlayerHUD>() != null)
            return;

        if (FindAnyObjectByType<PlayerController>() == null)
            return;

        var hud = new GameObject("Player HUD", typeof(Canvas));
        hud.AddComponent<PlayerHUD>();
        hud.AddComponent<DungeonMinimap>();
        hud.AddComponent<DungeonMapOverlay>();
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        BuildHealthBar();
        BuildWeaponLabel();
        BuildGameOverBanner();
    }

    void OnEnable()
    {
        BindToPlayer();
    }

    void Start()
    {
        // Scene reload can race component enable order; rebind once Start runs.
        BindToPlayer();
    }

    void OnDisable()
    {
        UnbindFromPlayer();
    }

    void BindToPlayer()
    {
        PlayerController found = player != null
            ? player
            : FindAnyObjectByType<PlayerController>();

        if (found == null)
            return;

        if (boundToPlayer && player == found)
        {
            OnHealthChanged(player.CurrentHealth, player.MaxHealth);
            OnWeaponChanged(player.CurrentWeaponName);
            SetGameOverVisible(player.IsDead);
            return;
        }

        UnbindFromPlayer();
        player = found;
        player.HealthChanged += OnHealthChanged;
        player.Died += OnPlayerDied;
        player.WeaponChanged += OnWeaponChanged;
        boundToPlayer = true;

        OnHealthChanged(player.CurrentHealth, player.MaxHealth);
        OnWeaponChanged(player.CurrentWeaponName);
        SetGameOverVisible(player.IsDead);
    }

    void UnbindFromPlayer()
    {
        if (!boundToPlayer || player == null)
        {
            boundToPlayer = false;
            return;
        }

        player.HealthChanged -= OnHealthChanged;
        player.Died -= OnPlayerDied;
        player.WeaponChanged -= OnWeaponChanged;
        boundToPlayer = false;
    }

    void Update()
    {
        if (!isGameOver)
            return;

        if (WasRestartPressed())
            RestartRun();
    }

    void OnHealthChanged(float current, float max)
    {
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        healthFill.anchorMax = new Vector2(fraction, 1f);
        healthFillImage.enabled = fraction > 0f;
        healthFillImage.color = Color.Lerp(criticalColor, healthyColor, fraction);
        healthLabel.text = $"{FormatHealth(current)} / {FormatHealth(max)}";
    }

    static string FormatHealth(float value)
    {
        if (Mathf.Approximately(value, Mathf.Round(value)))
            return Mathf.RoundToInt(value).ToString();

        return value.ToString("0.#");
    }

    void OnWeaponChanged(string weaponName)
    {
        if (weaponLabel == null)
            return;

        weaponLabel.text = string.IsNullOrEmpty(weaponName) ? string.Empty : weaponName.ToUpperInvariant();
    }

    void OnPlayerDied()
    {
        SetGameOverVisible(true);
    }

    void SetGameOverVisible(bool visible)
    {
        isGameOver = visible;
        if (gameOverRoot != null)
            gameOverRoot.SetActive(visible);

        if (!visible || restartButton == null)
            return;

        // Make the button immediately usable with keyboard / gamepad Submit.
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

        // EventSystems were previously DontDestroyOnLoad and could linger across restarts.
        // Tear them down so the next HUD creates a fresh one.
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

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null &&
            (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame))
        {
            return true;
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

    void BuildHealthBar()
    {
        RectTransform background = CreateImage("Health Bar", transform, new Color(0f, 0f, 0f, 0.6f)).rectTransform;
        background.anchorMin = new Vector2(0f, 1f);
        background.anchorMax = new Vector2(0f, 1f);
        background.pivot = new Vector2(0f, 1f);
        background.anchoredPosition = new Vector2(barMargin.x, -barMargin.y);
        background.sizeDelta = barSize;

        healthFillImage = CreateImage("Fill", background, healthyColor);
        healthFill = healthFillImage.rectTransform;
        healthFill.anchorMin = Vector2.zero;
        healthFill.anchorMax = Vector2.one;
        healthFill.offsetMin = new Vector2(3f, 3f);
        healthFill.offsetMax = new Vector2(-3f, -3f);

        healthLabel = CreateText("Amount", background, string.Empty, 18, Color.white, TextAnchor.MiddleCenter);
        RectTransform labelRect = healthLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    void BuildWeaponLabel()
    {
        RectTransform background = CreateImage("Weapon", transform, new Color(0f, 0f, 0f, 0.55f)).rectTransform;
        background.anchorMin = new Vector2(0f, 0f);
        background.anchorMax = new Vector2(0f, 0f);
        background.pivot = new Vector2(0f, 0f);
        background.anchoredPosition = new Vector2(barMargin.x, barMargin.y);
        background.sizeDelta = new Vector2(280f, 36f);

        weaponLabel = CreateText("Name", background, string.Empty, 20, Color.white, TextAnchor.MiddleLeft);
        RectTransform labelRect = weaponLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);
    }

    void BuildGameOverBanner()
    {
        Image dim = CreateImage("Game Over", transform, new Color(0f, 0f, 0f, 0.65f));
        dim.raycastTarget = true;
        RectTransform dimRect = dim.rectTransform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;

        Text label = CreateText("Message", dimRect, gameOverMessage, 96, gameOverColor, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyle.Bold;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.45f);
        labelRect.anchorMax = new Vector2(1f, 0.75f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        restartButton = CreateButton("Restart", dimRect, restartButtonLabel, restartButtonColor, restartLabelColor);
        RectTransform buttonRect = restartButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.28f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.28f);
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
        hintRect.anchorMin = new Vector2(0f, 0.14f);
        hintRect.anchorMax = new Vector2(1f, 0.22f);
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
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

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
}
