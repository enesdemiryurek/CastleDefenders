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
    public float deathDelay = 20.0f; // Animasyon için zaman tanı (User Request: 20s)

    public event System.Action OnDeath;
    public event System.Action OnRevive; // İleride gerekebilir
    public event System.Action<int, int> EventHealthChanged; // Current, Max

    public override void OnStartServer()
    {
        currentHealth = maxHealth;
    }

    [Header("Effects")]
    [SerializeField] private GameObject bloodVfxPrefab;

    [Server]
    public void TakeDamage(int amount, Vector3? damageSource = null)
    {
        if (currentHealth <= 0) return;

        // Kalkan Sistemi Kontrolü
        ShieldSystem shield = GetComponent<ShieldSystem>();
        if (shield != null && damageSource.HasValue)
        {
            amount = shield.TryBlock(amount, damageSource.Value);
        }

        if (amount <= 0) return; // Tam bloklandıysa can gitmesin

        int oldHealth = currentHealth;
        currentHealth -= amount;
        
        Debug.Log($"{name} took {amount} damage. Current Health: {currentHealth}");

        // KAN EFEKTİ (Herkes görsün)
        RpcSpawnBlood(transform.position + Vector3.up * 1.5f);

        HandleHealthChanged(oldHealth, currentHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    [ClientRpc]
    private void RpcSpawnBlood(Vector3 position)
    {
        if (bloodVfxPrefab != null)
        {
            GameObject fx = Instantiate(bloodVfxPrefab, position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        else
        {
            // FALLBACK: Eğer prefab atanmamışsa basit kırmızı efekt oluştur (Programmer Art)
            CreateFallbackBlood(position);
        }
    }

    private void CreateFallbackBlood(Vector3 pos)
    {
        GameObject blood = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blood.transform.position = pos;
        blood.transform.localScale = Vector3.one * 0.3f;
        
        var rend = blood.GetComponent<Renderer>();
        if (rend != null) 
        {
            rend.material = new Material(Shader.Find("Standard")); // Basit material
            rend.material.color = Color.red;
        }

        Destroy(blood.GetComponent<Collider>()); // Fizik etkileşimi olmasın
        Destroy(blood, 0.5f); // Yarım saniye sonra sil
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
