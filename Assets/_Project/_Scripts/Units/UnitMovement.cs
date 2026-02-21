using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(UnitAttack))] // "Kas" sistemi zorunlu
public class UnitMovement : NetworkBehaviour
{
    public enum UnitState { Idle, Guarding, Chasing, Charging, Moving, Volley }

    [Header("State Info")]
    [SyncVar] public UnitState currentState = UnitState.Idle;
    [SyncVar] public int SquadIndex;

    [Header("Settings")]
    [SerializeField] public float moveSpeed = 3.5f; // Koşma Hızı (Inspector'dan ayarlanır)
    [SerializeField] private float guardRange = 10f; // Savunmadayken ne kadar uzağa baksın?
    [SerializeField] private float updateInterval = 0.25f; // Saniyede 4 kez karar ver

    [Header("Targeting")]
    [SerializeField] private LayerMask enemyLayerMask = -1; 
    [SerializeField] private float meleeAggroRange = 50f; // Melee: 50m görüş
    [SerializeField] private float rangedAggroRange = 40f; // Okçu: 40m görüş (User Request)
    [SerializeField] private float chargeWindup = 1.0f; // Geri geldi (1 saniye)
    [SerializeField] private string chargeTriggerName = "Charge";


    private IEnumerator TryRegisterCommander()
    {
        // 10 saniye boyunca komutanı ara
        for (int i = 0; i < 20; i++) 
        {
            PlayerUnitCommander commander = FindFirstObjectByType<PlayerUnitCommander>();
            if (commander != null)
            {
                commander.RegisterUnit(this);
                // Debug.Log($"[Unit {netId}] Komutan bulundu ve kayıt olundu!");
                yield break; // Bulduk, çık
            }
            yield return new WaitForSeconds(0.5f);
        }
        Debug.LogWarning($"[Unit {netId}] Komutan BULUNAMADI! (20 saniye denendi)");
    }

    private IEnumerator ChargeRoutine()
    {
        // 1. Kısa dur ve bağır
        if(agent.isOnNavMesh) 
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
        currentState = UnitState.Idle;

        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger("Charge");

        // 2. HIZLI Bekleme (0.3 saniye - Tüm askerler aynı anda tepki versin!)
        yield return new WaitForSeconds(0.3f);

        // 3. Saldır!
        if (netAnim != null) 
        {
            netAnim.ResetTrigger("Charge"); 
            Animator anim = GetComponent<Animator>();
            if(anim != null) anim.CrossFadeInFixedTime("Locomotion", 0.05f); 
        }
        
        chargingWindupActive = false;
        currentState = UnitState.Charging;
        currentTarget = null; 
        if(agent.isOnNavMesh) agent.isStopped = false;
    }

    [Header("Visual")]
    [SerializeField] private Transform modelTransform;
    [SerializeField] private LayerMask groundLayer = -1;
    [SerializeField] private float alignmentSpeed = 10f;

    [Header("Formation Marker")]
    [SerializeField] private GameObject formationMarkerPrefab; // Inspector'dan ata!
    private GameObject guardMarkerInstance; // Yerdeki aktif marker
    private float guardMarkerTimer = 0f; // 3 saniye sonra kapat
    private const float GUARD_MARKER_DURATION = 3f;

    // Components
    private NavMeshAgent agent;
    private UnitAttack attacker;
    private Transform currentTarget;
    private Vector3? guardPosition = null;
    private Quaternion? guardRotation = null;
    private Vector3 volleyTarget;
    private Coroutine commandDelayRoutine; // Yeni: Komut gecikmesi için (Charge Animasyonu)

    private Coroutine chargeRoutine;
    private bool isCharging;
    private bool chargingWindupActive;
    private bool isInVChargeMode = false; // V komutu aktif mi (tekrar V’de animasyon oynamasın)

    // CHARGE ATTACK (F1 Özel Saldırı)
    [Header("Charge Attack")]
    [SerializeField] private float chargeAttackRange = 4f; // Özel saldırı mesafesi (metre)
    [SerializeField] private float knockdownChance = 0.3f; // %30 yere düşürme şansı
    [SerializeField] private float chargeAttackDamageMultiplier = 1.5f; // 1.5x hasar
    private bool hasUsedChargeAttack = false; // Tek seferlik kullanım

    private float lastDecisionTime;
    private bool isAttackMoving = false; // Attack Move durumu

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        attacker = GetComponent<UnitAttack>();

        // Health eventine abone ol
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += OnDeathHandler;
            health.OnDamaged += OnDamagedHandler; // Arkadan saldırınca tepki ver!
        }

        // Animator bul (Model düzeltme için)
        if (modelTransform == null)
        {
            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.transform != transform) modelTransform = anim.transform;
        }

        // Tüm Animatorlar için Culling Mode = Always Animate (Uzakta donmasın)
        Animator[] allAnims = GetComponentsInChildren<Animator>();
        foreach(var anim in allAnims)
        {
            if(anim == null) continue;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if(anim.transform == transform) anim.applyRootMotion = false; // Sadece root'taki hareket etmesin
        }

        // Tüm SkinnedMeshRendererlar için Update When Offscreen = True (Uzakta yok olmasın)
        SkinnedMeshRenderer[] allSkinnedMeshes = GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach(var smr in allSkinnedMeshes)
        {
            if(smr == null) continue;
            smr.updateWhenOffscreen = true; // Bounds dışına çıksa bile hesapla (LOD sorunu yoksa görünür kılar)
        }
    }


    
    [Server]
    private void SnapToGround()
    {
        // 1. Önce fiziksel zemini bul (Havadan aşağı Ray at)
        Vector3 startPos = transform.position + Vector3.up * 5.0f;
        
        if (Physics.Raycast(startPos, Vector3.down, out RaycastHit hit, 100f))
        {
            // 2. Fiziksel zemine en yakın NavMesh noktasını bul
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
                agent.enabled = true;
            }
        }
        else
        {
             // Fallback
             if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit2, 20.0f, NavMesh.AllAreas))
             {
                 agent.Warp(navHit2.position);
             }
        }
    }

    // --- Actions ---

    [Server]
    public void MoveTo(Vector3 position, Quaternion? lookRotation = null, bool shieldWall = false, bool attackMove = false, bool isFollowMove = false)
    {
        // Eski rutinleri temizle
        if (commandDelayRoutine != null) StopCoroutine(commandDelayRoutine);
        
        // X veya F1 komutu geldi → V charge modunu kapat
        isInVChargeMode = false;
        hasUsedChargeAttack = false; // Yeni emir = yeni charge attack hakkı
        
        if (attackMove)
        {
            // SADECE F1 (attackMove) komutunda charge animasyonu oynat (1.3s)
            NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
            if (netAnim != null) netAnim.SetTrigger(chargeTriggerName);
            StartCoroutine(StopChargeAnimationAfterDelay(1.3f));
        }
        
        ExecuteMoveTo(position, lookRotation, shieldWall, attackMove, isFollowMove);
    }

    private System.Collections.IEnumerator StopChargeAnimationAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        // Charge animasyonunu kes, normal yürüme/koşma geçsin
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.ResetTrigger(chargeTriggerName);
        }
    }

    private void ExecuteMoveTo(Vector3 pos, Quaternion? rot, bool isWall, bool attackMove, bool isFollowMove = false)
    {
        currentState = UnitState.Moving;
        guardPosition = pos;
        
        // Follow hareketi değilse marker timerı başlat (C modunda marker görünmesin!)
        if (!isFollowMove)
        {
            guardMarkerTimer = GUARD_MARKER_DURATION;
        }
        else
        {
            guardMarkerTimer = 0f; // Follow sırasında marker kesinlikle kapalı
        }
        
        guardRotation = rot;
        isAttackMoving = attackMove;
        UpdateGuardMarker();
        
        if(agent.isOnNavMesh) agent.autoTraverseOffMeshLink = !isWall;
        
        TrySetDestination(pos);

        currentTarget = null;
        UnregisterEngagement(currentTarget);
    }

    [Server]
    public void ChargeNearestEnemy()
    {
        bool isRangedUnit = attacker != null && attacker.IsRanged;

        // CHARGE ANİMASYON: Sadece V modunda değilken oynat (V→V = animasyon yok, C/X→V = animasyon var)
        if (!isInVChargeMode)
        {
            NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
            if (netAnim != null) netAnim.SetTrigger(chargeTriggerName);
            StartCoroutine(StopChargeAnimationAfterDelay(1.3f));
        }
        isInVChargeMode = true;
        hasUsedChargeAttack = false; // Yeni V = yeni charge attack hakkı

        // OKÇULAR
        if (isRangedUnit)
        {
            currentState = UnitState.Charging;
            isAttackMoving = true;
            currentTarget = null;
            
            guardPosition = null;
            guardRotation = null;
            guardMarkerTimer = 0f;
            UpdateGuardMarker();
            
            PlayerUnitCommander commander = FindFirstObjectByType<PlayerUnitCommander>();
            if (commander != null) commander.ServerSetFollowing(SquadIndex, false);
            
            Transform enemy = FindNearestEnemy();
            if (enemy != null)
            {
                currentTarget = enemy;
                float dist = Vector3.Distance(transform.position, enemy.position);
                float range = attacker.GetRange();
                
                if (dist <= range)
                {
                    StopMovement();
                    attacker.Attack(enemy.GetComponent<IDamageable>());
                }
                else
                {
                    float stopDist = Mathf.Max(1f, range - 2f);
                    MoveToTarget(enemy, stopDist);
                }
            }
            return;
        }

        // MELEE
        currentState = UnitState.Charging;
        currentTarget = null;
        isAttackMoving = true;
        
        guardPosition = null;
        guardRotation = null;
        guardMarkerTimer = 0f;
        UpdateGuardMarker();
        
        PlayerUnitCommander commander2 = FindFirstObjectByType<PlayerUnitCommander>();
        if (commander2 != null)
        {
            commander2.ServerSetFollowing(SquadIndex, false);
        }
        
        Transform meleeEnemy = FindNearestEnemy();
        if (meleeEnemy != null)
        {
            currentTarget = meleeEnemy;
            EngageTarget(meleeEnemy);
        }
    }

    [Server]
    public void StartCharging()
    {
        // SPAM CHECK: Zaten Hucumdaysa tekrar baslatma
        if (isCharging) return;

        Debug.Log("[Server] Unit START CHARGING!");
        currentState = UnitState.Charging; // Mantıken Charging de kalsın ama Routine onu Idle'a çekecek
        currentTarget = null;
        isCharging = true;
        chargingWindupActive = true; // KİLİDİ AKTİF ET
        
        // Hareket Kilidi (Anında Dur)
        if(agent != null && agent.enabled && agent.isOnNavMesh) 
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }

        if (chargeRoutine != null) StopCoroutine(chargeRoutine);
        chargeRoutine = StartCoroutine(ChargeRoutine());
    }

    [Server]
    public void StopCharging()
    {
         if (chargeRoutine != null)
         {
             StopCoroutine(chargeRoutine);
             chargeRoutine = null;
         }

         chargingWindupActive = false;
            isCharging = false;

         if(currentState == UnitState.Charging) currentState = UnitState.Guarding;
    }



    [Server]
    public override void OnStartServer()
    {
        base.OnStartServer();

        // 1. ZEMİN FIX
        if(agent != null) agent.baseOffset = 0f; 
        SnapToGround();
        
        // 2. NAVMESH AYARLARI
        if (agent != null)
        {
            agent.speed = moveSpeed;
            agent.acceleration = 20f; 
            agent.angularSpeed = 2000f; 
            agent.autoBraking = false; 
            agent.stoppingDistance = 1.0f;
            agent.autoTraverseOffMeshLink = false; // MANUEL TIRMANMA İÇİN
        }

        // 3. KAYITLAR
        if (BattleManager.Instance != null) BattleManager.Instance.RegisterPlayerUnit(this);

        // Komutanı Bulma (Tekrar eden döngü ile - Race Condition Fix)
        StartCoroutine(TryRegisterCommander());

        // 4. Varsayılan State
        currentState = UnitState.Guarding;
        guardPosition = transform.position;
        guardMarkerTimer = GUARD_MARKER_DURATION;
        UpdateGuardMarker(); // İlk spawn marker
    }

    // ... (Aradaki kodlar) ...

    private void Update()
    {
        // Sadece Server karar verir
        if (!isServer) return;

        // Görsel Düzeltmeler (PERFORMANS: Her 3 frame'de bir)
        if (Time.frameCount % 3 == 0) AlignModelToGround();
        UpdateAnimations();

        // MERDİVEN (OffMeshLink) KONTROLÜ
        if (agent.isOnOffMeshLink && !isClimbing)
        {
            StartCoroutine(TraverseLadder());
            return;
        }

        // Karar Mekanizması (Brain)
        if (Time.time - lastDecisionTime >= updateInterval)
        {
            lastDecisionTime = Time.time;
            Think();
        }

        // Guard Marker timer (3 saniye sonra kapat)
        UpdateGuardMarker();
    }

    private bool isClimbing = false;

    private IEnumerator TraverseLadder()
    {
        isClimbing = true;
        currentState = UnitState.Moving; // State güncelle
        
        if(agent.enabled) agent.isStopped = true;

        // Animasyon
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger("ClimbTrigger"); // Veya SetBool("Climb", true)

        OffMeshLinkData data = agent.currentOffMeshLinkData;
        Vector3 startPos = agent.transform.position;
        Vector3 endPos = data.endPos + Vector3.up * agent.baseOffset;

        float duration = 2.5f; // Tırmanma hızı
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (this == null) yield break; // Obje yok olduysa çık

            // Lineer İnterpolasyon (Lerp) ile taşı
            transform.position = Vector3.Lerp(startPos, endPos, elapsed / duration);
            
            // Yüzünü duvara (bitişe) dön
            Vector3 lookPos = endPos;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.position = endPos;
        if(agent.enabled) agent.CompleteOffMeshLink();
        
        if(agent.enabled) agent.isStopped = false;
        isClimbing = false;
        
        // Nöbet pozisyonunu güncelle ki geri dönmeye çalışmasın
        guardPosition = endPos;
        guardMarkerTimer = GUARD_MARKER_DURATION;
        UpdateGuardMarker(); // Merdiven sonrası marker güncelle
    }

    [Server]
    private void Think()
    {
        // 1. Hedef Kontrolü (Öldü mü? Kayboldu mu?)
        if (currentTarget != null)
        {
            // Cache'li Health kontrolü (GetComponent yerine)
            Health targetHealth = currentTarget.GetComponent<Health>();
            if (targetHealth != null && !targetHealth.IsAlive)
            {
                currentTarget = null; // Hedef öldü, yenisini bul
            }
        }

        // 2. State Machine
        switch (currentState)
        {
            case UnitState.Idle:
                // Hiçbir şey yapma, bekle.
                StopMovement();
                break;

            case UnitState.Guarding:
                HandleGuarding();
                break;

            case UnitState.Charging:
                HandleCharging();
                break;
                
            case UnitState.Chasing:
                // Guard veya Charge içinden geçiş yapılır, burada sadece takip mantığı
                if(currentTarget != null) MoveToTarget(currentTarget);
                else 
                {
                    // V komutu verilmişse → yeni düşman ara (geri dönme!)
                    if (isAttackMoving || isCharging)
                        currentState = UnitState.Charging;
                    else
                        currentState = UnitState.Guarding;
                }
                break;

            case UnitState.Moving:
                HandleMoving();
                break;

            case UnitState.Volley:
                HandleVolley();
                break;
        }
    }

    private void HandleGuarding()
    {
        bool isRangedUnit = attacker != null && attacker.IsRanged;
        float attackRange = attacker != null ? attacker.GetRange() : 2f;

        // Eğer zaten bir hedefimiz varsa, kontrol et
        if (currentTarget != null)
        {
            float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
            
            // Hedef çok uzaklaştıysa veya öldüyse bırak
            float limitRange = isRangedUnit ? attackRange : (attackRange + 2f); // Melee: sadece yakındakiler
            if (distToTarget > limitRange || !currentTarget.gameObject.activeInHierarchy)
            {
                currentTarget = null;
            }
            else
            {
                // SALDIRI! Ama HAREKET ETME (Formasyon bozulmasın)
                if (distToTarget <= attackRange)
                {
                    // Attack range içinde → VUR!
                    StopMovement();
                    attacker.Attack(currentTarget.GetComponent<IDamageable>());
                }
                // Okçu menzildeyse ateş et, melee beklesin
                return;
            }
        }

        // Yeni Hedef Ara
        // MELEE: Sadece saldırı menzili + 3m (FORMASYON BOZMASIN!)
        // OKÇU: Tam menzilinde ara
        float scanRange = isRangedUnit ? attackRange : (attackRange + 3f);
        
        System.Predicate<Transform> filter = t => !IsTargetOverwhelmed(t); // HER ZAMAN swarm filtresi!
        
        Transform found = AcquireTarget(scanRange, filter);
        
        if (found != null)
        {
            RegisterEngagement(found); // HER ZAMAN kayıt ol (swarm filtresi için)
            currentTarget = found;
            
            float dist = Vector3.Distance(transform.position, found.position);
            if (dist <= attackRange)
            {
                StopMovement();
                attacker.Attack(found.GetComponent<IDamageable>());
            }
            // Melee: Menzil dışındaysa BEKLE, formasyon bozma!
        }
        else
        {
            if (isAttackMoving)
            {
                currentState = UnitState.Moving;
                return;
            }

            // Normal Mode: Pozisyonun biraz kaydıysa düzelt
            ReturnToPost();
            
            // Rotasyonu Koru
            if (guardRotation.HasValue && Vector3.Distance(transform.position, guardPosition.Value) < 0.5f)
            {
                 transform.rotation = Quaternion.Slerp(transform.rotation, guardRotation.Value, Time.deltaTime * 5f);
            }
        }
    }

    private void HandleCharging()
    {
        if (chargingWindupActive)
        {
            StopMovement();
            return;
        }

        // Hedef Bul (Yoksa veya Öldüyse)
        if (currentTarget == null || (currentTarget != null && !currentTarget.gameObject.activeInHierarchy))
        {
             bool isRangedUnit = attacker != null && attacker.IsRanged;
             float searchRange = isRangedUnit ? attacker.GetRange() : 300f;
             // FARKLI DÜŞMAN SEÇ: Aynı hedefe üşüşmesinler!
             currentTarget = AcquireTarget(searchRange, t => !IsTargetOverwhelmed(t));
        }
        
        if (currentTarget == null)
        {
            // HEDEF YOK → Koridorun sonundaysa DÜŞMAN ARA! Durma!
            // Son gidilen yere vardıysa (agent durmuşsa) tekrar ara
            if (agent.isOnNavMesh && (!agent.hasPath || agent.remainingDistance < 2f))
            {
                // Her 0.5 saniyede tekrar ara
                if (Time.frameCount % 30 == 0)
                {
                    currentTarget = AcquireTarget(300f, t => !IsTargetOverwhelmed(t));
                    if (currentTarget != null) return; // Buldu, sonraki frame saldıracak
                }
            }
        }
        else
        {
            // Hedefe Git ve Saldır
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            float range = attacker.GetRange();
            bool isRangedUnit = attacker != null && attacker.IsRanged;

            // ★ CHARGE ATTACK: 4m kala tek seferlik özel saldırı (sadece melee)
            if (!isRangedUnit && !hasUsedChargeAttack && dist <= chargeAttackRange && dist > range)
            {
                TriggerChargeAttack(currentTarget);
            }

            if (dist <= range)
            {
                // Menzilde! Durdur + Hedefe dön + Vur!
                if(agent.isOnNavMesh) 
                {
                    agent.isStopped = true;
                    agent.updateRotation = false;
                }
                
                // Yüzünü hedefe dön
                Vector3 lookDir = currentTarget.position - transform.position;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(lookDir);
                
                // SALDIRI (cooldown içinde kontrol ediliyor)
                attacker.TryAttack(currentTarget);
            }
            else
            {
                // Hedefe koş
                float stopDist = isRangedUnit ? Mathf.Max(1f, range - 2f) : 1.0f;
                if(agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                    agent.stoppingDistance = stopDist;
                    agent.SetDestination(currentTarget.position);
                }
            }
        }
    }

    // ★ CHARGE ATTACK: F1/V hücumunda düşmana yaklaşınca tek seferlik özel saldırı
    [Server]
    private void TriggerChargeAttack(Transform target)
    {
        if (target == null || hasUsedChargeAttack) return;
        hasUsedChargeAttack = true;

        Debug.Log($"[Unit {netId}] ★ CHARGE ATTACK! Target: {target.name}");

        // Charge Attack animasyonu tetikle
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger("ChargeAttack");

        // HASAR: Normal hasar × 1.5
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null)
        {
            int baseDamage = attacker != null ? attacker.GetDamage() : 10;
            int chargeDamage = Mathf.RoundToInt(baseDamage * chargeAttackDamageMultiplier);
            targetHealth.TakeDamage(chargeDamage, transform.position);

            // %30 KNOCKDOWN: Düşman yere düşsün!
            if (Random.value < knockdownChance)
            {
                Debug.Log($"[Unit {netId}] ★ KNOCKDOWN! {target.name} yere düştü!");
                targetHealth.ApplyKnockdown(1f); // 1 saniye yerde kal
            }
        }
    }
    private void HandleVolley()
    {
        // Hız kontrolü (Charge'dan kalma hızlanmayı sıfırla)
        if(agent.isOnNavMesh) agent.speed = moveSpeed;

        // Hedefe (Alana) olan mesafe
        float dist = Vector3.Distance(transform.position, volleyTarget);
        float range = attacker.GetRange(); // Okçu menzili (50m)

        if (dist <= range)
        {
            // Menzildeyiz: Dur ve Ateş Et
            if(agent.isOnNavMesh) 
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            // Atış Yap (Cooldown kontrolü UnitAttack içinde)
            if(attacker.CanAttack())
            {
                attacker.FireVolley(volleyTarget);
            }
        }
        else
        {
            // Menzilde Değiliz: Yürü
            if(agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(volleyTarget);
                // Not: TrySetDestination kullanmıyoruz çünkü direkt hedefe gitmeye çalışıyoruz, 
                // ama menzile girince duracağız.
            }
        }
    }

    [Server]
    public void OrderVolley(Vector3 targetPoint)
    {
        // Varsa eski rutini durdur
        if (commandDelayRoutine != null) StopCoroutine(commandDelayRoutine);

        // Yeni rutini başlat (Animasyon -> Hareket)
        commandDelayRoutine = StartCoroutine(ChargeToVolleyRoutine(targetPoint));
    }

    private IEnumerator ChargeToVolleyRoutine(Vector3 targetPoint)
    {
        // 1. Dur ve Bağır
        StopMovement(); // Hareketi kes
        
        // Animasyon (Charge)
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger(chargeTriggerName);
        
        // 1 Saniye Bekle (User Request)
        yield return new WaitForSeconds(1.0f);

        // 2. Aksiyonu Başlat
        currentState = UnitState.Volley;
        volleyTarget = targetPoint;
        currentTarget = null;
        isAttackMoving = false;
        
        if(agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPoint);
        }
    }

    // --- SWARMING LOGIC (Hedef Dağılımı) ---
    private static Dictionary<Transform, int> activeEngagements = new Dictionary<Transform, int>();
    private Transform myEngagedTarget = null; // Bu unit'in şu anki hedefi (takip için)

    private void RegisterEngagement(Transform target)
    {
        if (target == null) return;
        
        // Zaten aynı hedefe kayıtlıysak tekrar ekleme!
        if (myEngagedTarget == target) return;
        
        // Eski hedeften çık
        UnregisterEngagement(myEngagedTarget);
        
        // Yeni hedefe kayıt ol
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
        if (target == null) return false;
        return activeEngagements.ContainsKey(target) && activeEngagements[target] >= 5; // Max 5 kişi aynı düşmana
    }

    private void HandleMoving()
    {
        // Debug.Log($"[Unit {netId}] HandleMoving - isAttackMoving:{isAttackMoving}, guardPos:{guardPosition}, State:{currentState}");
        
        // ATTACK MOVE KONTROLÜ
        if (isAttackMoving)
        {
            // HIZLANDIR (Charge Effect - 1.2x)
            if(agent.isOnNavMesh) agent.speed = moveSpeed * 1.2f; 

            // F1 KORIDOR MODU: 90° koni ile düşman ara (geniş alan)
            Transform found = AcquireTargetInCone(20f, 90f, t => !IsTargetOverwhelmed(t));

            if (found != null)
            {
                // Düşman bulduk! Hareketi kes ve savaş
                if(agent.isOnNavMesh) agent.speed = moveSpeed; // Hızı normale döndür
                
                RegisterEngagement(found); // Kayıt ol
                
                currentState = UnitState.Guarding;
                currentTarget = found;
                EngageTarget(found);
                return;
            }

            // Hedefe yaklaştık mı? (10m içinde)
            if (guardPosition.HasValue)
            {
                float distToGoal = Vector3.Distance(transform.position, guardPosition.Value);
                if (distToGoal <= 10f)
                {
                    // KORIDORUN SONUNA YAKLAŞTIK → SALDIRIi MODU!
                    Transform nearbyEnemy = AcquireTarget(30f, t => !IsTargetOverwhelmed(t));
                    if (nearbyEnemy != null)
                    {
                        if(agent.isOnNavMesh) agent.speed = moveSpeed;
                        RegisterEngagement(nearbyEnemy);
                        currentState = UnitState.Charging;
                        currentTarget = nearbyEnemy;
                        EngageTarget(nearbyEnemy);
                        return;
                    }
                }
            }
        }

        // Hedef yoksa Idle
        if (!guardPosition.HasValue)
        {
            StopMovement();
            currentState = UnitState.Idle;
            return;
        }

        // Mesafe Kontrolü
        float dist = Vector3.Distance(transform.position, guardPosition.Value);
        if (dist <= 1.0f) // Vardık
        {
            StopMovement();
            currentState = UnitState.Charging; // Vardık → saldırı moduna geç!
            
            // Hemen dönmeye başla (Manuel Rotation)
            if (guardRotation.HasValue) 
            {
                transform.rotation = guardRotation.Value; // Anlık düzeltme (veya Lerp ile yapılabilir)
            }
        }
        else
        {
            // Yürümeye devam
            // Debug.Log($"[Unit {netId}] Continuing to move. Distance: {dist}");
            TrySetDestination(guardPosition.Value);
        }
    }

    // F1 KORIDOR MODU: Sadece önümüzdeki düşmanları bul (koni şeklinde)
    private Transform AcquireTargetInCone(float range, float coneAngle, System.Predicate<Transform> verificationCallback = null)
    {
        Transform bestTarget = null;
        float bestDistance = range;

        // Önce OverlapSphere ile tüm düşmanları bul
        Collider[] hits = Physics.OverlapSphere(transform.position, range, enemyLayerMask);
        
        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            // Arkadaş Kontrolü
            UnitMovement otherUnit = hit.GetComponentInParent<UnitMovement>();
            if (otherUnit != null && otherUnit.connectionToClient == this.connectionToClient) continue;
            
            if (hit.GetComponentInParent<PlayerController>() != null) continue;

            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            // ÖLÜ KONTROLÜ: Ölü hedeflere saldırma!
            Health health = hit.GetComponentInParent<Health>();
            if (health != null && !health.IsAlive) continue;

            NetworkIdentity identity = hit.GetComponentInParent<NetworkIdentity>();
            Transform candidate = identity != null ? identity.transform : hit.transform;

            // Verification callback
            if (verificationCallback != null && !verificationCallback(candidate)) continue;

            // KONİ KONTROLÜ: Sadece önümüzdeki düşmanlar
            Vector3 dirToTarget = (candidate.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToTarget);
            
            if (angle > coneAngle) continue; // Koni dışında, atla

            // En yakını bul
            float dist = Vector3.Distance(transform.position, candidate.position);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    private Transform AcquireTarget(float range, System.Predicate<Transform> verificationCallback = null)
    {
        Transform bestTarget = null;
        float bestDistance = range;

        if (BattleManager.Instance != null)
        {
            Transform bmTarget = BattleManager.Instance.GetNearestEnemyForUnit(transform.position, range);
            // BattleManager target'ı da callback'ten geçir
            if (bmTarget != null && (verificationCallback == null || verificationCallback(bmTarget)))
            {
                bestTarget = bmTarget;
                bestDistance = Vector3.Distance(transform.position, bmTarget.position);
            }
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, range, enemyLayerMask);
        EvaluateHitResults(hits, ref bestTarget, ref bestDistance, verificationCallback);

        // Eğer maskeden sonuç yoksa, son çare tüm layer'larda ara (dostları yine filtreleyecek)
        if (bestTarget == null)
        {
            Collider[] anyHits = Physics.OverlapSphere(transform.position, range, ~0);
            EvaluateHitResults(anyHits, ref bestTarget, ref bestDistance, verificationCallback);
        }

        return bestTarget;
    }

    private Transform FindNearestEnemy()
    {
        // Okçu mu Melee mi?
        bool isRanged = attacker != null && attacker.IsRanged;
        float searchRange = isRanged ? rangedAggroRange : meleeAggroRange;
        
        // En yakın düşmanı bul (engagement limiti olmadan)
        return AcquireTarget(searchRange, null);
    }

    // OKÇU GÖRÜŞ HATTI KONTROLÜ: Hedefe net atış var mı?
    private bool HasLineOfSight(Transform target, float maxDistance)
    {
        if (target == null) return false;

        // Göğüs hizasından ateş et (ayak seviyesinden değil)
        Vector3 startPos = transform.position + Vector3.up * 1.5f;
        Vector3 targetPos = target.position + Vector3.up * 1.0f;

        Vector3 direction = (targetPos - startPos).normalized;
        float distance = Vector3.Distance(startPos, targetPos);

        // Raycast: Arada engel var mı?
        if (Physics.Raycast(startPos, direction, out RaycastHit hit, Mathf.Min(distance, maxDistance)))
        {
            // Hedefe çarptı mı yoksa duvara mı?
            Transform hitRoot = hit.transform.root;
            Transform targetRoot = target.root;

            // Eğer hedefe çarptıysa → Görüş var
            if (hitRoot == targetRoot || hit.transform == target)
            {
                return true;
            }

            // Başka bir şeye çarptı (duvar, bina, vs.) → Görüş yok
            return false;
        }

        // Hiçbir şeye çarpmadı → Açık alan, görüş var
        return true;
    }

    private void EvaluateHitResults(Collider[] hits, ref Transform bestTarget, ref float bestDistance, System.Predicate<Transform> verificationCallback)
    {
        // Ranged için Rastgelelik (Distributed Fire)
        bool isRanged = (attacker != null && attacker.GetRange() > 5.0f); // 5m üstüne Ranged kabul edelim
        System.Collections.Generic.List<Transform> rangedCandidates = null;
        if (isRanged) rangedCandidates = new System.Collections.Generic.List<Transform>();

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            // Arkadaş Kontrolü (Aynı bağlantı/takım ise vurma)
            UnitMovement otherUnit = hit.GetComponentInParent<UnitMovement>();
            if (otherUnit != null && otherUnit.connectionToClient == this.connectionToClient) continue; 
            
            if (hit.GetComponentInParent<PlayerController>() != null) continue;

            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            // ÖLÜ KONTROLÜ: Ölü hedeflere saldırma!
            Health health = hit.GetComponentInParent<Health>();
            if (health != null && !health.IsAlive) continue;

            NetworkIdentity identity = hit.GetComponentInParent<NetworkIdentity>();
            Transform candidate = identity != null ? identity.transform : hit.transform;

            // CUSTOM SORGULAR (Örn: Swarm Limit)
            if (verificationCallback != null && !verificationCallback(candidate)) continue;

            // TARGET DAĞILIMI: Bu düşmana zaten 5 kişi saldırıyorsa atla
            if (BattleManager.Instance != null && !BattleManager.Instance.CanEngageEnemy(candidate))
            {
                continue; // Bu düşman dolu, başkasını bul
            }

            float dist = Vector3.Distance(transform.position, candidate.position);
            
            if (isRanged)
            {
                // OKÇU: Görüş hattı kontrolü - Duvar arkasındaki düşmanları atla
                if (!HasLineOfSight(candidate, dist))
                {
                    continue; // Görüş yok, bu hedefi atla
                }

                // Menzildeyse ve görünüyorsa listeye ekle
                rangedCandidates.Add(candidate);
            }
            else
            {
                // Melee: En yakını bul (LOS gerekmez, yaklaşıp vurur)
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestTarget = candidate;
                }
            }
        }

        // Ranged ise Listeden Rastgele Seç
        if (isRanged && rangedCandidates != null && rangedCandidates.Count > 0)
        {
            // Eğer hali hazırda bir hedefo varsa ve hala geçerliyse
            // %70 ihtimalle onu değiştirmesin (Sürekli hedef değiştirmesin)
            if (currentTarget != null && rangedCandidates.Contains(currentTarget) && Random.value > 0.3f)
            {
                bestTarget = currentTarget;
            }
            else
            {
                bestTarget = rangedCandidates[Random.Range(0, rangedCandidates.Count)];
            }
        }
    }

    private void EngageTarget(Transform target)
    {
        // Yeni hedef mi? Kayıt ol
        if (target != currentTarget)
        {
            // Eski hedeften çık
            if (currentTarget != null && BattleManager.Instance != null)
            {
                BattleManager.Instance.UnregisterEnemyEngagement(currentTarget);
            }
            
            // Yeni hedefe kayıt ol
            if (target != null && BattleManager.Instance != null)
            {
                BattleManager.Instance.RegisterEnemyEngagement(target);
            }
        }

        float dist = Vector3.Distance(transform.position, target.position);
        float attackRange = attacker != null ? attacker.GetRange() : 2.0f; // Gerçek menzili kullan
        bool isRangedUnit = attacker != null && attacker.IsRanged;

        if (dist <= attackRange)
        {
            // Menzilde! Vur!
            StopMovement();
            attacker.Attack(target.GetComponent<IDamageable>());
        }
        else
        {
            // Menzil dışında, yaklaş
            // STAND YOUR GROUND: Normal Guarding modunda hareket etme!
            // AMA ATTACK MOVE ise (isAttackMoving) saldır, kovala!
            // VEYA: Menzil içindeyse (Guard Zone) saldır (User Request: Vardıklarında saldırıya gitsinler)
            if (currentState != UnitState.Guarding || isAttackMoving || dist <= guardRange)
            {
                if (isAttackMoving && agent.isOnNavMesh) 
                {
                    currentState = UnitState.Chasing; // Aktif takip moduna geç
                }
                
                // Okçular için: Tam menzilde dur (25m ise 25m'de dur)
                // Melee için: Yakına git (1m)
                float stopDist = isRangedUnit ? (attackRange - 2f) : 1.0f; // Okçu: Menzil-2m, Melee: 1m
                stopDist = Mathf.Max(1.0f, stopDist); // Minimum 1m
                
                MoveToTarget(target, stopDist);
            }
            else
            {
                 // Eğer Guarding ise ve menzilde değilse, sadece dönüp bakabilir (Opsiyonel)
                 // transform.LookAt(target); // Şimdilik kapalı, formasyonu bozmasın
                 StopMovement();
            }
        }
    }

    private void OnDestroy()
    {
        UnregisterEngagement(myEngagedTarget);

        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath -= OnDeathHandler;
            health.OnDamaged -= OnDamagedHandler;
        }

        // Kayıt Silme
        if (BattleManager.Instance != null) BattleManager.Instance.UnregisterPlayerUnit(this);

        // Marker temizle
        if (guardMarkerInstance != null) Destroy(guardMarkerInstance);
    }

    // FORMATION MARKER: Guard pozisyonunda zemin işaretçisi
    private void UpdateGuardMarker()
    {
        if (formationMarkerPrefab == null) return;

        // Timer azalt
        if (guardMarkerTimer > 0f)
            guardMarkerTimer -= Time.deltaTime;

        // Göster: guardPosition var + timer aktif + saldırı modunda değil + takip modunda değil
        if (guardPosition.HasValue && !isAttackMoving && guardMarkerTimer > 0f)
        {
            if (guardMarkerInstance == null)
            {
                guardMarkerInstance = Instantiate(formationMarkerPrefab);
                foreach (var col in guardMarkerInstance.GetComponentsInChildren<Collider>())
                {
                    Destroy(col);
                }
            }
            guardMarkerInstance.transform.position = guardPosition.Value + Vector3.up * 0.05f;
            guardMarkerInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            guardMarkerInstance.SetActive(true);
        }
        else
        {
            // Timer bitti veya guard yok → marker kapat
            if (guardMarkerInstance != null)
            {
                guardMarkerInstance.SetActive(false);
            }
        }
    }

    // REACTIVE DEFENSE: Hasar alınca saldırana dön ve saldır!
    private void OnDamagedHandler(Transform attacker)
    {
        if (!isServer) return;
        if (attacker == null) return;
        
        // Zaten bir hedefiniz varsa değiştirme
        if (currentTarget != null) return;
        
        // Sadece Guarding veya Idle'daysa tepki ver  
        if (currentState != UnitState.Guarding && currentState != UnitState.Idle) return;
        
        // Saldıran düşman mı kontrol et (aynı takımdan biri olmasın)
        EnemyAI enemyAI = attacker.GetComponent<EnemyAI>();
        if (enemyAI == null) enemyAI = attacker.GetComponentInParent<EnemyAI>();
        if (enemyAI == null) return; // Düşman değilse yoksay
        
        // TEPKİ! Dön ve saldır!
        currentTarget = attacker.transform;
        
        // Yüzünü döndür
        Vector3 lookPos = attacker.transform.position;
        lookPos.y = transform.position.y;
        transform.LookAt(lookPos);
    }

    private void OnDeathHandler()
    {
        UnregisterEngagement(currentTarget);

        // Death Animation (FORCE)
        Animator anim = GetComponentInChildren<Animator>();
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        
        // Network Sync'i durdur (Yoksa Idle'a geri atıyor)
        if (netAnim != null) netAnim.enabled = false;

        if(anim != null) 
        {
            anim.enabled = true; 
            anim.SetFloat("Speed", 0f);
            anim.SetBool("IsMoving", false);
            anim.SetTrigger("Die"); // TRIGGER kullan! CrossFadeInFixedTime çalışmıyor
        }
        
        if(agent != null) agent.enabled = false;
        
        Collider col = GetComponent<Collider>();
        if(col != null) col.enabled = false; 

        // QuickOutline KAPAT (Ölü askerlerde outline olmasın)
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            Outline outline = rend.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        // Yerle hizala (Floating Fix)
        SnapCorpseToGround();

        currentState = UnitState.Idle; 

        // Ceset Yönetimi
        if (CorpseManager.Instance != null) 
        {
            CorpseManager.Instance.RegisterCorpse(gameObject);
        }
        else
        {
            Destroy(gameObject, 30f);
        }

        // AI'ı HEMEN kapatma! Animasyon oynasın, sonra kapat
        StartCoroutine(DisableAfterDeath());
    }

    private IEnumerator DisableAfterDeath()
    {
        // Ölüm animasyonunun süresini bekle
        yield return new WaitForSeconds(2.5f);
        
        // Animator'ı tamamen kapat (Idle'a dönmesin)
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.enabled = false;
        
        // AI'ı kapat
        enabled = false;
    }

    private void SnapCorpseToGround()
    {
        // Cesedi yere yapıştır (Havada kalmasın!) - 20m yukarıdan ray at
        Vector3 rayStart = transform.position + Vector3.up * 10f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 20f))
        {
            transform.position = hit.point;
        }
        else
        {
            // Raycast başarısız - NavMesh'e yapıştır
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit navHit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                transform.position = navHit.position;
            }
        }
    }
        


    private void MoveToTarget(Transform target, float stopDist = 1.0f)
    {
        // Eğer yeni bir hedefe hareket ediyorsak, mevcut hedefi bırak
        if (currentTarget != null && currentTarget != target)
        {
            UnregisterEngagement(currentTarget);
        }
        currentTarget = target; // MoveToTarget genellikle bir hedefi takip etmek için kullanılır
        TrySetDestination(target.position, stopDist);
    }

    private void StopMovement()
    {
        UnregisterEngagement(currentTarget);
        currentTarget = null;
        
        if(agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void ReturnToPost()
    {
        if (guardPosition.HasValue)
        {
            if (Vector3.Distance(transform.position, guardPosition.Value) > 1.0f)
            {
                TrySetDestination(guardPosition.Value);
            }
            else
            {
                StopMovement(); // Yerine geldi, bekle
                
                // Rotasyonu Koru (Formation Facing)
                if (guardRotation.HasValue)
                {
                     transform.rotation = Quaternion.Slerp(transform.rotation, guardRotation.Value, Time.deltaTime * 5f);
                }
            }
        }
    }

    // NavMesh hedefi güvenli şekilde ayarla
    // NavMesh hedefi güvenli şekilde ayarla
    private void TrySetDestination(Vector3 targetPos, float stopDist = 1.0f)
    {
        if (agent == null || !agent.enabled) return;

        // Eğer NavMesh'ten düştüyse tekrar warp etmeyi dene
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit reHit, 10f, NavMesh.AllAreas))
            {
                agent.Warp(reHit.position);
            }
        }

        // Hedef nokta NavMesh üzerinde mi? Değilse yakındaki geçerli noktaya yaklaştır
        Vector3 finalPos = targetPos;
        if (NavMesh.SamplePosition(targetPos, out NavMeshHit destHit, 5f, NavMesh.AllAreas))
        {
            finalPos = destHit.position;
        }

        if (agent.isOnNavMesh)
        {
            agent.stoppingDistance = stopDist; 
            agent.isStopped = false;
            agent.SetDestination(finalPos);
        }
    }

    // --- Visuals ---
    private Animator _cachedAnimator;
    private bool _animatorCached = false;

    private void UpdateAnimations()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            float speed = agent.velocity.magnitude;
            // Animator'\u0131 cache'le (her frame GetComponent yapma!)
            if (!_animatorCached)
            {
                _cachedAnimator = GetComponent<Animator>();
                _animatorCached = true;
            }
            if (_cachedAnimator != null) _cachedAnimator.SetFloat("Speed", speed);
        }
    }

    private void AlignModelToGround()
    {
        if(modelTransform == null) return;
        
        Ray ray = new Ray(transform.position + Vector3.up, Vector3.down);
         if (Physics.Raycast(ray, out RaycastHit hit, 3f, groundLayer))
        {
             Quaternion targetRotation = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
             modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRotation, Time.deltaTime * alignmentSpeed);
        }
        else
        {
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, transform.rotation, Time.deltaTime * alignmentSpeed);
        }
    }
}
