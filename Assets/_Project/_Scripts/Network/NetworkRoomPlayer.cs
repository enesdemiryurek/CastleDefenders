using Mirror;
using UnityEngine;
using System;

public class NetworkRoomPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(HandleReadyStatusChanged))]
    public bool IsReady = false;

    [SyncVar(hook = nameof(HandleDisplayNameChanged))]
    public string DisplayName = "Loading...";

    public static event Action<NetworkRoomPlayer> OnPlayerSpawned;
    public static event Action<NetworkRoomPlayer> OnPlayerDespawned;
    public static event Action<NetworkRoomPlayer> OnPlayerResync;

    public override void OnStartClient()
    {
        OnPlayerSpawned?.Invoke(this);
    }

    public override void OnStopClient()
    {
        OnPlayerDespawned?.Invoke(this);
    }

    [Command]
    public void CmdSetReady(bool state)
    {
        IsReady = state;
    }

    [Command]
    public void CmdSetDisplayName(string newName)
    {
        DisplayName = newName;
    }

    void HandleReadyStatusChanged(bool oldValue, bool newValue)
    {
        OnPlayerResync?.Invoke(this);
    }

    void HandleDisplayNameChanged(string oldValue, string newValue)
    {
        OnPlayerResync?.Invoke(this);
    }
}
