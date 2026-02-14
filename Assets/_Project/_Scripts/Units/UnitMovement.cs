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
        // 1. Önce Dur ve Bağır (Anlık)
        if(agent.isOnNavMesh) 
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
        currentState = UnitState.Idle; // Animasyon oynarken hareket etmesin

        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger("Charge");

        // 2. Bekleme (1 Saniye - User Request: Yarıda Kesilsin)
        yield return new WaitForSeconds(chargeWindup); // Inspector'dan 1.0 ayarlı olmalı

        // 3. Saldır! (Animasyonu Zorla Kes)
        if (netAnim != null) 
        {
            netAnim.ResetTrigger("Charge"); 
            // ANIMASYONU KESMEK İÇİN: "Locomotion" veya "Move" state'ine zorla geçiş yap
            Animator anim = GetComponent<Animator>();
            if(anim != null) 
            {
                // FORCE CUT: 0.1 saniyede geçiş yap (Neredeyse anında)
                // Eğer state adı farklıysa (örn: "Run", "Blend Tree") burayı güncellemek gerekebilir.
                anim.CrossFadeInFixedTime("Locomotion", 0.05f); 
            }
        }
        
        chargingWindupActive = false; // Kilidi aç
        currentState = UnitState.Charging; // Tekrar Charge moda al
        currentTarget = null; 
        if(agent.isOnNavMesh) agent.isStopped = false;
        
        Debug.Log($"[Unit {netId}] HÜCUM BAŞLADI (1sn Cut)!");
    }

    [Header("Visual")]
    [SerializeField] private Transform modelTransform;
    [SerializeField] private LayerMask groundLayer = -1;
    [SerializeField] private float alignmentSpeed = 10f;

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
    public void MoveTo(Vector3 position, Quaternion? lookRotation = null, bool shieldWall = false, bool attackMove = false)
    {
        // Eski rutinleri temizle
        if (commandDelayRoutine != null) StopCoroutine(commandDelayRoutine);
        
        if (attackMove)
        {
            // F1 Saldırı emri: Charge animasyonu tetikle AMA HEMEN HAREKET ET (GECİKME YOK!)
            NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
            if (netAnim != null) netAnim.SetTrigger(chargeTriggerName);
            
            // ANINDA hareket et - bekleme yok!
            ExecuteMoveTo(position, lookRotation, shieldWall, attackMove);
        }
        else
        {
            // Normal hareket ise direkt git
            ExecuteMoveTo(position, lookRotation, shieldWall, attackMove);
        }
    }

    private void ExecuteMoveTo(Vector3 pos, Quaternion? rot, bool isWall, bool attackMove)
    {
        Debug.Log($"[Unit {netId}] ExecuteMoveTo called! Pos: {pos}, AttackMove: {attackMove}");
        
        currentState = UnitState.Moving;
        guardPosition = pos;
        guardRotation = rot;
        isAttackMoving = attackMove;
        
        // ... (Kalan logic aynı)
        // Duvar modu iptal edildiyse, NavMeshLink kullanmasın
        if(agent.isOnNavMesh) agent.autoTraverseOffMeshLink = !isWall;
        
        TrySetDestination(pos);

        // Eğer hedef varsa unut (yeni emir geldi)
        currentTarget = null;
        UnregisterEngagement(currentTarget);
    }

    [Server]
    public void ChargeNearestEnemy()
    {
        Debug.Log($"[Unit {netId}] V KEY CHARGE! Switching to Charging state...");
        
        // Charging state'e geç
        currentState = UnitState.Charging;
        currentTarget = null; // Yeni hedef bul
        isAttackMoving = true; // Attack move aktif et
        
        // Follow modunu kapat
        PlayerUnitCommander commander = FindFirstObjectByType<PlayerUnitCommander>();
        if (commander != null)
        {
            commander.ServerSetFollowing(SquadIndex, false);
        }
        
        // Hemen hedef ara ve saldır
        Transform enemy = FindNearestEnemy();
        if (enemy != null)
        {
            Debug.Log($"[Unit {netId}] Found enemy! Engaging: {enemy.name}");
            currentTarget = enemy;
            EngageTarget(enemy);
        }
        else
        {
            Debug.LogWarning($"[Unit {netId}] V KEY: No enemy found in range!");
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
        guardPosition = transform.position; // SnapToGround'dan sonra al ki kayma yapmasın
    }

    // ... (Aradaki kodlar) ...

    private void Update()
    {
        // Sadece Server karar verir
        if (!isServer) return;

        // Görsel Düzeltmeler (Herkes için çalışabilir ama logic serverda)
        AlignModelToGround();
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
    }

    [Server]
    private void Think()
    {
        // 1. Hedef Kontrolü (Öldü mü? Kayboldu mu?)
        if (currentTarget != null)
        {
            if (currentTarget.GetComponent<Health>() is Health h && h.CurrentHealth <= 0)
            {
                currentTarget = null; // Hedef öldü, yenisini bul
            }
            else if (currentTarget.GetComponentInParent<UnitMovement>() != null)
            {
                // Dost hedef seçilmişse iptal et
                currentTarget = null;
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
                else currentState = UnitState.Guarding; // Hedef yoksa nöbete dön
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
        // Eğer zaten bir hedefimiz varsa, menzilden çıktı mı kontrol et
        if (currentTarget != null)
        {
            float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
            
            // Eğer hedef çok uzaklaştıysa veya öldüyse bırak
            float limitRange = Mathf.Max(guardRange, attacker.GetRange()); // Okçular için menzili dikkate al
            if (distToTarget > limitRange || !currentTarget.gameObject.activeInHierarchy)
            {
                currentTarget = null;
                // ReturnToPost(); // Gerek yok, zaten yerindeyiz (hareket etmiyoruz)
            }
            else
            {
                // Menzildeyse VUR, değilse BEKLE (Hareket Etme!)
                EngageTarget(currentTarget);
                return;
            }
        }

        // Yeni Hedef Ara (Sadece Vurabileceği mesafedekiler - Dizilişi bozmamak için)
        // Stand Your Ground: Sadece attackRange içindekilere saldır, uzaktakine gitme.
        float scanningRange = Mathf.Max(guardRange, attacker.GetRange()); // Okçuysan uzağı gör
        
        // Eğer HÜCUM MODUNDAYSA: Dolu hedefleri pas geç (Swarm Filter)
        System.Predicate<Transform> filter = isAttackMoving ? (t => !IsTargetOverwhelmed(t)) : null;
        
        Transform found = AcquireTarget(scanningRange, filter); // Geniş ara, ama EngageTarget gitmeyecek.
        
        if (found != null)
        {
            // Hücum modunda Swarm Limit için kayıt olalım (HandleMoving'de olduğu gibi)
            if (isAttackMoving) RegisterEngagement(found);
            
            currentTarget = found;
            EngageTarget(currentTarget);
        }
        else
        {
            // Hedef yoksa...
            if (isAttackMoving)
            {
                // HÜCUM MANTIĞI: Düşman bitti ama hala menzile girmedik veya yolumuza devam etmeliyiz.
                // Guarding'de beklemek yerine KOŞMAYA DEVAM ET!
                currentState = UnitState.Moving;
                return;
            }

            // Normal Mode: Pozisyonun biraz kaydıysa düzelt (Micro-correction)
            ReturnToPost();
            
            // Rotasyonu Koru (Formation Facing - Enemy yoksa öne bak)
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

        // Hedef Bul (Yoksa veya Ödüyse)
        if (currentTarget == null || (currentTarget != null && !currentTarget.gameObject.activeInHierarchy))
        {
             // AcquireTarget hem BattleManager'a hem de Fiziksel (OverlapSphere) aramaya bakar.
             // HÜCUM MODUNDA: Menzil Sınırsız Olsun (Global Attack)
             currentTarget = AcquireTarget(float.MaxValue);
        }
        
        // DEBUG: Hedef Durumu
        if (currentTarget == null)
        {
            // Hedef Bulunamadı -> Dur
            if(agent.isOnNavMesh) agent.isStopped = true;
            
            // Sık log atmamak için 60 frame'de bir uyar
            if(Time.frameCount % 60 == 0) Debug.LogWarning($"[Unit {netId}] Charge Modunda ama HEDEF BULAMIYOR! (BattleManager döndürmedi)");
        }
        else
        {
            // Hedefe Git
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            float range = attacker.GetRange();

            if (dist <= range)
            {
                // Vur
                if(agent.isOnNavMesh) agent.isStopped = true;
                attacker.TryAttack(currentTarget);
            }
            else
            {
                // Yaklaş
                if(agent.isOnNavMesh) 
                {
                    agent.isStopped = false;
                    agent.SetDestination(currentTarget.position);
                }
            }
        }
        
        // DEBUG: NavMesh durumunu kontrol et
        if(!agent.isOnNavMesh && Time.frameCount % 60 == 0) Debug.LogWarning($"[Unit {netId}] NavMesh üzerinde değil!");
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

    // --- SWARMING LOGIC ---
    private static Dictionary<Transform, int> activeEngagements = new Dictionary<Transform, int>();

    private void RegisterEngagement(Transform target)
    {
        if (target == null) return;
        if (!activeEngagements.ContainsKey(target)) activeEngagements[target] = 0;
        activeEngagements[target]++;
    }

    private void UnregisterEngagement(Transform target)
    {
        if (target == null) return;
        if (activeEngagements.ContainsKey(target))
        {
            activeEngagements[target]--;
            if (activeEngagements[target] <= 0) activeEngagements.Remove(target);
        }
    }

    private bool IsTargetOverwhelmed(Transform target)
    {
        if (target == null) return false;
        return activeEngagements.ContainsKey(target) && activeEngagements[target] >= 3;
    }

    private void HandleMoving()
    {
        // Debug.Log($"[Unit {netId}] HandleMoving - isAttackMoving:{isAttackMoving}, guardPos:{guardPosition}, State:{currentState}");
        
        // ATTACK MOVE KONTROLÜ
        if (isAttackMoving)
        {
            // HIZLANDIR (Charge Effect - 1.2x)
            if(agent.isOnNavMesh) agent.speed = moveSpeed * 1.2f; 

            // Filtereli Arama: Üzerinde 3 kişiden az olan EN YAKIN düşmanı bul (5m Menzil - User Request: Sadece yakındakine dal)
            // Eğer hepsi doluysa null döner -> Koşmaya devam ederiz.
            Transform found = AcquireTarget(5f, t => !IsTargetOverwhelmed(t)); 

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
            currentState = UnitState.Guarding; // Nöbete başla
            isAttackMoving = false; // Vardığımız için Attack Move bitti
            
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
                // Menzildeyse listeye ekle (En yakın olması şart değil)
                // Ama çok uzaktakileri de almayalım (Range check zaten OverlapSphere ile yapıldı)
                rangedCandidates.Add(candidate);
            }
            else
            {
                // Melee: En yakını bul
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
        UnregisterEngagement(currentTarget);

        Health health = GetComponent<Health>();
        if (health != null) health.OnDeath -= OnDeathHandler;

        // Kayıt Silme
        if (BattleManager.Instance != null) BattleManager.Instance.UnregisterPlayerUnit(this);
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
            // "Die" ve "Death" ikisini de dene (Farklı modellerde farklı isimler olabilir)
            anim.CrossFadeInFixedTime("Die", 0.1f); 
        }
        
        if(agent != null) agent.enabled = false;
        
        Collider col = GetComponent<Collider>();
        if(col != null) col.enabled = false; 

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
        // Cesedi yere yapıştır (Havada kalmasın)
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 5f, groundLayer))
        {
            transform.position = hit.point;
            // Eğim varsa eğime göre de yatırılabilir ama şimdilik pozisyon yeterli
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

    private void UpdateAnimations()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            float speed = agent.velocity.magnitude;
            Animator anim = GetComponent<Animator>();
            if (anim != null) anim.SetFloat("Speed", speed);
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
