using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime Host / Join lobby UI for the multiplayer movement prototype.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class MultiplayerLobbyUI : MonoBehaviour
{
    [SerializeField] string defaultAddress = MultiplayerSession.DefaultAddress;
    [SerializeField] ushort defaultPort = MultiplayerSession.DefaultPort;

    InputField addressField;
    InputField portField;
    Text statusLabel;
    Font font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapAfterSceneLoad()
    {
        if (!IsLobbyScene())
            return;

        if (FindAnyObjectByType<MultiplayerLobbyUI>() != null)
            return;

        var go = new GameObject("Multiplayer Lobby UI", typeof(Canvas));
        go.AddComponent<MultiplayerLobbyUI>();
    }

    static bool IsLobbyScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.name == MultiplayerSession.LobbySceneName;
    }

    void Awake()
    {
        MultiplayerSession.EnsureSingleton();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        BuildUi();
        RefreshStatus(MultiplayerSession.EnsureReadyMessage() ?? "Ready. Host on one instance, Join from another.");
    }

    void OnEnable()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            RefreshStatus($"Connected as client #{clientId}. Waiting for arena...");
        else
            RefreshStatus($"Client #{clientId} connected.");
    }

    void OnClientDisconnected(ulong clientId)
    {
        RefreshStatus($"Client #{clientId} disconnected.");
    }

    void BuildCanvas()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

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

    void BuildUi()
    {
        Image panel = CreateImage("Panel", transform, new Color(0.05f, 0.07f, 0.09f, 0.94f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(520f, 500f);

        Text title = CreateText("Title", panelRect, "MULTIPLAYER LOBBY", 34, new Color(0.55f, 0.95f, 0.78f), TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -70f), new Vector2(-20f, -16f));

        Text subtitle = CreateText(
            "Subtitle",
            panelRect,
            "LAN prototype · WASD / arrows to move in the arena",
            18,
            new Color(0.8f, 0.85f, 0.88f),
            TextAnchor.MiddleCenter);
        SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -110f), new Vector2(-20f, -70f));

        CreateText("Address Label", panelRect, "Host Address", 16, Color.white, TextAnchor.MiddleLeft);
        SetRect(
            panelRect.Find("Address Label").GetComponent<RectTransform>(),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(36f, -150f),
            new Vector2(-36f, -120f));

        addressField = CreateInputField("Address Field", panelRect, defaultAddress);
        SetRect(addressField.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(36f, -190f), new Vector2(-36f, -150f));

        CreateText("Port Label", panelRect, "Port", 16, Color.white, TextAnchor.MiddleLeft);
        SetRect(
            panelRect.Find("Port Label").GetComponent<RectTransform>(),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(36f, -230f),
            new Vector2(-36f, -200f));

        portField = CreateInputField("Port Field", panelRect, defaultPort.ToString());
        SetRect(portField.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(36f, -270f), new Vector2(-36f, -230f));

        Button hostButton = CreateButton("Host Button", panelRect, "HOST", new Color(0.18f, 0.45f, 0.32f, 1f));
        SetRect(hostButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(36f, 100f), new Vector2(-8f, 155f));
        hostButton.onClick.AddListener(OnHostClicked);

        Button joinButton = CreateButton("Join Button", panelRect, "JOIN", new Color(0.2f, 0.35f, 0.55f, 1f));
        SetRect(joinButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(8f, 100f), new Vector2(-36f, 155f));
        joinButton.onClick.AddListener(OnJoinClicked);

        Button backButton = CreateButton("Back Button", panelRect, "BACK TO GAME", new Color(0.25f, 0.25f, 0.28f, 1f));
        SetRect(backButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(36f, 45f), new Vector2(-36f, 90f));
        backButton.onClick.AddListener(() => SceneManager.LoadScene("Gameplay"));

        statusLabel = CreateText("Status", panelRect, string.Empty, 16, new Color(0.95f, 0.82f, 0.35f), TextAnchor.MiddleCenter);
        statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        SetRect(statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 8f), new Vector2(-28f, 40f));
    }

    void OnHostClicked()
    {
        MultiplayerSession.EnsureSingleton();

        string readyError = MultiplayerSession.EnsureReadyMessage();
        if (readyError != null)
        {
            RefreshStatus(readyError);
            return;
        }

        if (!TryReadConnection(out string address, out ushort port, out string parseError))
        {
            RefreshStatus(parseError);
            return;
        }

        if (!MultiplayerSession.TryStartHost(address, port, out string error))
        {
            RefreshStatus(error);
            return;
        }

        HideLobbyUi();
        RefreshStatus($"Hosting on {address}:{port}...");
    }

    void OnJoinClicked()
    {
        MultiplayerSession.EnsureSingleton();

        string readyError = MultiplayerSession.EnsureReadyMessage();
        if (readyError != null)
        {
            RefreshStatus(readyError);
            return;
        }

        if (!TryReadConnection(out string address, out ushort port, out string parseError))
        {
            RefreshStatus(parseError);
            return;
        }

        if (!MultiplayerSession.TryStartClient(address, port, out string error))
        {
            RefreshStatus(error);
            return;
        }

        HideLobbyUi();
        RefreshStatus($"Connecting to {address}:{port}...");
    }

    void HideLobbyUi()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = false;
    }

    bool TryReadConnection(out string address, out ushort port, out string error)
    {
        address = addressField != null ? addressField.text : defaultAddress;
        string portText = portField != null ? portField.text : defaultPort.ToString();
        if (!ushort.TryParse(portText, out port) || port == 0)
        {
            error = "Port must be a number from 1 to 65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            error = "Address cannot be empty.";
            return false;
        }

        error = null;
        return true;
    }

    void RefreshStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message ?? string.Empty;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
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

    InputField CreateInputField(string name, Transform parent, string value)
    {
        Image background = CreateImage(name, parent, new Color(0.12f, 0.14f, 0.16f, 1f));
        Text text = CreateText("Text", background.transform, value, 20, Color.white, TextAnchor.MiddleLeft);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);

        Text placeholder = CreateText("Placeholder", background.transform, string.Empty, 20, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
        RectTransform placeholderRect = placeholder.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(12f, 4f);
        placeholderRect.offsetMax = new Vector2(-12f, -4f);

        var input = background.gameObject.AddComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value;
        input.caretColor = Color.white;
        return input;
    }

    Button CreateButton(string name, Transform parent, string label, Color color)
    {
        Image background = CreateImage(name, parent, color);
        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        Text text = CreateText("Label", background.transform, label, 22, Color.white, TextAnchor.MiddleCenter);
        text.fontStyle = FontStyle.Bold;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }
}
