using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class LevelSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject panel; // T ile açılacak panel
    [SerializeField] private Button level1Button; // Örn: Game Sahnesi
    [SerializeField] private Button level2Button; // Örn: Forest Sahnesi (İlerde)

    [Header("Scene Names")]
    [SerializeField] private string level1SceneName = "Game";
    // [SerializeField] private string level2SceneName = "ForestMap";

    private bool isVisible = false;

    private void Start()
    {
        if (panel != null) panel.SetActive(false);

        // Butonları ayarla
        if (level1Button != null) 
            level1Button.onClick.AddListener(() => LoadLevel(level1SceneName));
            
        // if (level2Button != null) level2Button.onClick.AddListener(() => LoadLevel(level2SceneName));
    }

    private void Update()
    {
        // T tuşu ile aç/kapa
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleVisibility();
        }
    }

    private void ToggleVisibility()
    {
        isVisible = !isVisible;
        if (panel != null) panel.SetActive(isVisible);

        // Mouse kontrolü (Eğer J paneli kapalıysa mouse'u yönet)
        // Not: İki panel aynı anda açılırsa çakışabilir, basit tutuyoruz.
        if (isVisible)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            // Panel kapanınca mouse'u kilitle (TPS modu için)
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    private void LoadLevel(string sceneName)
    {
        // Sadece HOST (Server) sahneyi değiştirebilir
        if (NetworkManager.singleton != null && NetworkServer.active)
        {
            Debug.Log($"Loading Level: {sceneName}");
            NetworkManager.singleton.ServerChangeScene(sceneName);
        }
        else
        {
            Debug.LogWarning("Sadece HOST bölüm başlatabilir!");
            // Belki ekrana "Only Host can start" uyarısı basılabilir
        }
    }
}
