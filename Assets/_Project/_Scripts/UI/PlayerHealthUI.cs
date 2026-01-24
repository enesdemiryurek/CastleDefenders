using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class PlayerHealthUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image healthFillImage;
    
    private Health targetHealth;

    private void Update()
    {
        // Eğer bağlı bir can sistemi yoksa sürekli ara (Player spawn olana kadar)
        if (targetHealth == null)
        {
            FindLocalPlayerHealth();
        }
    }

    private void FindLocalPlayerHealth()
    {
        // Mirror sisteminde Local Player oluştu mu?
        if (NetworkClient.localPlayer != null)
        {
            var health = NetworkClient.localPlayer.GetComponent<Health>();
            if (health != null)
            {
                targetHealth = health;
                
                // Event'e abone ol
                targetHealth.EventHealthChanged += UpdateHealthVisual;
                
                // İlk açılışta full göster
                UpdateHealthVisual(100, 100); 
                
                Debug.Log("Player Health UI connected to Local Player!");
            }
        }
    }

    private void UpdateHealthVisual(int current, int max)
    {
        Debug.Log($"[UI DEBUG] UpdateHealthVisual Called! Current: {current}, Max: {max}");
        if (healthFillImage != null)
        {
            float fillAmount = (float)current / max;
            healthFillImage.fillAmount = fillAmount;
            Debug.Log($"[UI DEBUG] Set Fill Amount to: {fillAmount}");
        }
        else
        {
             Debug.LogError("[UI DEBUG] Health Fill Image is NULL!");
        }
    }

    private void OnDestroy()
    {
        if (targetHealth != null)
        {
            targetHealth.EventHealthChanged -= UpdateHealthVisual;
        }
    }
}
