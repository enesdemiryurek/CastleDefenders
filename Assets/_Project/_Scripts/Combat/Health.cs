using Mirror;
using UnityEngine;

public class Health : NetworkBehaviour, IDamageable
{
    [Header("Settings")]
    [SerializeField] private int maxHealth = 100;
    
    [SyncVar(hook = nameof(HandleHealthChanged))]
    private int currentHealth;

    public int CurrentHealth => currentHealth;

    [Header("Debug")]
    public bool destroyOnDeath = true;
    public float deathDelay = 3.0f; // Animasyon için zaman tanı

    public event System.Action OnDeath;
    public event System.Action OnRevive; // İleride gerekebilir
    public event System.Action<int, int> EventHealthChanged; // Current, Max

    public override void OnStartServer()
    {
        currentHealth = maxHealth;
    }

    [Server]
    public void TakeDamage(int amount)
    {
        if (currentHealth <= 0) return;

        int oldHealth = currentHealth;
        currentHealth -= amount;
        
        Debug.Log($"{name} took {amount} damage. Current Health: {currentHealth}");

        // CRITICAL FIX: SyncVar hook'u Server/Host üzerinde otomatik çalışmaz.
        // Host oynayan kişinin UI'ının güncellenmesi için bunu elle çağırmalıyız.
        HandleHealthChanged(oldHealth, currentHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void HandleHealthChanged(int oldValue, int newValue)
    {
        // UI'a haber ver
        EventHealthChanged?.Invoke(newValue, maxHealth);
    }

    [Server]
    private void Die()
    {
        if (currentHealth > 0) return; // Zaten ölmüşse tekrar ölmesin (Network lag koruması)

        Debug.Log($"{name} Died!");
        
        // Event'i tetikle (Server tarafındaki diğer scriptler için)
        OnDeath?.Invoke();

        // Tüm clientlara öldüğünü bildir (Animasyon için)
        RpcDie();

        if (destroyOnDeath)
        {
            StartCoroutine(DestroyAfterDelay());
        }
    }

    [ClientRpc]
    private void RpcDie()
    {
        // Client tarafında event tetikle (Animation Controller burayı dinleyecek)
        OnDeath?.Invoke();
    }

    [Server]
    private System.Collections.IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(deathDelay);
        NetworkServer.Destroy(gameObject);
    }
}
