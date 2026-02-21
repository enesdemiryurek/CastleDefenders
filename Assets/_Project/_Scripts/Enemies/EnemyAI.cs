using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aggroRange = 300f; // Düşmanı algılama mesafesi (metre)
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float moveSpeed = 3.5f; // Koşma hızı (Inspector'dan ayarlanır)
    [SerializeField] private int damage = 10;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float updateInterval = 0.5f; // Saniyede 2 kere karar ver (performans için)

    [Header("Targets")]
    [SerializeField] private LayerMask targetLayers; // Player ve Unit layer'ları

    [Header("Zone Restriction")]
    [SerializeField] private bool useZoneRestriction = true; // Bölge sınırlaması aktif mi?
    [SerializeField] private float maxDistanceFromSpawn = 50f; // Spawn noktasından max uzaklık
    private Vector3 spawnPosition; // Doğduğu yer

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
        
        // 1. Hareket İptal
        if (agent != null)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        // 2. Animasyon (FORCE) - Die TRIGGER kullan!
        if (networkAnimator != null) networkAnimator.enabled = false;

        if (animator != null) 
        {
            // Tüm parametreleri sıfırla (koşma animasyonu dursun)
            animator.SetFloat("Speed", 0f);
            animator.SetBool("IsMoving", false);
            animator.SetTrigger("Die"); // TRIGGER kullan!
        }
        else if (networkAnimator != null && networkAnimator.animator != null) 
        {
            networkAnimator.animator.SetFloat("Speed", 0f);
            networkAnimator.animator.SetBool("IsMoving", false);
            networkAnimator.animator.SetTrigger("Die"); // TRIGGER kullan!
        }

        // Tüm clientlarda da ölüm animasyonu tetikle
        RpcPlayDeathAnimation();

        // 3. Yere Düşür (Havada kalmasın!)
        SnapCorpseToGround();
        
        // 4. AI Temizliği
        CancelInvoke();

        // 5. Savaş Yönetiminden Sil
        if (BattleManager.Instance != null && NetworkServer.active) 
        {
             BattleManager.Instance.UnregisterEnemy(this);
        }

        // 6. Animasyon bitince Animator'ı kapat + Collider'ları kapat
        StartCoroutine(DisableAnimatorAfterDeath());

        // 7. Ceset Yönetimi (Sınır: 30)
        if (CorpseManager.Instance != null) 
        {
            CorpseManager.Instance.RegisterCorpse(gameObject);
        }
        else
        {
            if(NetworkServer.active) NetworkServer.Destroy(gameObject);
            else Destroy(gameObject, 5f);
        }
    }

    private System.Collections.IEnumerator DisableAnimatorAfterDeath()
    {
        // Kısa bir süre bekle, sonra collider'ları kapat
        yield return new WaitForSeconds(0.5f);
        
        // Collider'ları kapat (cesedin içinden geçilebilsin)
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Ölüm animasyonu bittikten sonra Animator'ı kapat
        yield return new WaitForSeconds(2.0f);
        
        if (animator != null) animator.enabled = false;
        if (networkAnimator != null && networkAnimator.animator != null) 
        {
            networkAnimator.animator.enabled = false;
        }

        this.enabled = false;
    }

    [ClientRpc]
    private void RpcPlayDeathAnimation()
    {
        // Tüm clientlarda ölüm animasyonu tetikle
        Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.SetFloat("Speed", 0f);
            anim.SetBool("IsMoving", false);
            anim.SetTrigger("Die");
        }
    }

    private void SnapCorpseToGround()
    {
        // Cesedi yere yapıştır (Havada kalmasın!)
        // Yukarıdan 10m ray at - geniş arama
        if (Physics.Raycast(transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, groundLayer))
        {
            transform.position = hit.point;
        }
        else
        {
            // Fallback: Rigidbody ekle, yerçekimiyle düşsün
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 50f;
            rb.linearDamping = 2f;
            rb.freezeRotation = true;
            // 3 saniye sonra Rigidbody'yi kaldır (yere düştükten sonra)
            Destroy(rb, 3f);
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        // Spawn pozisyonunu kaydet (zone restriction için)
        spawnPosition = transform.position;
        
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
             // AggroRange güvenlik kontrolü
             if (aggroRange > 500f)
             {
                 Debug.LogWarning($"[{name}] AggroRange {aggroRange} çok yüksek! 300'e düşürülüyor.");
                 aggroRange = 300f;
             }
             
             agent.speed = moveSpeed; // Koşma hızını ayarla
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
        // AggroRange minimum 10, maksimum 500
        aggroRange = Mathf.Clamp(aggroRange, 10f, 500f);
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
        
        // KNOCKDOWN: Yerdeyken hiçbir şey yapma!
        Health myHealth = GetComponent<Health>();
        if (myHealth != null && myHealth.isKnockedDown)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            return;
        }
        
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

        // ZONE RESTRICTION: Spawn noktasından çok uzaklaştıysak geri dön
        if (useZoneRestriction && NetworkServer.active)
        {
            float distFromSpawn = Vector3.Distance(transform.position, spawnPosition);
            if (distFromSpawn > maxDistanceFromSpawn)
            {
                // Çok uzaklaştık! Geri dön
                currentTarget = null; // Hedefi bırak
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(spawnPosition);
                }
                return; // Hedef arama yapma
            }
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
        // MEVCUT HEDEFİ KONTROL ET: Hedef varsa ve canlıysa değiştirme!
        if (currentTarget != null)
        {
            Health existingHealth = currentTarget.GetComponent<Health>();
            if (existingHealth != null && existingHealth.IsAlive)
            {
                return; // Hedef hala canlı, değiştirme → zikzak yapma!
            }
        }

        // Hedef yok veya öldü → yeni hedef ara
        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRange, targetLayers);
        
        Transform bestTarget = null;
        float bestDist = float.MaxValue; 

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            var damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            // ÖLÜ KONTROLÜ
            Health targetHealth = hit.GetComponentInParent<Health>();
            if (targetHealth != null && !targetHealth.IsAlive) continue;

            Transform candidate = hit.transform;
            if (hit.GetComponentInParent<NetworkBehaviour>() != null)
                candidate = hit.GetComponentInParent<NetworkBehaviour>().transform;

            float dist = Vector3.Distance(transform.position, candidate.position);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = candidate;
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
        // --- MELEE MANTIK ---
        else
        {
            if (dist <= attackRange)
            {
                // Menzildeyiz: DUR ve SALDIRI
                agent.isStopped = true;
                agent.updateRotation = false;
                agent.ResetPath(); // İçine girmeyi önle!
                
                // Hedefe Dön
                Vector3 lookPos = currentTarget.position;
                lookPos.y = transform.position.y;
                transform.LookAt(lookPos);

                AttackTarget();
            }
            else
            {
                // Menzil dışındayız: Koş ama saldırı menzilinde dur
                agent.stoppingDistance = attackRange - 0.3f;
                agent.isStopped = false;
                agent.updateRotation = true;
                agent.SetDestination(currentTarget.position);
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
