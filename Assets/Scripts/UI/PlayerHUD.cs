using UnityEngine;
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

    RectTransform healthFill;
    Image healthFillImage;
    Text healthLabel;
    Text weaponLabel;
    GameObject gameOverRoot;
    Font font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
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
        BuildCanvas();
        BuildHealthBar();
        BuildWeaponLabel();
        BuildGameOverBanner();
    }

    void OnEnable()
    {
        if (player == null)
            player = FindAnyObjectByType<PlayerController>();

        if (player == null)
            return;

        player.HealthChanged += OnHealthChanged;
        player.Died += OnPlayerDied;
        player.WeaponChanged += OnWeaponChanged;

        OnHealthChanged(player.CurrentHealth, player.MaxHealth);
        OnWeaponChanged(player.CurrentWeaponName);
        gameOverRoot.SetActive(player.IsDead);
    }

    void OnDisable()
    {
        if (player == null)
            return;

        player.HealthChanged -= OnHealthChanged;
        player.Died -= OnPlayerDied;
        player.WeaponChanged -= OnWeaponChanged;
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
        gameOverRoot.SetActive(true);
    }

    void BuildCanvas()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

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
        RectTransform dimRect = dim.rectTransform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;

        Text label = CreateText("Message", dimRect, gameOverMessage, 96, gameOverColor, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyle.Bold;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        gameOverRoot = dim.gameObject;
        gameOverRoot.SetActive(false);
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
