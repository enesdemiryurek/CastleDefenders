using Mirror;
using UnityEngine;
using UnityEngine.AI;

public class Health : NetworkBehaviour, IDamageable
{
    [Header("Settings")]
    [SerializeField] private int maxHealth = 100;
    
    [SyncVar(hook = nameof(HandleHealthChanged))]
    private int currentHealth;

    public int CurrentHealth => currentHealth;
    public bool IsAlive => currentHealth > 0;

    [Header("Debug")]
    public bool destroyOnDeath = false; // User Request: CorpseManager yönetecek
    public float deathDelay = 20.0f; // Animasyon için zaman tanı (User Request: 20s)

    public event System.Action OnDeath;
    public event System.Action OnRevive; // İleride gerekebilir
    public event System.Action<int, int> EventHealthChanged; // Current, Max
    public event System.Action<Transform> OnDamaged; // Hasar aldığında saldıranın Transform'u

    public override void OnStartServer()
    {
        currentHealth = maxHealth;
        // User Request: Cesetleri CorpseManager yönetsin, Health destroy etmesin
        destroyOnDeath = false; 
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
        
        // Debug.Log($"{name} took {amount} damage. Current Health: {currentHealth}"); // Removed per user request

        // KAN EFEKTİ (Herkes görsün)
        RpcSpawnBlood(transform.position + Vector3.up * 1.5f);

        HandleHealthChanged(oldHealth, currentHealth);

        // Saldıranı bildir (Reactive Defense için)
        if (damageSource.HasValue)
        {
            // damageSource pozisyonundan saldıranı bul
            Collider[] nearAttackers = Physics.OverlapSphere(damageSource.Value, 3f);
            foreach (var col in nearAttackers)
            {
                if (col.transform != transform) // Kendimiz değil
                {
                    OnDamaged?.Invoke(col.transform);
                    break; // İlk bulunan saldıran yeterli
                }
            }
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    [ClientRpc]
    private void RpcSpawnBlood(Vector3 position)
    {
        try
        {
            if (bloodVfxPrefab != null)
            {
                GameObject fx = Instantiate(bloodVfxPrefab, position, Quaternion.identity);
                Destroy(fx, 2f);
            }
            // bloodVfxPrefab yoksa hiçbir şey yapma (Shader.Find build'de crash yapar!)
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RpcSpawnBlood error (safe): {e.Message}");
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
        
        // GÜVENLIK: Player objesini ASLA destroy etme!
        if (GetComponent<PlayerController>() != null)
        {
            Debug.LogWarning($"[Health] Player destroy engellendi: {name}");
            yield break;
        }
        
        NetworkServer.Destroy(gameObject);
    }

    // ==========================================
    // KNOCKDOWN SİSTEMİ (F1 Charge Attack)
    // ==========================================
    
    [SyncVar] public bool isKnockedDown = false;
    private Coroutine knockdownRoutine;

    [Server]
    public void ApplyKnockdown(float groundDuration = 1f)
    {
        // Ölüyse veya zaten yerdeyse tekrar knockdown yapma
        if (!IsAlive || isKnockedDown) return;
        
        if (knockdownRoutine != null) StopCoroutine(knockdownRoutine);
        knockdownRoutine = StartCoroutine(KnockdownRoutine(groundDuration));
    }

    [Server]
    private System.Collections.IEnumerator KnockdownRoutine(float groundDuration)
    {
        isKnockedDown = true;
        
        // NavMeshAgent durdur
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        
        // Düşme animasyonu tetikle
        RpcTriggerKnockdown();
        
        // 1 saniye yerde kal
        yield return new WaitForSeconds(groundDuration);
        
        // Kalkma animasyonu tetikle
        RpcTriggerGetUp();
        
        // Kalkma animasyonu süresince bekle (1 saniye)
        yield return new WaitForSeconds(1f);
        
        // Kalktı! Normal duruma dön
        isKnockedDown = false;
        
        if (agent != null && agent.isOnNavMesh && IsAlive)
        {
            agent.isStopped = false;
        }
    }

    [ClientRpc]
    private void RpcTriggerKnockdown()
    {
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.SetTrigger("KnockedDown");
        }
    }

    [ClientRpc]
    private void RpcTriggerGetUp()
    {
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.SetTrigger("GetUp");
        }
    }
}
