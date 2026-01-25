using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro; // Added for TMPro.TMP_InputField

public class MainMenuUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button hostButton; // Play -> Host oldu
    [SerializeField] private Button joinButton; // Yeni Join butonu
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button settingsBackButton; // Yeni Geri Tuşu

    [Header("Inputs")]
    [SerializeField] private TMPro.TMP_InputField ipInputField; // IP Girmek için

    [Header("Panels")]
    [SerializeField] private GameObject settingsPanel;

    private void Start()
    {
        // Buton dinleyicilerini ayarla
        if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
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

    private void OnHostClicked()
    {
        Debug.Log("Host Clicked - Starting Host...");
        if (Mirror.NetworkManager.singleton != null)
        {
            Mirror.NetworkManager.singleton.StartHost();
        }
    }

    private void OnJoinClicked()
    {
        Debug.Log("Join Clicked - Connecting to Server...");
        string ipAddress = "localhost";
        
        if (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
        {
            ipAddress = ipInputField.text;
        }

        if (Mirror.NetworkManager.singleton != null)
        {
            Mirror.NetworkManager.singleton.networkAddress = ipAddress;
            Mirror.NetworkManager.singleton.StartClient();
        }
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
