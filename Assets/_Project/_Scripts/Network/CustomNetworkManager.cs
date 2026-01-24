using Mirror;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("[NetworkManager] Server Started");
        GameManager.Instance.SetGameState(GameManager.GameState.Lobby);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("[NetworkManager] Server Stopped");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[NetworkManager] Client Connected");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("[NetworkManager] Client Disconnected");
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Basic implementation: Spawn the player prefab
        // In the future, this can be customized to spawn different prefabs based on GameState
        base.OnServerAddPlayer(conn);
        
        Debug.Log($"[NetworkManager] Player Added: {conn.connectionId}");
    }

}
