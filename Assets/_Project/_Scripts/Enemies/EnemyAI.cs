using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aggroRange = 10f;
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

    private void OnDestroy()
    {
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath -= OnDeathHandler;
        }
    }

    private void OnDeathHandler()
    {
        isDead = true;
        
        if (agent != null)
        {
            // Hata vermemesi için önce NavMesh üzerinde mi diye bak
            if (agent.isOnNavMesh) 
            {
                agent.isStopped = true;
            }
            agent.enabled = false; // NavMesh'ten tamamen kopar (Havada kalmasın)
        }

        // Animasyon Tetikle
        if (networkAnimator != null) networkAnimator.SetTrigger("Die");
        else if (animator != null) animator.SetTrigger("Die");
        
        // Saldırı eventini durdur
        StopAllCoroutines();
        CancelInvoke();
    }

    public override void OnStartServer()
    {
        agent.enabled = true;
        
        // Saldırı menzilinin biraz içine girince dursun (iç içe geçmesinler)
        agent.stoppingDistance = Mathf.Max(0.5f, attackRange - 0.5f);
    }

    private void Update()
    {
        AlignModelToGround();

        if (isDead) return;

        // Sadece Server karar verir
        if (!NetworkServer.active) return;
        
        // Animasyonları güncelle (Hız)
        if (animator != null && agent != null)
        {
            animator.SetFloat("Speed", agent.velocity.magnitude);
        }

        // Çok sık karar verme (Performans)
        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        FindBestTarget();
        MoveToTarget();
    }

    private void AlignModelToGround()
    {
        if (modelTransform == null) return;

        Ray ray = new Ray(transform.position + Vector3.up, Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(ray, 3f, groundLayer);

        RaycastHit bestHit = new RaycastHit();
        bool found = false;
        float minDst = float.MaxValue;

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            if (hit.collider.isTrigger) continue;

            if (hit.distance < minDst)
            {
                minDst = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        if (found)
        {
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, bestHit.normal) * transform.rotation;
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRotation, Time.deltaTime * alignmentSpeed);
        }
        else
        {
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, transform.rotation, Time.deltaTime * alignmentSpeed);
        }
    }

    private void FindBestTarget()
    {
        // Aday Hedefleri Belirle
        System.Collections.Generic.List<Transform> candidates = new System.Collections.Generic.List<Transform>();
        
        // 1. Yerel Arama (Aggro Range)
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, aggroRange, targetLayers);
        foreach (var hit in hitColliders)
        {
            if (hit.transform == transform) continue;
            if (hit.GetComponentInParent<PlayerController>() != null || hit.GetComponentInParent<UnitMovement>() != null)
            {
                candidates.Add(hit.transform);
            }
        }

        // 2. Global Arama (Eğer kimse yoksa)
        if (candidates.Count == 0)
        {
            foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) candidates.Add(p.transform);
            foreach (var u in FindObjectsByType<UnitMovement>(FindObjectsSortMode.None)) candidates.Add(u.transform);
        }

        if (candidates.Count == 0) return;

        // 3. Organik Hedef Seçimi (Mesafe + Rastgelelik)
        Transform bestCandidate = null;
        float bestScore = float.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;
            
            float dist = Vector3.Distance(transform.position, candidate.position);
            
            // "Kişilik" Faktörü: Her düşman, hedeflere biraz farklı gözle bakar.
            // Mesafeye -5 ile +5 arasında rastgele bir "Gürültü" ekliyoruz.
            // Böylece hepsi matematiksel olarak en yakındaki TEK kişiye saldırmaz, dağılırlar.
            // "Kişilik" Faktörü: Biraz çeşitlilik olsun ama saçmalamasınlar
            float randomNoise = Random.Range(-1f, 1f);
            
            // Player için YÜKSEK bir "Geri Planda Kalma" payı
            // Normalde önce askerlere (Frontline) saldırmalılar.
            // Askerler varken Player'a saldırması için Player'ın çooook yakında olması lazım.
            float roleBias = 0f;
            if (candidate.GetComponentInParent<PlayerController>() != null) roleBias = 10f; // +10 metre ceza

            float score = dist + randomNoise + roleBias;

            // Mevcut hedefe sadakat (Sticky)
            // Eğer bu aday zaten benim hedefimse, puanını biraz düşür (daha cazip kıl) ki zırt pırt hedef değiştirmesin.
            if (candidate == currentTarget)
            {
                score -= 3f; 
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        currentTarget = bestCandidate;
    }

    private void MoveToTarget()
    {
        if (currentTarget == null) return;

        if (!agent.isOnNavMesh) return;

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        
        // --- RANGED MANTIK (Bannerlord Tarzı) ---
        if (isRanged)
        {
            // Eğer saldırı menzilindeysek DUR ve SIK
            if (dist <= attackRange)
            {
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
