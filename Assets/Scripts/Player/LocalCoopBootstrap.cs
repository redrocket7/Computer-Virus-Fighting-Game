using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns / pairs a second local player when two gamepads are connected.
/// Shared-camera co-op only — no networking.
/// </summary>
public class LocalCoopBootstrap : MonoBehaviour
{
    public static LocalCoopBootstrap Instance { get; private set; }

    public const int MaxLocalPlayers = 2;

    [SerializeField] PlayerController playerPrefab;
    [SerializeField] Color playerTwoTint = new Color(0.35f, 0.75f, 1f, 1f);

    bool coopActive;

    public bool IsCoopActive => coopActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<LocalCoopBootstrap>() != null)
            return;

        // Only for the main dungeon / single-player scene flow.
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != "SinglePlayer" && scene != "Gameplay")
            return;

        var go = new GameObject("Local Coop Bootstrap");
        go.AddComponent<LocalCoopBootstrap>();
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        TryEnableCoop();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void TryEnableCoop()
    {
        if (coopActive)
            return;

        PlayerController primary = PlayerRegistry.GetPrimary();
        if (primary == null)
            primary = FindAnyObjectByType<PlayerController>();

        if (primary == null)
            return;

        primary.ConfigureLocalPlayer(0, forceGamepadOnly: Gamepad.all.Count >= 1);

        if (Gamepad.all.Count < 2)
        {
            Debug.Log("Local co-op: fewer than 2 gamepads — single player.", this);
            BindCameraAndHud();
            return;
        }

        if (PlayerRegistry.Count >= 2)
        {
            coopActive = true;
            BindCameraAndHud();
            return;
        }

        PlayerController secondary = SpawnSecondPlayer(primary);
        if (secondary == null)
            return;

        primary.ConfigureLocalPlayer(0, forceGamepadOnly: true);
        secondary.ConfigureLocalPlayer(1, forceGamepadOnly: true);
        ApplyTint(secondary, playerTwoTint);

        coopActive = true;
        BindCameraAndHud();
        FindAnyObjectByType<DungeonGenerator>()?.RepositionRegisteredPlayers();
        Debug.Log("Local co-op: 2 gamepads paired (P1 pad0, P2 pad1).", this);
    }

    PlayerController SpawnSecondPlayer(PlayerController primary)
    {
        PlayerController prefabSource = playerPrefab != null ? playerPrefab : primary;
        GameObject instance = Instantiate(prefabSource.gameObject, primary.transform.position, primary.transform.rotation);
        instance.name = "Player 2";

        PlayerController secondary = instance.GetComponent<PlayerController>();
        if (secondary == null)
        {
            Destroy(instance);
            return null;
        }

        PlayerInput secondaryInput = instance.GetComponent<PlayerInput>();
        if (secondaryInput != null && secondaryInput.actions != null)
            secondaryInput.actions = Instantiate(secondaryInput.actions);

        // Offset slightly so they don't overlap before dungeon place.
        secondary.TeleportTo(primary.transform.position + Vector3.right * 1.5f);
        return secondary;
    }

    static void ApplyTint(PlayerController player, Color tint)
    {
        Renderer[] renderers = player.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material[] materials = renderer.materials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null)
                    continue;

                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", tint);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", tint);
            }

            renderer.materials = materials;
        }
    }

    static void BindCameraAndHud()
    {
        TopDownCameraFollow cameraFollow = FindAnyObjectByType<TopDownCameraFollow>();
        if (cameraFollow != null)
            cameraFollow.UsePlayerRegistry();

        PlayerHUD.EnsureExists();
        PlayerHUD hud = FindAnyObjectByType<PlayerHUD>();
        hud?.RebuildForAllPlayers();

        LowHealthEffect.EnsureExists();
    }
}
