using UnityEngine;
using UnityEngine.UI;
using Mirror;
using TMPro;
using System.Collections.Generic;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform playerListParent;
    [SerializeField] private GameObject playerListEntryPrefab;
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonText;
    [SerializeField] private Button startButton;

    private void OnEnable()
    {
        NetworkRoomPlayer.OnPlayerSpawned += HandlePlayerSpawned;
        NetworkRoomPlayer.OnPlayerDespawned += HandlePlayerDespawned;
        NetworkRoomPlayer.OnPlayerResync += HandlePlayerResync;
        
        if(readyButton) readyButton.onClick.AddListener(OnReadyClicked);
        if(startButton) startButton.onClick.AddListener(OnStartClicked);
        
        UpdateUI();
    }

    private void OnDisable()
    {
        NetworkRoomPlayer.OnPlayerSpawned -= HandlePlayerSpawned;
        NetworkRoomPlayer.OnPlayerDespawned -= HandlePlayerDespawned;
        NetworkRoomPlayer.OnPlayerResync -= HandlePlayerResync;
        
        if(readyButton) readyButton.onClick.RemoveListener(OnReadyClicked);
        if(startButton) startButton.onClick.RemoveListener(OnStartClicked);
    }

    private void HandlePlayerSpawned(NetworkRoomPlayer player) => UpdateUI();
    private void HandlePlayerDespawned(NetworkRoomPlayer player) => UpdateUI();
    private void HandlePlayerResync(NetworkRoomPlayer player) => UpdateUI();

    private void UpdateUI()
    {
        // Clear current list
        foreach (Transform child in playerListParent)
        {
            Destroy(child.gameObject);
        }

        bool allReady = true;
        var players = FindObjectsByType<NetworkRoomPlayer>(FindObjectsSortMode.None);

        foreach (var player in players)
        {
            GameObject entry = Instantiate(playerListEntryPrefab, playerListParent);
            TMP_Text infoText = entry.GetComponentInChildren<TMP_Text>();
            
            string status = player.IsReady ? "<color=green>READY</color>" : "<color=red>NOT READY</color>";
            if(infoText) infoText.text = $"{player.DisplayName} - {status}";

            if (!player.IsReady) allReady = false;
        }

        // Host Logic for Start Button
        if (NetworkServer.active && players.Length > 0)
        {
            startButton.gameObject.SetActive(true);
            startButton.interactable = allReady;
        }
        else
        {
            startButton.gameObject.SetActive(false);
        }
    }

    private void OnReadyClicked()
    {
        var players = FindObjectsByType<NetworkRoomPlayer>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            if (player.isLocalPlayer)
            {
                player.CmdSetReady(!player.IsReady);
                readyButtonText.text = !player.IsReady ? "NOT READY" : "READY"; // Optimistic update
                break;
            }
        }
    }

    private void OnStartClicked()
    {
        // Manager should handle scene switch
        NetworkManager.singleton.ServerChangeScene("Game");
    }
}
