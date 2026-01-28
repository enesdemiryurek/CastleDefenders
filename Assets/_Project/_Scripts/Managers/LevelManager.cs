using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LevelManager : NetworkBehaviour
{
    public static LevelManager Instance;

    [Header("Settings")]
    [SerializeField] private string nextSceneName = "TheKeep";
    [SerializeField] private float winDelay = 5f;

    [Header("UI References")]
    [SerializeField] private GameObject victoryPanel; // Zafer Ekranı (Panel)
    [SerializeField] private GameObject objectiveText; // "Kaleye Ulaş" yazısı

    private int playersInZone = 0;

    private void Awake()
    {
        Instance = this;
    }

    [Server]
    public void PlayerEnteredWinZone()
    {
        playersInZone++;
        Debug.Log($"[LevelManager] Player entered zone. ({playersInZone}/{NetworkServer.connections.Count})");
        CheckWinCondition();
    }

    [Server]
    public void PlayerExitedWinZone()
    {
        playersInZone--;
        Debug.Log($"[LevelManager] Player left zone. ({playersInZone}/{NetworkServer.connections.Count})");
    }

    [Server]
    private void CheckWinCondition()
    {
        // Tüm oyuncular bölgede mi?
        // NetworkServer.connections.Count aktif bağlantı sayısını verir.
        if (playersInZone >= NetworkServer.connections.Count)
        {
            Debug.Log("[LevelManager] VICTORY! All players reached the goal.");
            RpcShowVictory();
            StartCoroutine(LoadNextLevelRoutine());
        }
    }

    [ClientRpc]
    private void RpcShowVictory()
    {
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
            // Burada güzel bir ses veya efekt de çalınabilir
        }
    }

    [Server]
    private IEnumerator LoadNextLevelRoutine()
    {
        yield return new WaitForSeconds(winDelay);
        
        // "TheKeep" sahnesine dön
        NetworkManager.singleton.ServerChangeScene(nextSceneName);
    }
}
