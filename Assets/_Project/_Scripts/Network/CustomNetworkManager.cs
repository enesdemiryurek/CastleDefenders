using Mirror;
using UnityEngine;
using System.Collections.Generic;

public class CustomNetworkManager : NetworkManager
{
    [Header("Character Selection")]
    public List<CharacterData> characters = new List<CharacterData>();

    public struct CharacterMessage : NetworkMessage
    {
        public int characterIndex;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("[NetworkManager] Server Started");
        GameManager.Instance.SetGameState(GameManager.GameState.Lobby);

        // Register the message handler
        NetworkServer.RegisterHandler<CharacterMessage>(OnCreateCharacter);
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[NetworkManager] Client Connected");
        
        // Şimdilik varsayılan olarak ilk karakteri seçelim (İleride UI'dan gelecek)
        // Karakter seçme ekranı yapılana kadar otomatik 0. indexi yolla
        CharacterMessage characterMessage = new CharacterMessage { characterIndex = 0 };
        NetworkClient.Send(characterMessage);
    }

    // Client'tan gelen isteği karşıla
    void OnCreateCharacter(NetworkConnectionToClient conn, CharacterMessage message)
    {
        // Geçerli bir index mi?
        if (message.characterIndex < 0 || message.characterIndex >= characters.Count)
        {
            Debug.LogError($"[NetworkManager] Invalid Character Index: {message.characterIndex}");
            return;
        }

        Transform startPos = GetStartPosition();
        GameObject characterPrefab = characters[message.characterIndex].characterPrefab.gameObject;
        
        GameObject player;

        if (startPos != null)
        {
            player = Instantiate(characterPrefab, startPos.position, startPos.rotation);
        }
        else
        {
            Debug.LogWarning("[NetworkManager] No NetworkStartPosition found! Spawning at default.");
            player = Instantiate(characterPrefab, new Vector3(0, 2, 0), Quaternion.identity);
        }

        NetworkServer.AddPlayerForConnection(conn, player);
        Debug.Log($"[NetworkManager] Player Added: {conn.connectionId} as {characters[message.characterIndex].characterName}");
    }

    // NOT: Base OnServerAddPlayer'ı override etmiyoruz çünkü biz "Mesaj" ile spawn ediyoruz.
    // Ancak Mirror bazen otomatik çağırabilir, o yüzden boş bırakabiliriz veya base'i iptal edebiliriz.
    // Mirror "Auto Create Player" açıksa bu metot çalışır, kapalıysa çalışmaz.
    // Biz manuel mesajla yaptığımız için Inspector'dan "Auto Create Player" kapatılmalı!
    public override void OnServerAddPlayer(NetworkConnectionToClient conn) 
    { 
        // Boş bırakıyoruz, çünkü CharacterMessage gelince spawn edeceğiz.
    }

}
