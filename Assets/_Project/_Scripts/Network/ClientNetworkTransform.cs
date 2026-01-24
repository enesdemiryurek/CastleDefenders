using Mirror;
using UnityEngine;

public class ClientNetworkTransform : NetworkTransformReliable
{
    protected override void Awake()
    {
        base.Awake();
        // Client Authority için yönü zorla ayarla
        syncDirection = SyncDirection.ClientToServer;
    }

    // OnValidate override is sometimes needed in Editor to prevent warnings when adding to non-root objects
    protected override void OnValidate()
    {
        base.OnValidate();
    }
}
