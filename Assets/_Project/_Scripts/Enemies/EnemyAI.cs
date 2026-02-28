using Mirror;
using UnityEngine;
using UnityEngine.AI;

public enum SiegeRole
{
    None,           // Normal AI — en yakın düşmana koş
    GateAttacker,   // Kapıya saldır, kırılınca normal savaş
    WallClimber     // En yakın merdiveni bul, tırman, surda savaş
}

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

    [Header("Siege Settings")]
    [SerializeField] public SiegeRole siegeRole = SiegeRole.None;
    private Transform siegeTarget; // Kapı veya merdiven başlangıç noktası
    private bool siegeTargetReached = false; // Merdivene/kapıya ulaştı mı?
    private bool gateDestroyed = false; // Kapı kırıldı mı?

    [Header("Targets")]
    [SerializeField] private LayerMask targetLayers; // Player ve Unit layer'ları

    [Header("Zone Restriction")]
    [SerializeField] private bool useZoneRestriction = false; // Bölge sınırlaması KAPATILDI (kaçma/titreme bug'ına sebep oluyordu)
    [SerializeField] private float maxDistanceFromSpawn = 150f; // Sınırı da çok genişlettik, güvenli olsun diye
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
    private int lastKnownHealth = -1;

    // Hedef Dağılımı (Swarm Limit)
    private static System.Collections.Generic.Dictionary<Transform, int> activeEngagements = new System.Collections.Generic.Dictionary<Transform, int>();
    private Transform myEngagedTarget = null;

    private void RegisterEngagement(Transform target)
    {
        if (target == null) return;
        if (myEngagedTarget == target) return;
        
        UnregisterEngagement(myEngagedTarget);
        
        if (!activeEngagements.ContainsKey(target)) activeEngagements[target] = 0;
        activeEngagements[target]++;
        myEngagedTarget = target;
    }

    private void UnregisterEngagement(Transform target)
    {
        if (target == null) return;
        if (activeEngagements.ContainsKey(target))
        {
            activeEngagements[target]--;
            if (activeEngagements[target] <= 0) activeEngagements.Remove(target);
        }
        if (myEngagedTarget == target) myEngagedTarget = null;
    }

    private bool IsTargetOverwhelmed(Transform target)
    {
        // KULLANICI İSTEĞİ: Hedefteki kişi sınırı tamamen KALDIRILDI.
        // Düşmanlar da istedikleri gibi kalabalık halde saldırabilir.
        return false;
    }

    // ... (Awake and other methods remain same) ...

    private void AttackTarget()
    {
        if (isDead) return;
        Health myHealth = GetComponent<Health>();
        if (myHealth != null && myHealth.isKnockedDown) return; // Yerdeyken saldıramaz

        // Cooldown kontrolü
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;
        
        // 1. Hedefe Dön (Y eksenini kilitleyerek)
        if (currentTarget != null)
        {
            Vector3 lookPos = currentTarget.position;
            lookPos.y = transform.position.y;
            Vector3 dir = (lookPos - transform.position).normalized;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        // 2. Animasyonu Tetikle
        OnAttack?.Invoke();
        if (attackTriggers != null && attackTriggers.Length > 0)
        {
            string randomTrigger = attackTriggers[Random.Range(0, attackTriggers.Length)];
            RpcPlayAttackAnimation(randomTrigger);
        }

        // 3. Durdur (Animasyon bitene kadar kaymasın)
        if (agent != null && agent.enabled)
        {
             agent.isStopped = true;
             Invoke(nameof(ResumeMovement), attackCooldown * 0.8f); // Cooldown bitmeden az önce tekrar yürümeye başlasın
        }

        // 4. Saldırı (Gecikmeli)
        if (isRanged)
        {
            // Okçu saldırısı
            StartCoroutine(SpawnProjectileDelayed(0.4f)); 
        }
        else
        {
            // Kılıç/Yakın dövüş saldırısı (Animasyonun kılıcı vurduğu ana denk getir)
            StartCoroutine(DealMeleeDamageDelayed(0.5f));
        }
    }

    private System.Collections.IEnumerator DealMeleeDamageDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (isDead || currentTarget == null) yield break;
        
        Health myHealth = GetComponent<Health>();
        if (myHealth != null && myHealth.isKnockedDown) yield break; // Havadayken/Yerdeyken vuruşu iptal et

        // Hedef hala menzilde mi ve yaşıyor mu kontrol et
        float dist = Vector3.Distance(transform.position, currentTarget.position);
        float effectiveAttackRange = Mathf.Max(attackRange, attackRange * 1.5f); // Hasar vururken biraz daha toleranslı ol

        if (dist <= effectiveAttackRange)
        {
            IDamageable damageable = currentTarget.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, transform.position);
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
            health.EventHealthChanged += OnHealthChanged;
        }
    }

    private void OnHealthChanged(int current, int max)
    {
        // İlk açılışta health set edilirken Hit oynamasın
        if (lastKnownHealth == -1)
        {
            lastKnownHealth = current;
            return;
        }

        // Can azaldıysa = HASAR → Hit animasyonu
        if (current < lastKnownHealth && current > 0 && !isDead)
        {
            // Yerde yatanı titrEtme
            Health hp = GetComponent<Health>();
            if (hp != null && hp.isKnockedDown)
            {
                lastKnownHealth = current;
                return;
            }

            // SyncVar hook tüm makinelerde çalışır, direkt Animator'a tetikle
            Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetTrigger("Hit");
            }
        }
        lastKnownHealth = current;
    }


    private void OnDeathHandler()
    {
        if (isDead) return;
        isDead = true;
        UnregisterEngagement(myEngagedTarget);
        
        // 1. Hareketi DURDUR ama KAPATMA! (agent.enabled = false yapınca yerçekimi kayboluyor ve havada kalıyor!)
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
            // agent.enabled = false; → YAPMA! Animasyon bitene kadar ajanı açık tut, yoksa havada kalır!
        }

        // 2. ÖNCELİKLE tüm clientlara ölüm animasyonunu yolla (NetworkAnimator HALA AÇIK olmalı!)
        // DİKKAT: Eski kodda networkAnimator.enabled = false BURADA yapılıyordu ve
        // RpcPlayDeathAnimation() hiçbir zaman clientlara ulaşamıyordu! Bu yüzden askerler ayakta ölüyordu.
        RpcPlayDeathAnimation();

        // 3. Server tarafında da lokal animasyonu tetikle
        Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();
        if (anim != null) 
        {
            anim.SetFloat("Speed", 0f);
            anim.SetBool("IsMoving", false);
            anim.ResetTrigger("Attack");
            anim.SetTrigger("Die");
        }

        // 4. Collider İptal (Başka askerler bu cesedin üzerinden geçebilsin)
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 5. AI Temizliği
        CancelInvoke();

        // 6. Savaş Yönetiminden Sil
        if (BattleManager.Instance != null && NetworkServer.active) 
        {
             BattleManager.Instance.UnregisterEnemy(this);
        }

        // 7. Animasyon bitince Animator'ı kapat VE cesedi yere mühürle
        StartCoroutine(DisableAnimatorAfterDeath());

        // 8. Ceset Yönetimi (Sınır: 30)
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
        // 1. Die sinyalinin Animator'a işlenip state değişmesi için yeterli bir süre bekle
        yield return new WaitForSeconds(0.5f);

        Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();
        float waitTime = 2.0f; // Animasyon okunamaza Fallback süre
        
        if (anim != null)
        {
            // 2. Çalmakta olan (Death) animasyonunun toplam saniyesini dinamik olarak oku
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.length > 0.5f)
            {
                waitTime = (stateInfo.length * 0.95f) - 0.5f;
            }
        }

        waitTime = Mathf.Clamp(waitTime, 0.1f, 5.0f);
        yield return new WaitForSeconds(waitTime);
        
        // 3. Artık animasyon bitti, cesedi yere mühürle (ÖNCEKİ KODDA BU ADIM ÇOK ERKEN ÇALIŞIYORDU!)
        SnapCorpseToGround();

        // 4. NavMeshAgent'ı artık güvenle kapatabiliriz (animasyon bitti, ceset yerde)
        if (agent != null) agent.enabled = false;
        
        // 5. Animasyonu kapat ve olduğu pozda (yerde yatarak) kalıcı olarak dondur
        if (anim != null) anim.enabled = false;
        if (networkAnimator != null) networkAnimator.enabled = false;

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

    [ClientRpc]
    private void RpcPlayAttackAnimation(string triggerName)
    {
        // Network Authority'si olmayan nesnelerde animasyon oynatmak için zorunlu RPC
        Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.SetTrigger(triggerName);
        }
    }

    private void SnapCorpseToGround()
    {
        bool foundGround = false;
        float highestGroundY = -9999f;

        // 1. RaycastAll ile tüm objeleri delip geç, SADECE cansız zemini bul
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 5f, Vector3.down, 20f);
        foreach (var hit in hits)
        {
            // Cesedin kendine çarpmasını engelle
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;
            
            // Başka karakterlere (Player, Enemy, Ally) çarpmasını engelle
            if (hit.collider.GetComponentInParent<Health>() != null) continue;
            
            // Eğer cansız bir nesneyse (zemin, taş, köprü vs.)
            if (hit.point.y > highestGroundY)
            {
                highestGroundY = hit.point.y;
                foundGround = true;
            }
        }

        if (foundGround)
        {
            Vector3 targetPos = transform.position;
            targetPos.y = highestGroundY;
            transform.position = targetPos;
        }
        else
        {
            // 2. Yedeğin yedeği: NavMesh
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit navHit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            {
                transform.position = navHit.position;
            }
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
             agent.angularSpeed = 1000f; // ARABA GİBİ DÖNME! Anında yüzünü çevir (default 120 çok yavaştı)
             agent.acceleration = 20f; // Hızlı ivmelenme (default 8 yavaştı, koşmaya direkt başlasın)
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
        if (health != null) 
        {
            health.OnDeath -= OnDeathHandler;
            health.EventHealthChanged -= OnHealthChanged;
        }
        
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

        // ======= KUŞATMA ROLÜ MANTIĞI =======
        if (siegeRole != SiegeRole.None && !siegeTargetReached)
        {
            HandleSiegeRole();
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

    // ==================== KUŞATMA ROLÜ ====================
    private void HandleSiegeRole()
    {
        if (!agent.isOnNavMesh) return;

        switch (siegeRole)
        {
            case SiegeRole.GateAttacker:
                HandleGateAttacker();
                break;
            case SiegeRole.WallClimber:
                HandleWallClimber();
                break;
        }
    }

    private void HandleGateAttacker()
    {
        // 1. Kapı hedefi bul (bir kere)
        if (siegeTarget == null && !gateDestroyed)
        {
            GateSystem[] gates = FindObjectsOfType<GateSystem>();
            float bestDist = float.MaxValue;
            GateSystem bestGate = null;

            foreach (var gate in gates)
            {
                if (gate.IsDead) continue; // Kırık kapıyı hedefleme
                float d = Vector3.Distance(transform.position, gate.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestGate = gate;
                }
            }

            // GateController da olabilir
            if (bestGate == null)
            {
                GateController[] gateControllers = FindObjectsOfType<GateController>();
                foreach (var gc in gateControllers)
                {
                    if (gc.CurrentHealth <= 0) continue;
                    float d = Vector3.Distance(transform.position, gc.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        siegeTarget = gc.transform;
                    }
                }
            }
            else
            {
                siegeTarget = bestGate.transform;
            }

            if (siegeTarget != null)
            {
                Debug.Log($"[EnemyAI] {name} ★ GATE ATTACKER → Kapıya gidiyor: {siegeTarget.name}");
            }
            else
            {
                // Kapı yok veya hepsi kırık → normal savaşa geç
                Debug.Log($"[EnemyAI] {name} Kapı bulunamadı, normal savaşa geçiyor!");
                siegeTargetReached = true;
                return;
            }
        }

        // 2. Kapı kırıldı mı kontrol et
        if (siegeTarget != null)
        {
            IDamageable gateDmg = siegeTarget.GetComponent<IDamageable>();
            GateSystem gs = siegeTarget.GetComponent<GateSystem>();
            GateController gc2 = siegeTarget.GetComponent<GateController>();

            bool dead = false;
            if (gs != null) dead = gs.IsDead;
            else if (gc2 != null) dead = gc2.CurrentHealth <= 0;

            if (dead)
            {
                Debug.Log($"[EnemyAI] {name} ★ KAPI KIRILDI! Normal savaşa geçiyor.");
                gateDestroyed = true;
                siegeTargetReached = true;
                siegeTarget = null;
                currentTarget = null;
                return;
            }
        }

        // 3. Kapıya yürü ve saldır
        if (siegeTarget != null)
        {
            float dist = Vector3.Distance(transform.position, siegeTarget.position);
            float effectiveRange = Mathf.Max(attackRange, attackRange * 1.15f);

            if (dist <= effectiveRange + 1.5f) // Kapı daha geniş, biraz tolerans
            {
                // Kapıya saldır!
                agent.isStopped = true;
                
                // Kapıya dön
                Vector3 lookPos = siegeTarget.position;
                lookPos.y = transform.position.y;
                Vector3 dir = (lookPos - transform.position).normalized;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(dir);

                // Saldırı (cooldown)
                if (Time.time - lastAttackTime >= attackCooldown)
                {
                    lastAttackTime = Time.time;
                    
                    // Animasyon
                    if (attackTriggers != null && attackTriggers.Length > 0)
                    {
                        string randomTrigger = attackTriggers[Random.Range(0, attackTriggers.Length)];
                        RpcPlayAttackAnimation(randomTrigger);
                    }
                    
                    // Gecikmeli hasar
                    currentTarget = siegeTarget; // AttackTarget'ın hedefini kapıya çevir
                    StartCoroutine(DealMeleeDamageDelayed(0.5f));
                }
            }
            else
            {
                // Kapıya koş
                agent.isStopped = false;
                agent.updateRotation = true;
                agent.stoppingDistance = Mathf.Max(0.5f, attackRange);
                agent.SetDestination(siegeTarget.position);
            }
        }
    }

    private void HandleWallClimber()
    {
        // 1. En yakın merdiveni bul (bir kere)
        if (siegeTarget == null)
        {
            SiegeLadder[] ladders = FindObjectsOfType<SiegeLadder>();
            float bestDist = float.MaxValue;
            SiegeLadder bestLadder = null;

            foreach (var ladder in ladders)
            {
                // Merdivenin alt noktası (zemin tarafı)
                Transform start = ladder.transform; // SiegeLadder'ın kendisi veya startPoint
                // startPoint private, ama OffMeshLink.startTransform kullanabiliriz
                OffMeshLink link = ladder.GetComponent<OffMeshLink>();
                if (link == null || link.startTransform == null) continue;

                float d = Vector3.Distance(transform.position, link.startTransform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestLadder = ladder;
                }
            }

            if (bestLadder != null)
            {
                OffMeshLink ladderLink = bestLadder.GetComponent<OffMeshLink>();
                siegeTarget = ladderLink.startTransform;
                Debug.Log($"[EnemyAI] {name} ★ WALL CLIMBER → Merdivene gidiyor: {bestLadder.name}");
            }
            else
            {
                // Merdiven yok → normal savaşa geç
                Debug.Log($"[EnemyAI] {name} Merdiven bulunamadı, normal savaşa geçiyor!");
                siegeTargetReached = true;
                return;
            }
        }

        // 2. Merdivene ulaştık mı? (OffMeshLink devreye giriyor mu?)
        if (agent.isOnOffMeshLink)
        {
            // Merdivene bindik! TraverseLadder otomatik devralacak
            siegeTargetReached = true;
            Debug.Log($"[EnemyAI] {name} ★ MERDİVENE BINDI! Tırmanıyor...");
            return;
        }

        // 3. Merdiven başlangıcına yeterince yaklaştık mı?
        if (siegeTarget != null)
        {
            float dist = Vector3.Distance(transform.position, siegeTarget.position);
            if (dist < 1.5f)
            {
                // Çok yakınız, OffMeshLink devreye girmeli
                // Biraz bekleme payı ver, NavMesh linke bağlanacak
                siegeTargetReached = true;
                Debug.Log($"[EnemyAI] {name} ★ Merdiven dibine ulaştı! OffMeshLink bekliyor...");
                return;
            }

            // Merdivene koş
            agent.isStopped = false;
            agent.updateRotation = true;
            agent.stoppingDistance = 0.5f;
            agent.SetDestination(siegeTarget.position);
        }
    }

    private void FindBestTarget()
    {
        // Hedef ara
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

            // Zerg Limiti: Bu hedefe zaten 5 kişi saldırıyorsa ve ben saldırmıyorsam pas geç
            if (myEngagedTarget != candidate && IsTargetOverwhelmed(candidate)) continue;

            float dist = Vector3.Distance(transform.position, candidate.position);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = candidate;
            }
        }

        // ZİGZAK KORUMASI: Eğer mevcut hedefim varsa ve hala menzil içindeyse, 
        // yeni hedef SADECE eskisinden çok daha yakınsa değiştir (1.5m tolerans)
        if (currentTarget != null)
        {
            Health existingHealth = currentTarget.GetComponent<Health>();
            if (existingHealth != null && existingHealth.IsAlive)
            {
                float currentTargetDist = Vector3.Distance(transform.position, currentTarget.position);
                // Eğer yeni bulduğum en iyi hedef, eskisinden 1.5m'den daha yakınsa değiştir
                if (bestTarget != null && bestDist < currentTargetDist - 1.5f)
                {
                    currentTarget = bestTarget;
                    RegisterEngagement(currentTarget);
                }
                // Değilse, eski hedefe sadık kal - myEngagedTarget güncellenmesine gerek yok
            }
            else
            {
                // Eski hedef öldü, direkt yenisini al
                currentTarget = bestTarget;
                RegisterEngagement(currentTarget);
            }
        }
        else
        {
            // Hedefim yoktu, yenisini al
            currentTarget = bestTarget;
            RegisterEngagement(currentTarget);
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
            float effectiveAttackRange = Mathf.Max(attackRange, attackRange * 1.15f);

            // Eğer saldırı menzilindeysek DUR ve SIK
            if (dist <= effectiveAttackRange)
            {
                name = $"Enemy_RangedAttack_>{currentTarget.name}";
                agent.isStopped = true;
                
                // Hedefe Dön (Y eksenini TAMAMEN kilitleyerek)
                Vector3 lookPos = currentTarget.position;
                lookPos.y = transform.position.y;
                Vector3 dir = (lookPos - transform.position).normalized;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(dir);
                
                AttackTarget();
            }
            else
            {
                // Menzile girene kadar koş
                agent.isStopped = false;
                agent.updateRotation = true;
                agent.SetDestination(currentTarget.position);
            }
        }
        // --- MELEE MANTIK ---
        else
        {
            // OYUN MOTORU HASSASİYETİ (Tolerance)
            // NavMeshAgent bazen tam duramayıp hedeften 0.05m uzakta kalabilir.
            // Tolerans payı ekleyelim ki kilitlenip beklemesinler
            float effectiveAttackRange = Mathf.Max(attackRange, attackRange * 1.15f);

            if (dist <= effectiveAttackRange)
            {
                // Menzildeyiz: DUR ve SALDIRI
                agent.isStopped = true;
                agent.updateRotation = false;
                agent.ResetPath(); // İçine girmeyi önle!
                
                // Hedefe Dön
                Vector3 lookPos = currentTarget.position;
                lookPos.y = transform.position.y;
                Vector3 dir = (lookPos - transform.position).normalized;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(dir);

                AttackTarget();
            }
            else
            {
                // AYRIK SALDIRI - SURROUND MANTIĞI: SADECE YAKINDAYKEN etrafını sar
                Vector3 finalDestination = currentTarget.position;
                
                // Eğer hedefe yeterince YAKINSA (2x saldırı menzili) → etrafını sar
                // UZAKKEN → DÜZ KOŞ! (Eski kodda uzakken bile offset ekliyordu, bu yüzden yuvarlak koşuyorlardı!)
                if (dist < attackRange * 2.5f)
                {
                    uint myId = (uint)gameObject.GetInstanceID();
                    NetworkIdentity netIdent = GetComponent<NetworkIdentity>();
                    if (netIdent != null) myId = netIdent.netId;

                    float angle = (myId % 8) * 45f;
                    float radius = Mathf.Max(1.0f, attackRange - 0.5f);
                    
                    if (activeEngagements.ContainsKey(currentTarget) && activeEngagements[currentTarget] > 8)
                    {
                        radius += 1.5f;
                    }

                    Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * radius;
                    finalDestination = currentTarget.position + offset;
                }

                // Menzil dışındayız: Koş ama saldırı menzilinde dur
                agent.stoppingDistance = Mathf.Max(0.5f, attackRange - 0.3f);
                agent.isStopped = false;
                agent.updateRotation = true;
                agent.SetDestination(finalDestination);
            }
        }
    }

    public event System.Action OnAttack;



    private System.Collections.IEnumerator TraverseLadder()
    {
        isClimbing = true;
        agent.isStopped = true;

        // Tırmanma Animasyonu — Server + Tüm Client'lara gönder
        if (animator != null) animator.SetBool("Climb", true);
        RpcSetClimbAnimation(true);

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
            Vector3 dir = (lookPos - agent.transform.position).normalized;
            if (dir.sqrMagnitude > 0.01f)
                agent.transform.rotation = Quaternion.LookRotation(dir);

            elapsed += Time.deltaTime;
            yield return null;
        }

        agent.transform.position = endPos;
        agent.CompleteOffMeshLink();
        
        // Tırmanma animasyonunu kapat — Server + Tüm Client'lar
        if (animator != null) animator.SetBool("Climb", false);
        RpcSetClimbAnimation(false);
        
        agent.isStopped = false;
        agent.updateRotation = true;
        isClimbing = false;
    }

    [ClientRpc]
    private void RpcSetClimbAnimation(bool climbing)
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.SetBool("Climb", climbing);
        }
    }

    private void ResumeMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.updateRotation = true;
        }
    }

    // Gizmos ile Aggro alanını çiz (Debug için)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isRanged ? Color.cyan : Color.red;
        Gizmos.DrawWireSphere(transform.position, aggroRange);
    }
}
