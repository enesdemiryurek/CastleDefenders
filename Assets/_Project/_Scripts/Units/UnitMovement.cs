using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(UnitAttack))] // "Kas" sistemi zorunlu
public class UnitMovement : NetworkBehaviour
{
    public enum UnitState { Idle, Guarding, Chasing, Charging, Moving }

    [Header("State Info")]
    [SyncVar] public UnitState currentState = UnitState.Idle;
    [SyncVar] public int SquadIndex;

    [Header("Settings")]
    [SerializeField] private float guardRange = 10f; // Savunmadayken ne kadar uzağa baksın?
    [SerializeField] private float updateInterval = 0.25f; // Saniyede 4 kez karar ver

    [Header("Targeting")]
    [SerializeField] private LayerMask enemyLayerMask = -1; 
    [SerializeField] private float chargeAggroRange = 500f; 
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

    private Coroutine chargeRoutine;
    private bool isCharging;
    private bool chargingWindupActive;

    private float lastDecisionTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        attacker = GetComponent<UnitAttack>();

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
    public void MoveTo(Vector3 position, Quaternion? lookRotation = null, bool shieldWall = false)
    {
        // Debug.Log($"[Server] Unit Moving To: {position}");
        currentState = UnitState.Moving; 
        guardPosition = position;
        guardRotation = lookRotation; // Rotasyonu kaydet
        currentTarget = null; 
        
        TrySetDestination(position);
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

    private void OnDestroy()
    {
        // Kayıt Silme
        if (BattleManager.Instance != null) BattleManager.Instance.UnregisterPlayerUnit(this);
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
        }
    }

    private void HandleGuarding()
    {
        // Eğer zaten bir hedefimiz varsa, menzilden çıktı mı kontrol et
        if (currentTarget != null)
        {
            float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
            
            // Eğer hedef çok uzaklaştıysa veya öldüyse bırak
            if (distToTarget > guardRange || !currentTarget.gameObject.activeInHierarchy)
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
        Transform found = AcquireTarget(guardRange); // Geniş ara, ama EngageTarget gitmeyecek.
        
        if (found != null)
        {
            currentTarget = found;
            EngageTarget(currentTarget);
        }
        else
        {
            // Hedef yoksa, pozisyonun biraz kaydıysa düzelt (Micro-correction)
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

    private void HandleMoving()
    {
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
            
            // Hemen dönmeye başla (Manuel Rotation)
            if (guardRotation.HasValue) 
            {
                transform.rotation = guardRotation.Value; // Anlık düzeltme (veya Lerp ile yapılabilir)
            }
        }
        else
        {
            // Yürümeye devam
            TrySetDestination(guardPosition.Value);
        }
    }

    private Transform AcquireTarget(float range)
    {
        Transform bestTarget = null;
        float bestDistance = range;

        if (BattleManager.Instance != null)
        {
            Transform bmTarget = BattleManager.Instance.GetNearestEnemyForUnit(transform.position, range);
            if (bmTarget != null)
            {
                bestTarget = bmTarget;
                bestDistance = Vector3.Distance(transform.position, bmTarget.position);
            }
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, range, enemyLayerMask);
        EvaluateHitResults(hits, ref bestTarget, ref bestDistance);

        // Eğer maskeden sonuç yoksa, son çare tüm layer'larda ara (dostları yine filtreleyecek)
        if (bestTarget == null)
        {
            Collider[] anyHits = Physics.OverlapSphere(transform.position, range, ~0);
            EvaluateHitResults(anyHits, ref bestTarget, ref bestDistance);
        }

        return bestTarget;
    }

    private void EvaluateHitResults(Collider[] hits, ref Transform bestTarget, ref float bestDistance)
    {
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

            float dist = Vector3.Distance(transform.position, candidate.position);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestTarget = candidate;
            }
        }
    }

    private void EngageTarget(Transform target)
    {
        float dist = Vector3.Distance(transform.position, target.position);
        float attackRange = 2.0f; // UnitAttack'tan çekilebilir aslında

        if (dist <= attackRange)
        {
            // Vur!
            StopMovement();
            attacker.Attack(target.GetComponent<IDamageable>());
        }
        else
        {
            // Yaklaş
            // STAND YOUR GROUND: Eğer Guarding modundaysak ASLA hareket etme!
            if (currentState != UnitState.Guarding)
            {
                MoveToTarget(target);
            }
            else
            {
                 // Eğer Guarding ise ve menzilde değilse, sadece dönüp bakabilir (Opsiyonel)
                 // transform.LookAt(target); // Şimdilik kapalı, formasyonu bozmasın
                 StopMovement();
            }
        }
    }

    private void MoveToTarget(Transform target)
    {
        TrySetDestination(target.position);
    }

    private void StopMovement()
    {
        if(agent.isOnNavMesh)
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
    private void TrySetDestination(Vector3 targetPos)
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
