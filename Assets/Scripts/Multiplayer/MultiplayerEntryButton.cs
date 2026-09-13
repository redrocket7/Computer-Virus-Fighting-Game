using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tiny Gameplay hook so you can open the multiplayer lobby from a running game.
/// Press F8, or use the on-screen button.
/// </summary>
public class MultiplayerEntryButton : MonoBehaviour
{
    Font font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != "Gameplay")
            return;

        if (FindAnyObjectByType<MultiplayerEntryButton>() != null)
            return;

        var go = new GameObject("Multiplayer Entry", typeof(Canvas));
        go.AddComponent<MultiplayerEntryButton>();
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        var buttonGo = new GameObject("Open Lobby", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(transform, false);
        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.22f, 0.85f);
        RectTransform rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(200f, 44f);

        var button = buttonGo.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(OpenLobby);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(buttonGo.transform, false);
        var text = labelGo.GetComponent<Text>();
        text.font = font;
        text.text = "MULTIPLAYER (F8)";
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        RectTransform labelRect = text.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    void Update()
    {
        KeyboardOpen();
    }

    void KeyboardOpen()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
            OpenLobby();
    }

    static void OpenLobby()
    {
        if (!Application.CanStreamedLevelBeLoaded(MultiplayerSession.LobbySceneName))
        {
            Debug.LogError(
                "MultiplayerLobby scene is missing from Build Settings. " +
                "In Unity, run Tools > Virus Game > Setup Multiplayer Scaffold.");
            return;
        }

        SceneManager.LoadScene(MultiplayerSession.LobbySceneName);
    }
}
