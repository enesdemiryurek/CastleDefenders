using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioMixer audioMixer; // Master, Music, SFX kanalları için
    
    [Header("UI References")]
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    private Resolution[] resolutions;

    private void Start()
    {
        // --- ÇÖZÜNÜRLÜK AYARLARI ---
        resolutions = Screen.resolutions;
        resolutionDropdown.ClearOptions();

        List<string> options = new List<string>();
        int currentResolutionIndex = 0;

        for (int i = 0; i < resolutions.Length; i++)
        {
            string option = resolutions[i].width + " x " + resolutions[i].height;
            options.Add(option);

            if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height)
            {
                currentResolutionIndex = i;
            }
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentResolutionIndex;
        resolutionDropdown.RefreshShownValue();
    }

    // --- GRAFİK KALİTESİ ---
    // 0: Low, 1: Medium, 2: High (Unity Project Settings -> Quality sırasına göre)
    public void SetQuality(int qualityIndex)
    {
        QualitySettings.SetQualityLevel(qualityIndex);
        Debug.Log("Grafik Kalitesi: " + QualitySettings.names[qualityIndex]);
    }

    // --- TAM EKRAN ---
    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
    }

    // --- ÇÖZÜNÜRLÜK ---
    public void SetResolution(int resolutionIndex)
    {
        Resolution resolution = resolutions[resolutionIndex];
        Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreen);
    }

    // --- SES (İleride AudioMixer bağlayınca çalışır) ---
    public void SetVolume(float volume)
    {
        // Logaritmik ses ayarı (Unity Slider lineerdir, ses logaritmik çalışır)
        // audioMixer.SetFloat("MasterVolume", Mathf.Log10(volume) * 20); 
        Debug.Log($"Ses Seviyesi: {volume}");
    }
}
