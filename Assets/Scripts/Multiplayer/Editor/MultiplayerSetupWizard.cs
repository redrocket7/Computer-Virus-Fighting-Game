#if UNITY_EDITOR
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates lobby/arena scenes, networked player prefab, and wires NetworkManager + build settings.
/// </summary>
public static class MultiplayerSetupWizard
{
    const string RootFolder = "Assets/Multiplayer";
    const string PrefabFolder = RootFolder + "/Prefabs";
    const string SceneFolder = RootFolder + "/Scenes";
    const string PlayerPrefabPath = PrefabFolder + "/NetworkPlayer.prefab";
    const string LobbyScenePath = SceneFolder + "/MultiplayerLobby.unity";
    const string ArenaScenePath = SceneFolder + "/MultiplayerArena.unity";

    [MenuItem("Tools/Virus Game/Setup Multiplayer Scaffold")]
    public static void RunFromMenu()
    {
        Run();
        EditorUtility.DisplayDialog(
            "Multiplayer Scaffold",
            "Created lobby + arena scenes and NetworkPlayer prefab.\n\n" +
            "1) Open MultiplayerLobby\n" +
            "2) Press Play, click Host\n" +
            "3) Build & Run a second instance (or Multiplayer Play Mode) and Join 127.0.0.1",
            "OK");
    }

    [MenuItem("Tools/Virus Game/Rebuild Network Player Prefab")]
    public static void RebuildNetworkPlayerPrefabFromMenu()
    {
        EnsureFolders();
        GameObject playerPrefab = CreateOrUpdatePlayerPrefab();
        if (playerPrefab == null)
        {
            EditorUtility.DisplayDialog("Network Player", "Failed to rebuild NetworkPlayer prefab.", "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "Network Player",
            "Rebuilt NetworkPlayer from Assets/Prefabs/Player.prefab.\n\n" +
            "If the lobby NetworkManager still points at an old prefab, run Setup Multiplayer Scaffold once.",
            "OK");
    }

    [InitializeOnLoadMethod]
    static void AutoSetupIfMissing()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (File.Exists(PlayerPrefabPath) && File.Exists(LobbyScenePath) && File.Exists(ArenaScenePath))
                return;

            // Wait until NGO types are available after package import.
            if (System.Type.GetType("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime") == null)
                return;

            try
            {
                Run();
                Debug.Log("Multiplayer scaffold auto-created under Assets/Multiplayer.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Multiplayer scaffold auto-setup skipped: {ex.Message}");
            }
        };
    }

    public static void Run()
    {
        EnsureFolders();
        GameObject playerPrefab = CreateOrUpdatePlayerPrefab();
        CreateLobbyScene(playerPrefab);
        CreateArenaScene();
        EnsureBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(RootFolder))
            AssetDatabase.CreateFolder("Assets", "Multiplayer");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder(RootFolder, "Prefabs");
        if (!AssetDatabase.IsValidFolder(SceneFolder))
            AssetDatabase.CreateFolder(RootFolder, "Scenes");
    }

    static GameObject CreateOrUpdatePlayerPrefab()
    {
        const string SourcePlayerPrefab = "Assets/Prefabs/Player.prefab";
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePlayerPrefab);
        if (source == null)
        {
            Debug.LogError($"Missing {SourcePlayerPrefab}. Cannot build NetworkPlayer.");
            return null;
        }

        GameObject root = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (root == null)
            root = Object.Instantiate(source);

        root.name = "NetworkPlayer";

        var motor = root.GetComponent<NetworkPlayerMotor>();
        if (motor != null)
            Object.DestroyImmediate(motor);

        NetworkObject networkObject = root.GetComponent<NetworkObject>();
        if (networkObject == null)
            networkObject = root.AddComponent<NetworkObject>();

        SerializedObject networkObjectSo = new SerializedObject(networkObject);
        SerializedProperty inScenePlaced = networkObjectSo.FindProperty("m_InScenePlaced");
        if (inScenePlaced != null)
            inScenePlaced.boolValue = false;
        networkObjectSo.ApplyModifiedPropertiesWithoutUndo();

        if (root.GetComponent<ClientNetworkTransform>() == null)
            root.AddComponent<ClientNetworkTransform>();

        // Physics-driven owner movement; remotes are made kinematic at runtime.
        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.constraints |= RigidbodyConstraints.FreezePositionY;
        }

        if (root.GetComponent<PlayerController>() == null)
            Debug.LogWarning("NetworkPlayer is missing PlayerController after cloning Player prefab.");

        if (root.GetComponent<PlayerInput>() == null)
            Debug.LogWarning("NetworkPlayer is missing PlayerInput after cloning Player prefab.");

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateLobbyScene(GameObject playerPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.07f, 0.09f);
        cam.transform.position = new Vector3(0f, 1f, -10f);
        camGo.AddComponent<AudioListener>();

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var networkGo = new GameObject("NetworkManager");
        var networkManager = networkGo.AddComponent<NetworkManager>();
        var transport = networkGo.AddComponent<UnityTransport>();
        // NGO does not auto-bind a sibling transport; StartHost fails without this assignment.
        networkManager.NetworkConfig.NetworkTransport = transport;
        TryAddNetworkPrefab(networkManager, playerPrefab);

        var lobbyUi = new GameObject("Multiplayer Lobby UI");
        lobbyUi.AddComponent<MultiplayerLobbyUI>();

        EditorSceneManager.SaveScene(scene, LobbyScenePath);
    }

    static void TryAddNetworkPrefab(NetworkManager networkManager, GameObject playerPrefab)
    {
        if (networkManager == null || playerPrefab == null)
            return;

        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
        networkManager.NetworkConfig.EnableSceneManagement = true;

        try
        {
            networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Network prefab list registration warning: {ex.Message}");
        }
    }

    static void CreateArenaScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
        cam.transform.position = new Vector3(0f, 18f, -10f);
        cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<TopDownCameraFollow>();

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        var floorRenderer = floor.GetComponent<MeshRenderer>();
        if (floorRenderer != null && floorRenderer.sharedMaterial != null)
        {
            Material mat = new Material(floorRenderer.sharedMaterial);
            mat.color = new Color(0.18f, 0.22f, 0.2f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.18f, 0.22f, 0.2f));
            floorRenderer.sharedMaterial = mat;
        }

        // Simple boundary walls.
        CreateWall("Wall North", new Vector3(0f, 1f, 20f), new Vector3(40f, 2f, 1f));
        CreateWall("Wall South", new Vector3(0f, 1f, -20f), new Vector3(40f, 2f, 1f));
        CreateWall("Wall East", new Vector3(20f, 1f, 0f), new Vector3(1f, 2f, 40f));
        CreateWall("Wall West", new Vector3(-20f, 1f, 0f), new Vector3(1f, 2f, 40f));

        var hud = new GameObject("Arena Systems", typeof(Canvas));
        hud.AddComponent<MultiplayerArenaHud>();

        EditorSceneManager.SaveScene(scene, ArenaScenePath);
    }

    static void CreateWall(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
    }

    static void EnsureBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        void Ensure(string path)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == path)
                {
                    scenes[i] = new EditorBuildSettingsScene(path, true);
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        // Keep Gameplay first if present.
        Ensure("Assets/Scenes/Gameplay.unity");
        Ensure(LobbyScenePath);
        Ensure(ArenaScenePath);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
