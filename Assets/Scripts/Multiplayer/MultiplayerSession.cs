using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Host / join helpers for the LAN multiplayer prototype (Unity Transport).
/// </summary>
public static class MultiplayerSession
{
    public const string LobbySceneName = "MultiplayerLobby";
    public const string ArenaSceneName = "MultiplayerArena";
    public const ushort DefaultPort = 7777;
    public const string DefaultAddress = "127.0.0.1";

    public static bool IsReady =>
        NetworkManager.Singleton != null &&
        NetworkManager.Singleton.NetworkConfig != null &&
        NetworkManager.Singleton.NetworkConfig.PlayerPrefab != null;

    public static string EnsureReadyMessage()
    {
        if (NetworkManager.Singleton == null)
            return "NetworkManager missing. Open Unity and run Tools > Virus Game > Setup Multiplayer Scaffold.";

        if (NetworkManager.Singleton.NetworkConfig.PlayerPrefab == null)
            return "Player prefab not assigned on NetworkManager. Run Tools > Virus Game > Setup Multiplayer Scaffold.";

        if (NetworkManager.Singleton.GetComponent<UnityTransport>() == null &&
            NetworkManager.Singleton.NetworkConfig.NetworkTransport == null)
            return "UnityTransport missing on NetworkManager. Run Tools > Virus Game > Setup Multiplayer Scaffold.";

        return null;
    }

    public static bool TryConfigureTransport(string address, ushort port, out string error)
    {
        error = null;
        if (NetworkManager.Singleton == null)
        {
            error = EnsureReadyMessage();
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        var transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            error = "UnityTransport missing on NetworkManager.";
            return false;
        }

        // Scaffolded scenes sometimes leave NetworkConfig.NetworkTransport unassigned even when
        // UnityTransport exists on the same GameObject. Bind it before StartHost/StartClient.
        if (networkManager.NetworkConfig.NetworkTransport == null)
            networkManager.NetworkConfig.NetworkTransport = transport;

        transport.SetConnectionData(
            string.IsNullOrWhiteSpace(address) ? DefaultAddress : address.Trim(),
            port == 0 ? DefaultPort : port);
        return true;
    }

    public static bool TryStartHost(string address, ushort port, out string error)
    {
        if (!TryConfigureTransport(address, port, out error))
            return false;

        if (!NetworkManager.Singleton.StartHost())
        {
            error = "StartHost failed.";
            return false;
        }

        LoadArenaAsHost();
        return true;
    }

    public static bool TryStartClient(string address, ushort port, out string error)
    {
        if (!TryConfigureTransport(address, port, out error))
            return false;

        if (!NetworkManager.Singleton.StartClient())
        {
            error = "StartClient failed.";
            return false;
        }

        return true;
    }

    public static void ShutdownAndReturnToLobby()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            SceneManager.LoadScene(LobbySceneName);
            return;
        }

        // Shutdown is deferred to NGO's update loop. Wait for stop callbacks before
        // destroying the DontDestroyOnLoad NetworkManager and loading the lobby.
        if (networkManager.IsListening)
        {
            bool returning = false;

            void ReturnToLobby(bool _)
            {
                if (returning)
                    return;
                returning = true;

                networkManager.OnClientStopped -= ReturnToLobby;
                networkManager.OnServerStopped -= ReturnToLobby;

                DestroyNetworkManagerAndLoadLobby(networkManager);
            }

            networkManager.OnClientStopped += ReturnToLobby;
            networkManager.OnServerStopped += ReturnToLobby;
            networkManager.Shutdown(true);
            return;
        }

        DestroyNetworkManagerAndLoadLobby(networkManager);
    }

    static void DestroyNetworkManagerAndLoadLobby(NetworkManager networkManager)
    {
        // Must destroy immediately. A deferred Destroy leaves the old Singleton alive
        // during LoadScene, so the lobby NetworkManager skips SetSingleton(); then the
        // old one clears Singleton on destroy and Host sees "NetworkManager missing".
        if (networkManager != null)
            Object.DestroyImmediate(networkManager.gameObject);

        SceneManager.LoadScene(LobbySceneName);
    }

    /// <summary>
    /// Makes sure the active scene's NetworkManager owns Singleton after scene reloads.
    /// </summary>
    public static void EnsureSingleton()
    {
        if (NetworkManager.Singleton != null)
            return;

        NetworkManager networkManager = Object.FindAnyObjectByType<NetworkManager>();
        if (networkManager != null)
            networkManager.SetSingleton();
    }

    static void LoadArenaAsHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        var sceneManager = NetworkManager.Singleton.SceneManager;
        if (sceneManager != null)
        {
            sceneManager.LoadScene(ArenaSceneName, LoadSceneMode.Single);
            return;
        }

        SceneManager.LoadScene(ArenaSceneName);
    }
}
