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
    [SerializeField] private GameObject defeatPanel; // Bozgun Ekranı

    private int playersInZone = 0;
    private bool isLevelActive = true;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Periyodik kontrol başlat
        if (isServer)
        {
            InvokeRepeating(nameof(CheckDefeatCondition), 5f, 1f);
        }
    }

    [Server]
    public void PlayerEnteredWinZone()
    {
        if (!isLevelActive) return;

        playersInZone++;
        Debug.Log($"[LevelManager] Player entered zone. ({playersInZone}/{NetworkServer.connections.Count})");
        CheckWinCondition();
    }

    [Server]
    public void PlayerExitedWinZone()
    {
        if (!isLevelActive) return;

        playersInZone--;
        Debug.Log($"[LevelManager] Player left zone. ({playersInZone}/{NetworkServer.connections.Count})");
    }

    [Server]
    private void CheckWinCondition()
    {
        if (!isLevelActive) return;

        // Tüm oyuncular bölgede mi?
        if (playersInZone >= NetworkServer.connections.Count && NetworkServer.connections.Count > 0)
        {
            Debug.Log("[LevelManager] VICTORY! All players reached the goal.");
            Victory();
        }
    }

    [Server]
    private void CheckDefeatCondition()
    {
        if (!isLevelActive) return;

        // Sahnedeki tüm oyuncuları bul
        PlayerController[] activePlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        
        // Eğer hiç oyuncu kalmadıysa (hepsi öldü ve yok olduysa) VE oyun başlamışsa (connection var)
        if (activePlayers.Length == 0 && NetworkServer.connections.Count > 0)
        {
            Debug.Log("[LevelManager] DEFEAT! All players are dead.");
            Defeat();
        }
    }

    [Server]
    private void Victory()
    {
        isLevelActive = false;
        RpcShowVictory();
        StartCoroutine(LoadNextLevelRoutine());
    }

    [Server]
    private void Defeat()
    {
        isLevelActive = false;
        RpcShowDefeat();
        // İsteğe bağlı: Sahneyi yeniden başlat veya Lobby'e dön
        // StartCoroutine(RestartLevelRoutine());
    }

    [ClientRpc]
    private void RpcShowVictory()
    {
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }
    }

    [ClientRpc]
    private void RpcShowDefeat()
    {
        if (defeatPanel != null)
        {
            defeatPanel.SetActive(true);
        }
    }

    [Server]
    private IEnumerator LoadNextLevelRoutine()
    {
        yield return new WaitForSeconds(winDelay);
        NetworkManager.singleton.ServerChangeScene(nextSceneName);
    }
}
