using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HitMarkerUI : MonoBehaviour
{
    public static HitMarkerUI Instance { get; private set; }

    [Header("UI Elements")]
    [Tooltip("Crosshair (Hit Marker) görselinin konulacağı UI Image bileşeni")]
    [SerializeField] private Image hitMarkerImage;

    [Header("Settings")]
    [Tooltip("Sadece isabet ettiğindeki renk")]
    [SerializeField] private Color hitColor = Color.gray;
    
    [Tooltip("Öldürücü isabet olduğundaki renk (Kırmızı)")]
    [SerializeField] private Color killColor = Color.red;
    
    [Tooltip("Ekranda kalıp kaybolma süresi")]
    [SerializeField] private float fadeDuration = 0.5f;

    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (hitMarkerImage == null)
        {
            hitMarkerImage = GetComponent<Image>();
        }

        if (hitMarkerImage != null)
        {
            // Başlangıçta görünmez yap (Alpha = 0)
            Color startColor = hitMarkerImage.color;
            startColor.a = 0f;
            hitMarkerImage.color = startColor;
        }
    }

    public void ShowMarker(bool isKill)
    {
        if (hitMarkerImage == null) return;

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }
        fadeRoutine = StartCoroutine(FadeEffect(isKill));
    }

    private IEnumerator FadeEffect(bool isKill)
    {
        // Rengi ayarla, Alpha 1 (tam görünür) yap
        Color targetColor = isKill ? killColor : hitColor;
        targetColor.a = 1f;
        hitMarkerImage.color = targetColor;

        // İsteğe bağlı eklenebilir: çapraz açıyı hafif çevirmek (Call of Duty gibi)
        // hitMarkerImage.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(-10f, 10f));

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            // Lineer olarak şeffaflaştır
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            targetColor.a = alpha;
            hitMarkerImage.color = targetColor;
            yield return null;
        }

        // Tamamen görünmez yap
        targetColor.a = 0f;
        hitMarkerImage.color = targetColor;
    }
}
