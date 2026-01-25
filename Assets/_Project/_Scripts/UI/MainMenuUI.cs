using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button settingsBackButton; // Yeni Geri Tuşu

    [Header("Panels")]
    [SerializeField] private GameObject settingsPanel;

    private void Start()
    {
        // Buton dinleyicilerini ayarla
        playButton.onClick.AddListener(OnPlayClicked);
        settingsButton.onClick.AddListener(OnSettingsClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
        
        if (settingsBackButton != null)
            settingsBackButton.onClick.AddListener(OnSettingsBackClicked);

        // Başlangıçta paneller kapalı olsun
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    private void OnSettingsBackClicked()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    private void OnPlayClicked()
    {
        Debug.Log("Play Clicked - Going to Lobby/Host Screen");
        // BURASI ÖNEMLİ: Şimdilik direk Lobby sahnesine atmıyoruz çünkü NetworkManager lazım.
        // İleride buraya "Host / Join" paneli açtıracağız.
        // Geçici olarak:
        // SceneManager.LoadScene("Lobby"); 
    }

    private void OnSettingsClicked()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(!settingsPanel.activeSelf);
        }
    }

    private void OnQuitClicked()
    {
        Debug.Log("Quit Game");
        Application.Quit();
    }
}
