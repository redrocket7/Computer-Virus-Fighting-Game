using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Arena HUD: connection status + disconnect, and a simple follow camera for the local player.
/// </summary>
public class MultiplayerArenaHud : MonoBehaviour
{
    Text statusLabel;
    Font font;
    Transform followTarget;
    Vector3 cameraOffset = new Vector3(0f, 18f, -10f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapAfterSceneLoad()
    {
        if (SceneManager.GetActiveScene().name != MultiplayerSession.ArenaSceneName)
            return;

        if (FindAnyObjectByType<MultiplayerArenaHud>() != null)
            return;

        var go = new GameObject("Multiplayer Arena HUD", typeof(Canvas));
        go.AddComponent<MultiplayerArenaHud>();
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUi();
    }

    void Update()
    {
        if (followTarget == null)
            TryFindLocalPlayer();

        Camera cam = Camera.main;
        if (cam != null && followTarget != null)
        {
            Vector3 desired = followTarget.position + cameraOffset;
            cam.transform.position = Vector3.Lerp(cam.transform.position, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
            cam.transform.rotation = Quaternion.LookRotation(followTarget.position - cam.transform.position, Vector3.up);
        }

        if (statusLabel == null)
            return;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            statusLabel.text = "Disconnected";
            return;
        }

        int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        string role = NetworkManager.Singleton.IsHost ? "HOST" : "CLIENT";
        statusLabel.text = $"{role}  ·  players {count}  ·  WASD to move";
    }

    void TryFindLocalPlayer()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return;

        NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObject != null)
            followTarget = playerObject.transform;
    }

    void BuildUi()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        statusLabel = CreateText("Status", transform, "Connecting...", 22, Color.white, TextAnchor.UpperLeft);
        RectTransform statusRect = statusLabel.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(0f, 1f);
        statusRect.pivot = new Vector2(0f, 1f);
        statusRect.anchoredPosition = new Vector2(24f, -24f);
        statusRect.sizeDelta = new Vector2(640f, 40f);

        var leaveGo = new GameObject("Leave", typeof(RectTransform), typeof(Image), typeof(Button));
        leaveGo.transform.SetParent(transform, false);
        var leaveImage = leaveGo.GetComponent<Image>();
        leaveImage.color = new Color(0.25f, 0.15f, 0.15f, 0.9f);
        RectTransform leaveRect = leaveGo.GetComponent<RectTransform>();
        leaveRect.anchorMin = new Vector2(1f, 1f);
        leaveRect.anchorMax = new Vector2(1f, 1f);
        leaveRect.pivot = new Vector2(1f, 1f);
        leaveRect.anchoredPosition = new Vector2(-24f, -24f);
        leaveRect.sizeDelta = new Vector2(160f, 44f);
        var leaveButton = leaveGo.GetComponent<Button>();
        leaveButton.targetGraphic = leaveImage;
        leaveButton.onClick.AddListener(MultiplayerSession.ShutdownAndReturnToLobby);

        Text leaveLabel = CreateText("Label", leaveRect, "LEAVE", 20, Color.white, TextAnchor.MiddleCenter);
        leaveLabel.fontStyle = FontStyle.Bold;
        RectTransform leaveLabelRect = leaveLabel.rectTransform;
        leaveLabelRect.anchorMin = Vector2.zero;
        leaveLabelRect.anchorMax = Vector2.one;
        leaveLabelRect.offsetMin = Vector2.zero;
        leaveLabelRect.offsetMax = Vector2.zero;
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
