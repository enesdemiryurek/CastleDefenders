using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class LauncherUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider loadingBar;
    [SerializeField] private float fakeLoadTime = 3.0f;
    [SerializeField] private string nextSceneName = "MainMenu";

    private void Start()
    {
        // Yükleme işlemini başlat
        StartCoroutine(LoadingProcess());
    }

    private IEnumerator LoadingProcess()
    {
        float timer = 0f;
        loadingBar.value = 0f;

        while (timer < fakeLoadTime)
        {
            timer += Time.deltaTime;
            // 0 ile 1 arasında değer üret (Bar yüzdesi)
            float progress = Mathf.Clamp01(timer / fakeLoadTime);
            loadingBar.value = progress;
            yield return null;
        }

        loadingBar.value = 1f;
        
        // Küçük bir bekleme (Bar %100 görünsün)
        yield return new WaitForSeconds(0.5f);

        // Ana Menüye Geç
        SceneManager.LoadScene(nextSceneName);
    }
}
