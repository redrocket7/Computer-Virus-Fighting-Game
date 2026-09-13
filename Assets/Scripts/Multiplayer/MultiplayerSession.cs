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

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            error = "UnityTransport missing on NetworkManager.";
            return false;
        }

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
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene(LobbySceneName);
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
