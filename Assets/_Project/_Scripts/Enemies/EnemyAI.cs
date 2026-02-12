using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aggroRange = 50000f; // Bannerlord Style: GERÇEKTEN tüm harita (30x arttırıldı)
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float updateInterval = 0.5f; // Saniyede 2 kere karar ver (performans için)

    [Header("Targets")]
    [SerializeField] private LayerMask targetLayers; // Player ve Unit layer'ları

    [Header("Ranged Settings")]
    [SerializeField] private bool isRanged = false;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;

    [Header("Visuals")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;
    [SerializeField] private string[] attackTriggers = { "Attack" }; // Birden fazla saldırı animasyonu için
    [SerializeField] private float terrainHeightCorrection = 0f; // Bake ile çözüldü, default 0
    [SerializeField] private Transform modelTransform; // Görsel için
    [SerializeField] private LayerMask groundLayer = -1; // Default: Everything (Tüm katmanları görsün)
    [SerializeField] private float alignmentSpeed = 10f;

    private NavMeshAgent agent;
    private Transform currentTarget;
    private float lastUpdateTime;
    private float lastAttackTime;
    private float lastGlobalSearchTime;
    private bool isDead = false;
    private bool isClimbing = false;

    // ... (Awake and other methods remain same) ...

    private void AttackTarget()
    {
        if (isDead) return;
        // Cooldown kontrolü
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;
        
        // 1. Hedefe Dön (LookAt)
        if (currentTarget != null)
        {
            Vector3 lookPos = currentTarget.position;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);
        }

        // 2. Durdur (Animasyon bitene kadar)
        if (agent != null && agent.enabled)
        {
             agent.isStopped = true;
             Invoke(nameof(ResumeMovement), 0.75f);
        }

        // 3. Animasyon Tetikle (Rastgele)
        if (attackTriggers.Length > 0)
        {
            string randomTrigger = attackTriggers[Random.Range(0, attackTriggers.Length)];
            
            if (networkAnimator != null)
            {
                networkAnimator.SetTrigger(randomTrigger);
            }
            else if (animator != null)
            {
                 animator.SetTrigger(randomTrigger);
            }
        }

        // 4. Saldırı (Melee vs Ranged)
        if (isRanged)
        {
            StartCoroutine(SpawnProjectileDelayed(0.4f)); 
        }
        else
        {
            // Hedefe hasar ver (Melee)
            IDamageable damageable = currentTarget.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, transform.position);
                OnAttack?.Invoke();
            }
        }
    }

    private System.Collections.IEnumerator SpawnProjectileDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (projectilePrefab != null && projectileSpawnPoint != null)
        {
            // Oku SpawnPoint'in açısıyla fırlat
            // AMA ÖNCE: SpawnPoint'in hedefe tam baktığından emin ol
            if (currentTarget != null)
            {
                // Hedef pozisyonunu al (Biraz yukarı nişan al ki ayaklarına sıkmasın)
                Vector3 targetPos = currentTarget.position + Vector3.up * 1.0f;
                
                // Spawn noktasını hedefe çevir
                projectileSpawnPoint.LookAt(targetPos);
            }

            GameObject proj = Instantiate(projectilePrefab, projectileSpawnPoint.position, projectileSpawnPoint.rotation);
            NetworkServer.Spawn(proj);

            // Eğer Fizikli Ok (Ballistic) ise fırlat
            BallisticProjectile ballistic = proj.GetComponent<BallisticProjectile>();
            if (ballistic != null)
            {
                // Hedef pozisyonunu ver (Launch içinde Rpc var)
                ballistic.SetShooter(gameObject); // DOST ATEŞİ FIX: Kendini veya arkadaşlarını vurmasın
                ballistic.Launch(currentTarget != null ? currentTarget.position : transform.forward * 10f);
            }
        }
    }

    // ...

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        // agent.baseOffset atamasını kaldırdık. NavMesh ayarları geçerli olacak.

        if (modelTransform == null)
        {
             if (animator != null) modelTransform = animator.transform;
             else {
                 Animator anim = GetComponentInChildren<Animator>();
                 if (anim != null && anim.transform != transform) modelTransform = anim.transform;
             }
        }

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();

        // Health eventine abone ol
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += OnDeathHandler;
        }
    }

    private void OnDeathHandler()
    {
        if (isDead) return;
        isDead = true;
        
        // 1. Hareket ve Fizik İptal
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false; 
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false; // Cesedin içinden geçilebilsin

        // 2. Animasyon
        if (networkAnimator != null) networkAnimator.SetTrigger("Die");
        else if (animator != null) animator.SetTrigger("Die");
        
        // 3. AI Temizliği
        StopAllCoroutines();
        CancelInvoke();
        this.enabled = false; // Update döngüsünü durdur

        // 4. Savaş Yönetiminden Sil
        if (BattleManager.Instance != null && NetworkServer.active) 
        {
             BattleManager.Instance.UnregisterEnemy(this);
        }

        // 5. Ceset Yönetimi (Sınır: 30)
        if (CorpseManager.Instance != null) 
        {
            CorpseManager.Instance.RegisterCorpse(gameObject);
        }
        else
        {
            // Fallback
            if(NetworkServer.active) NetworkServer.Destroy(gameObject);
            else Destroy(gameObject, 5f);
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        // NAVMESH FIX v2: Daha agresif zemin arama
        if (agent != null)
        {
            // 1. Önce 20 birim yarıçapta ara
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 20.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                agent.enabled = true;
            }
            // 2. Bulamazsan, aşağı doğru Ray atıp fiziksel zemini bul, oradan ara
            else if (Physics.Raycast(transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit physHit, 50f))
            {
                if (NavMesh.SamplePosition(physHit.point, out NavMeshHit hit2, 10.0f, NavMesh.AllAreas))
                {
                    agent.Warp(hit2.position);
                    agent.enabled = true;
                }
            }
            
            if(!agent.enabled)
            {
                Debug.LogError($"[{name}] AI NavMesh bulamadi! (Range: 20f + Raycast)");
            }
        } // <--- Outer block closing brace

        if(agent != null) 
        {
             agent.stoppingDistance = Mathf.Max(0.5f, attackRange - 0.5f);
             agent.autoTraverseOffMeshLink = false; // MERDİVEN İÇİN: Otomatik geçişi kapat
        }
        
        // Savaş Yönetimine Kaydol (RESTORED)
        if (BattleManager.Instance != null) 
        {
            BattleManager.Instance.RegisterEnemy(this);
        }
        else
        {
            Debug.LogError($"[EnemyAI] {name}: BattleManager BULUNAMADI! Düşmanlar sisteme kaydolamıyor.");
        }
    }

    private void Start()
    {
        // ... (Kodu koru)
        if(NetworkServer.active && agent != null && !agent.isOnNavMesh)
        {
             if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                agent.enabled = true;
            }
        }
    }

    private void OnValidate()
    {
        if (aggroRange < 50000f) aggroRange = 50000f;
    }

    private void OnDestroy()
    {
        Health health = GetComponent<Health>();
        if (health != null) health.OnDeath -= OnDeathHandler;
        
        if (BattleManager.Instance != null && NetworkServer.active) BattleManager.Instance.UnregisterEnemy(this);
    }

    private void Update()
    {
        if (isDead) return;
        
        // Animasyon Hızı (Basit)
        if (animator != null && agent.enabled) animator.SetFloat("Speed", agent.velocity.magnitude);

        // Server-Side Mantık
        if (!agent.enabled) return;

        // Tırmanma (OffMeshLink) Kontrolü
        if (agent.isOnOffMeshLink)
        {
            if (!isClimbing) StartCoroutine(TraverseLadder());
            return;
        }

        // Hedef Arama (1 saniyede bir - Yeterli)
        if (Time.time - lastGlobalSearchTime > 1.0f) 
        {
            lastGlobalSearchTime = Time.time;
            FindBestTarget();
        }

        // Hareket
        if (currentTarget != null)
        {
            MoveToTarget();
        }
    }
    
    // YENİ: AlignModelToGround KALDIRILDI (Titreme sebebiydi)

    private void FindBestTarget()
    {
        // TARGETING (Simple + Distributed)
        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRange, targetLayers);
        
        Transform bestTarget = null;
        float bestScore = float.MaxValue; 

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            var damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                Transform candidate = hit.transform;
                if (hit.GetComponentInParent<NetworkBehaviour>() != null)
                    candidate = hit.GetComponentInParent<NetworkBehaviour>().transform;

                float dist = Vector3.Distance(transform.position, candidate.position);
                
                // --- DAĞINIK SALDIRI MANTIĞI ---
                // Sadece en yakınına gitmesinler, biraz rastgele dağılsınlar.
                // Okçular (Ranged) çok daha rastgele hedefler seçsin (arka safları vursun).
                // Yakıncılar (Melee) biraz daha toplu kalsın ama yine de yığılmasın.
                
                float noiseRange = isRanged ? 20.0f : 5.0f; 
                float randomBias = Random.Range(0f, noiseRange); // Pozitif sayı ekliyoruz ki uzaktaki "daha uzak" görünsün DEĞİL.
                // Mantık: Score = Distance + Noise. 
                // Eğer gürültü negatif olursa uzaktaki yakına gelebilir. Biz 'distribution' istiyoruz.
                // Doğrusu: Random.Range(-noise, noise).
                
                float noise = Random.Range(-noiseRange, noiseRange);
                float score = dist + noise; 

                // Skor ne kadar düşükse o kadar "yakın" hissedilir.
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }
        }

        currentTarget = bestTarget;
    }



    private void MoveToTarget()
    {
        if (currentTarget == null) 
        {
            name = "Enemy_Idle_NoTarget";
            return;
        }

        if (!agent.isOnNavMesh) return;

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        
        // --- RANGED MANTIK (Bannerlord Tarzı) ---
        if (isRanged)
        {
            // Eğer saldırı menzilindeysek DUR ve SIK
            if (dist <= attackRange)
            {
                name = $"Enemy_RangedAttack_>{currentTarget.name}";
                agent.isStopped = true;
                
                // Hedefe Dön (LookAt) - Sadece Y ekseninde
                Vector3 lookPos = currentTarget.position;
                lookPos.y = transform.position.y;
                transform.LookAt(lookPos);
                
                AttackTarget();
            }
            else
            {
                // Menzile girene kadar koş
                agent.isStopped = false;
                agent.SetDestination(currentTarget.position);
            }
        }
        // --- MELEE MANTIK (Klasik) ---
        else
        {
            // Yaklaşma mantığı (NavMeshStopping Distance ayarı ile uyumlu olmalı)
            agent.SetDestination(currentTarget.position);

            if (dist <= attackRange)
            {
                name = $"Enemy_MeleeAttack_>{currentTarget.name}";
                // Menzildeyiz: Dur, Dön ve Saldır
                agent.isStopped = true;
                agent.updateRotation = false; // NavMesh dönmesin, biz döndüreceğiz
                
                // Hedefe Dön
                Vector3 lookPos = currentTarget.position;
                lookPos.y = transform.position.y;
                transform.LookAt(lookPos); // veya Smooth Rotate

                AttackTarget();
            }
            else
            {
                name = $"Enemy_Chasing_>{currentTarget.name}";
                // Menzil dışındayız: Koş
                agent.isStopped = false;
                agent.updateRotation = true;
            }
        }
    }

    public event System.Action OnAttack;



    private System.Collections.IEnumerator TraverseLadder()
    {
        isClimbing = true;
        agent.isStopped = true;

        if (animator != null) animator.SetBool("Climb", true); // Tırmanma Animasyonu
        if (networkAnimator != null) networkAnimator.SetTrigger("ClimbTrigger");

        OffMeshLinkData data = agent.currentOffMeshLinkData;
        Vector3 startPos = agent.transform.position;
        Vector3 endPos = data.endPos + Vector3.up * agent.baseOffset; // Yükseklik farkını koru

        float duration = 3.0f; // Tırmanma süresi (Animasyonla eşleşmeli)
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (isDead) yield break;
            
            agent.transform.position = Vector3.Lerp(startPos, endPos, elapsed / duration);
            
            // Yüzünü duvara dön (Start -> End Yönüne değil, duvarın normaline)
            // Basitçe bitişe baksa yeter
            Vector3 lookPos = endPos;
            lookPos.y = agent.transform.position.y;
            agent.transform.LookAt(lookPos);

            elapsed += Time.deltaTime;
            yield return null;
        }

        agent.transform.position = endPos;
        agent.CompleteOffMeshLink();
        
        if (animator != null) animator.SetBool("Climb", false);
        
        agent.isStopped = false;
        isClimbing = false;
    }

    private void ResumeMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    // Gizmos ile Aggro alanını çiz (Debug için)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isRanged ? Color.cyan : Color.red;
        Gizmos.DrawWireSphere(transform.position, aggroRange);
    }
}
