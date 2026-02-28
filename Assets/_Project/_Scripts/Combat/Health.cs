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

        // Player Kalkan Kontrolü (Tam absorb, HP bazlı)
        PlayerShield playerShield = GetComponent<PlayerShield>();
        if (playerShield != null && damageSource.HasValue)
        {
            amount = playerShield.TryBlock(amount, damageSource.Value);
        }

        // Ünite Kalkan Kontrolü (Şans bazlı, yüzde azaltma)
        ShieldSystem shield = GetComponent<ShieldSystem>();
        if (shield != null && damageSource.HasValue)
        {
            amount = shield.TryBlock(amount, damageSource.Value);
        }

        if (amount <= 0) return; // Tam bloklandıysa can gitmesin

        int oldHealth = currentHealth;
        currentHealth -= amount;
        
        bool isFatal = currentHealth <= 0;

        // KAN EFEKTİ (Herkes görsün)
        RpcSpawnBlood(transform.position + Vector3.up * 1.5f, isFatal);

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
    private void RpcSpawnBlood(Vector3 position, bool isFatal)
    {
        try
        {
            if (bloodVfxPrefab != null)
            {
                // KAN EFEKTİ DÜZELTMESİ: (Kullanıcı İstekleri: Tek noktadan çıksın, kısa sürsün, havada asılı kalmasın)
                // Yön hep rastgele (Y ekseninde)
                Quaternion randomRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                
                // Havada kalmasını önlemek için Parent'a bağlamıyoruz, zaten kısacık sürüp yok olacak.
                // Hep hedefin göğüs/bel hizasında (Yerden 1 metre yukarıda) tek bir noktada spawnla.
                Vector3 spawnPos = new Vector3(transform.position.x, transform.position.y + 1.0f, transform.position.z);

                GameObject fx = Instantiate(bloodVfxPrefab, spawnPos, randomRotation);
                
                // Kan spreyinin havada asılı kalıp kötü görünmemesi için çok kısa sürede (0.5 saniye) sil!
                Destroy(fx, 0.5f);
            }
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
        Debug.Log($"[Health] {name} ★ ApplyKnockdown çağrıldı! IsAlive={IsAlive}, isKnockedDown={isKnockedDown}, EnemyAI={GetComponent<EnemyAI>() != null}");

        // Ölüyse veya zaten yerdeyse tekrar knockdown yapma
        if (!IsAlive || isKnockedDown) 
        {
            Debug.Log($"[Health] {name} Knockdown İPTAL! IsAlive={IsAlive}, isKnockedDown={isKnockedDown}");
            return;
        }
        
        // SADECE DÜŞMANLARI DÜŞÜR (Oyuncu veya dost askerler düşmez)
        if (GetComponent<EnemyAI>() == null) 
        {
            Debug.Log($"[Health] {name} Knockdown İPTAL! EnemyAI bulunamadı (dost asker mi?)");
            return;
        }
        
        Debug.Log($"[Health] {name} ★★★ KNOCKDOWN BAŞLIYOR! Süre: {groundDuration}s");
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
        
        // Düşme animasyonu tetikle (Tüm clientlarda)
        RpcTriggerKnockdown();
        
        // Belirtilen saniye kadar yerde kal
        yield return new WaitForSeconds(groundDuration);
        
        // Kalkma animasyonu tetikle
        RpcTriggerGetUp();
        
        // Kalkma animasyonu süresince bekle (1.5 saniye)
        yield return new WaitForSeconds(1.5f);
        
        // Kalktı! Normal duruma dön
        isKnockedDown = false;
        
        if (agent != null && agent.isOnNavMesh && IsAlive)
        {
            agent.isStopped = false;
        }
    }

    // ANIMATOR TRIGGER SORUNU ÇÖZÜMÜ: NetworkAnimator bazen özel düşman prefablarında sync sorunu yaratabiliyor.
    // ClientRpc ile tüm istemcilere direkt "Animator" buldurup shotgunlama taktiğiyle her potansiyel trigger'ı vurduruyoruz.
    [ClientRpc]
    private void RpcTriggerKnockdown()
    {
        Animator anim = GetComponentInChildren<Animator>();
        Debug.Log($"[Health] {name} RpcTriggerKnockdown CLIENT'A ULAŞTI! Animator={anim != null}");
        if (anim != null)
        {
            // Önce mevcut saldırı/vurulma animasyonlarını durdur
            SafeResetTrigger(anim, "Hit");
            SafeResetTrigger(anim, "Attack");
            
            // Bilinen tüm knockdown trigger varyasyonlarını dene
            bool triggered = false;
            triggered |= SafeSetTrigger(anim, "KnockedDown");
            triggered |= SafeSetTrigger(anim, "Knockdown");
            triggered |= SafeSetTrigger(anim, "KnockedD");
            
            Debug.Log($"[Health] {name} Knockdown trigger gönderildi: {triggered}");
        }
    }

    [ClientRpc]
    private void RpcTriggerGetUp()
    {
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            SafeSetTrigger(anim, "GetUp");
        }
    }

    // Animator'da bu parametre var mı diye güvenli kontrol
    private bool HasParameter(Animator anim, string paramName)
    {
        foreach (var param in anim.parameters)
        {
            if (param.name == paramName) return true;
        }
        return false;
    }

    private bool SafeSetTrigger(Animator anim, string triggerName)
    {
        if (HasParameter(anim, triggerName))
        {
            anim.SetTrigger(triggerName);
            return true;
        }
        return false;
    }

    private void SafeResetTrigger(Animator anim, string triggerName)
    {
        if (HasParameter(anim, triggerName))
        {
            anim.ResetTrigger(triggerName);
        }
    }
}
