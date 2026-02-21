using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Mirror;
using System.Collections;

/// <summary>
/// Video Cutscene Yöneticisi
/// Oyunu durdurup tam ekran video oynatır, video bitince oyun devam eder
/// </summary>
public class CutsceneManager : MonoBehaviour
{
    public static CutsceneManager Instance;

    [Header("UI")]
    [SerializeField] private RawImage videoScreen;        // Canvas üstündeki RawImage (tam ekran)
    [SerializeField] private Canvas cutsceneCanvas;       // Cutscene Canvas (video için)
    [SerializeField] private VideoPlayer videoPlayer;     // VideoPlayer component

    [Header("Skip")]
    [SerializeField] private GameObject skipText;         // "Atlamak için Space" yazısı
    [SerializeField] private float skipDelay = 2f;        // 2 saniye sonra atlama aktif

    private bool isPlaying = false;
    private bool canSkip = false;
    private System.Action onCutsceneEnd;

    private void Awake()
    {
        Instance = this;

        // Başlangıçta kapat
        if (cutsceneCanvas != null) cutsceneCanvas.gameObject.SetActive(false);
        if (skipText != null) skipText.SetActive(false);
    }

    private void Update()
    {
        if (!isPlaying) return;

        // Space ile atla (skipDelay sonra aktif)
        if (canSkip && Input.GetKeyDown(KeyCode.Space))
        {
            StopCutscene();
        }
    }

    /// <summary>
    /// Video oynat ve oyunu durdur
    /// </summary>
    /// <param name="videoClip">Oynatılacak video</param>
    /// <param name="callback">Video bitince çağrılacak fonksiyon</param>
    public void PlayCutscene(VideoClip videoClip, System.Action callback = null)
    {
        if (videoClip == null)
        {
            Debug.LogError("[CutsceneManager] VideoClip NULL! Video atanmamış!");
            callback?.Invoke();
            return;
        }

        onCutsceneEnd = callback;
        isPlaying = true;
        canSkip = false;

        Debug.Log($"[CutsceneManager] Playing cutscene: {videoClip.name}");

        // 1. Canvas Aç
        if (cutsceneCanvas != null) cutsceneCanvas.gameObject.SetActive(true);

        // 2. VideoPlayer Ayarla
        if (videoPlayer != null)
        {
            videoPlayer.clip = videoClip;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            
            // RenderTexture oluştur
            RenderTexture rt = new RenderTexture((int)videoClip.width, (int)videoClip.height, 0);
            videoPlayer.targetTexture = rt;
            if (videoScreen != null) videoScreen.texture = rt;

            // Video bitince callback
            videoPlayer.loopPointReached += OnVideoEnd;
            videoPlayer.Play();
        }

        // 3. Oyunu Durdur (Tüm karakterler durur)
        // NOT: VideoPlayer Time.timeScale=0'da da çalışır (realtimeSinceStartup kullanır)
        Time.timeScale = 0f;

        // 4. Cursor'ı göster
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 5. Skip yazısı (2 saniye sonra)
        StartCoroutine(EnableSkipAfterDelay());
    }

    private IEnumerator EnableSkipAfterDelay()
    {
        // Time.timeScale = 0 olduğu için WaitForSecondsRealtime kullan
        yield return new WaitForSecondsRealtime(skipDelay);
        canSkip = true;
        if (skipText != null) skipText.SetActive(true);
    }

    private void OnVideoEnd(VideoPlayer vp)
    {
        StopCutscene();
    }

    private void StopCutscene()
    {
        if (!isPlaying) return;
        isPlaying = false;
        canSkip = false;

        Debug.Log("[CutsceneManager] Cutscene ended!");

        // 1. Video Durdur
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoEnd;
            videoPlayer.Stop();

            // RenderTexture temizle
            if (videoPlayer.targetTexture != null)
            {
                videoPlayer.targetTexture.Release();
                videoPlayer.targetTexture = null;
            }
        }

        // 2. Canvas Kapat
        if (cutsceneCanvas != null) cutsceneCanvas.gameObject.SetActive(false);
        if (skipText != null) skipText.SetActive(false);

        // 3. Oyunu Devam Ettir
        Time.timeScale = 1f;

        // 4. Cursor'ı gizle (FPS oyun)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 5. Callback
        onCutsceneEnd?.Invoke();
        onCutsceneEnd = null;
    }
}
