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
        // Custom spawn logic to prevent spawning in void if no NetworkStartPosition exists
        Transform startPos = GetStartPosition();
        GameObject player;

        if (startPos != null)
        {
            player = Instantiate(playerPrefab, startPos.position, startPos.rotation);
        }
        else
        {
            // Fallback to a safe height if no spawn point is found
            Debug.LogWarning("[NetworkManager] No NetworkStartPosition found! Spawning at safe default (0, 2, 0).");
            player = Instantiate(playerPrefab, new Vector3(0, 2, 0), Quaternion.identity);
        }

        NetworkServer.AddPlayerForConnection(conn, player);
        
        Debug.Log($"[NetworkManager] Player Added: {conn.connectionId}");
    }

}
