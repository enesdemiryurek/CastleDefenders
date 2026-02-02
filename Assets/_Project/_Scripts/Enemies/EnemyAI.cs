using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aggroRange = 5000f; // Bannerlord Style: GERÇEKTEN tüm harita (Physics Optimized)
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
        isDead = true;
        
        if (agent != null)
        {
            if (agent.isOnNavMesh) 
            {
                agent.isStopped = true;
            }
            agent.enabled = false; 
        }

        if (networkAnimator != null) networkAnimator.SetTrigger("Die");
        else if (animator != null) animator.SetTrigger("Die");
        
        StopAllCoroutines();
        CancelInvoke();
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
        }
        
        if(agent != null) agent.stoppingDistance = Mathf.Max(0.5f, attackRange - 0.5f);
    }

    private void Start()
    {
        // Client/Host senkronizasyonu için backup
        if(NetworkServer.active && agent != null && !agent.isOnNavMesh)
        {
             if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                agent.enabled = true;
            }
        }
    }

    private void OnDestroy()
    {
        Health health = GetComponent<Health>();
        if (health != null) health.OnDeath -= OnDeathHandler;
    }

    private void Update()
    {
        if (isDead) return;
        if (!agent.enabled) return;

        // Performans Optimization: Her frame hedef arama
        if (Time.time - lastGlobalSearchTime > 0.5f) // 0.5 saniyede bir ara
        {
            lastGlobalSearchTime = Time.time;
            FindBestTarget();
        }

        // Eğer hedef varsa hareket et, yoksa bekle
        if (currentTarget != null)
        {
            MoveToTarget();
        }
    }

    private void FindBestTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRange, targetLayers);
        
        Transform bestTarget = null;
        float bestScore = float.MaxValue; // Puan ne kadar düşükse o kadar iyi (Mesafe temelli)

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            var damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                Transform candidate = hit.transform;
                if (hit.GetComponentInParent<NetworkBehaviour>() != null)
                    candidate = hit.GetComponentInParent<NetworkBehaviour>().transform;
                
                // --- SCORING SYSTEM (Advanced) ---
                float dist = Vector3.Distance(transform.position, candidate.position);
                
                // 1. "Kişilik" Faktörü: Biraz rastgelelik (Gürültü artırıldı)
                float randomNoise = Random.Range(-5f, 5f);
                
                // 2. Rol Önceliği: Player'a saldırmasın, askere saldırsın
                // (Player'a +2 metre ceza puanı - Önceden 10du, şimdi daha agresifler)
                float roleBias = 0f;
                if (candidate.GetComponent<PlayerController>() != null || candidate.GetComponentInParent<PlayerController>() != null) 
                    roleBias = 2f; 

                // 3. Kalabalık Cezası: Hedefin etrafı çok kalabalıksa başka hedefe git
                float crowdPenalty = 0f;
                // Performans için sadece çok yakındakilere bak (2 metre)
                // Physics.OverlapSphere pahalı olabilir, bu yüzden basit bir kontrol:
                // Şimdilik raycast veya count yerine basit bırakalım, user snippet'i OverlapSphere kullanıyordu ama nested olması tehlikeli.
                // Yine de user isteği üzerine ekliyorum ama optimize edip (LayerMask ile):
                Collider[] admirers = Physics.OverlapSphere(candidate.position, 2.0f, 1 << gameObject.layer); // Sadece düşmanlara bak
                if (admirers.Length > 2) crowdPenalty = (admirers.Length - 1) * 2.0f;

                // 4. Sadakat (Sticky): Eğer bu zaten benim hedefimse puanını düşür (Öncelik ver)
                float stickinessBonus = (candidate == currentTarget) ? -2.0f : 0f;

                float score = dist + randomNoise + roleBias + crowdPenalty + stickinessBonus;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }
        }

        currentTarget = bestTarget;
        // DIAGNOSTIC LOG (3-4 saniyede bir)
        if (Time.frameCount % 200 == 0) // Tek bir düşman için değil hepsi için, ama spam olmasın diye seyrek
        {
            string status = currentTarget != null ? $"Dist: {bestScore:F1}" : "NO TARGET"; // Changed closestDist to bestScore
            string navStatus = agent.enabled ? (agent.hasPath ? "Moving" : "Idle") : "DISABLED";
            // Sadece ilk 1-2 düşman log atsın (Adı 'Tier...' veya 'Clone' ile bitenler)
            if(Random.value < 0.1f) 
                Debug.Log($"[AI STATUS] {name} | Targets in Range: {hits.Length} | Target: {currentTarget?.name ?? "None"} | Nav: {navStatus} | LayerMask: {targetLayers.value}");
        }
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
