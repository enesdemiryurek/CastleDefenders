using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class UnitMovement : NetworkBehaviour
{
    [Header("Charge Settings")]
    [SerializeField] private float detectionRadius = 20f;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private float updateInterval = 0.5f;

    private NavMeshAgent agent;
    private bool isCharging = false;
    private float lastUpdateTime;

    [Header("Visual Settings")]
    [SerializeField] private float terrainHeightCorrection = 0f; // Bake ile çözüldü, default 0
    [SerializeField] private Transform modelTransform; // Görsel modelin (Child) referansı
    [SerializeField] private LayerMask groundLayer = -1; // Default: Everything
    [SerializeField] private float alignmentSpeed = 10f;

    [SyncVar] public int SquadIndex;

    private bool isDead = false;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        // agent.baseOffset atamasını kaldırdık çünkü NavMesh Bake işlemiyle çözüldü.
        // Artık script manuel olarak yüksekliğe müdahale etmeyecek.

        // Eğer model atanmamışsa otomatik bul (Animator'un olduğu obje)
        if (modelTransform == null)
        {
            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.transform != transform)
            {
                modelTransform = anim.transform;
            }
        }
        
        // Health eventine abone ol
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += OnDeathHandler;
        }

        // Kök Hareketi (Root Motion) Sorununu Çöz
        // Bazı animasyonlar karakteri ileri götürür, bunu NavMeshAgent ile çakışmaması için kapatıyoruz.
        Animator rootAnim = GetComponent<Animator>();
        if (rootAnim == null) rootAnim = GetComponentInChildren<Animator>();
        if (rootAnim != null) 
        {
            rootAnim.applyRootMotion = false;
        }
    }

    private void LateUpdate()
    {
        // Modelin "Logic" objesinden uzaklaşmasını engelle (Drift Fix)
        if (modelTransform != null && !isDead)
        {
            Vector3 currentLocal = modelTransform.localPosition;
            // Sadece Y yüksekliğini koru (varsa), X ve Z her zaman 0 olsun (Tam ortada)
            if (Mathf.Abs(currentLocal.x) > 0.05f || Mathf.Abs(currentLocal.z) > 0.05f)
            {
                modelTransform.localPosition = new Vector3(0, currentLocal.y, 0);
            }
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
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
            agent.enabled = false; // NavMesh'ten kopar
        }

        // Animasyon Tetikle
        Animator anim = GetComponent<Animator>();
        if (anim != null) anim.SetTrigger("Die");
        
        // Network Animator varsa onu da tetikle (Senkronizasyon için)
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger("Die");
    }

    public override void OnStartServer()
    {
        // NavMeshAgent sadece Server'da çalışmalı
        agent.enabled = true;
        
        // Askerler hedefe tam sıfır noktasına gitmeye çalışırken titremesin/dönmesin
        agent.stoppingDistance = 1.5f; 
        agent.autoBraking = true;
    }



    private void AlignModelToGround()
    {
        if (modelTransform == null) return;

        // Player'ın biraz yukarısından aşağı ray at
        Ray ray = new Ray(transform.position + Vector3.up, Vector3.down);
        
        // RaycastAll kullanarak kendimize çarpma durumunu engelliyoruz
        RaycastHit[] hits = Physics.RaycastAll(ray, 3f, groundLayer);
        
        RaycastHit bestHit = new RaycastHit();
        bool found = false;
        float minDst = float.MaxValue;

        foreach (var hit in hits)
        {
            // Kendimize veya alt objelerimize çarpıyorsak yoksay
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            
            // Trigger'ları yoksay (Opsiyonel ama güvenli)
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
            // Zeminin normaline göre hedef rotasyon
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, bestHit.normal) * transform.rotation;
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRotation, Time.deltaTime * alignmentSpeed);
        }
        else
        {
            // Zemin bulunamazsa varsayılan dik duruşa yavaşça dön
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, transform.rotation, Time.deltaTime * alignmentSpeed);
        }
    }

    private Transform FindNearestEnemy()
    {
        // "Herhangi bir yerdeki" düşmanı bulmak için tüm sahneyi tarıyoruz
        // Upgrade: Unity 6+ için FindObjectsByType kullanımı
        EnemyAI[] allEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        
        Transform bestTarget = null;
        float closestDist = float.MaxValue;

        foreach (var enemy in allEnemies)
        {
            float d = Vector3.Distance(transform.position, enemy.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = enemy.transform;
            }
        }
        return bestTarget;
    }

    [Server]
    public void StartCharging()
    {
        isCharging = true;
        
        if (agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    [Server]
    public void StopCharging()
    {
        isCharging = false;
        // Hareketi hemen durdurmak istemeyebiliriz (MoveTo çağrılacak), ama clean slate olsun.
    }

    private Quaternion? targetFacingRotation = null;
    private bool isShieldWall = false;

    // ... (Previous Update Logic) ...

    private void Update()
    {
        // 1. Görsel Hizalama (Client & Server)
        AlignModelToGround();
        
        // 2. Sunucu Mantığı (Hareket & Charge)
        if (!isServer) return;

        if (isDead) return;

        // Formasyon Yönüne Dönme (Duruyorsa veya hedefe çok yakınsa)
        if (targetFacingRotation.HasValue && !isCharging)
        {
            float dist = agent.remainingDistance;
            
            // Hedefe çok yakınsak kontrolü biz alalım (NavMesh kendi kafasına göre dönmesin)
            if (dist <= agent.stoppingDistance + 0.5f)
            {
                agent.updateRotation = false; // NavMesh rotasyonu kapat
                float step = rotationSpeed * Time.deltaTime * 50f; // Hızlı dön
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetFacingRotation.Value, step);
            }
            else
            {
                agent.updateRotation = true; // Yürürken serbest bırak
            }
        }
        else
        {
             if (agent != null) agent.updateRotation = true;
        }

        // Animasyon Hızı Güncelle (Idle'a geçmesi için)
        if (agent != null && agent.isOnNavMesh)
        {
            float speed = agent.velocity.magnitude;

            // Eğer hedefe çok yaklaştıysak (Toleranslı) veya hızı çok düşükse IDLE yap
            // stoppingDistance (0.1f) + 0.5f = 0.6f tolerans (Kalabalıkta itiş kakışı önlemek için)
            if ((agent.remainingDistance <= agent.stoppingDistance + 0.5f) && !agent.pathPending)
            {
                speed = 0f;
                // Fiziksel itiş kakışı da durdur ki titremesinler
                if (!isCharging) agent.isStopped = true; 
            }
            // Çok düşük hızları da (sürtünme) yoksay
            else if (speed < 0.1f)
            {
                speed = 0f;
            }

            Animator anim = GetComponent<Animator>();
            if (anim != null) anim.SetFloat("Speed", speed);
        }
    }

    [Server]
    public void MoveTo(Vector3 targetPosition, Quaternion? lookRotation = null, bool shieldWall = false)
    {
        // Hareket emri gelince charge biter
        StopCharging();
        
        targetFacingRotation = lookRotation;
        isShieldWall = shieldWall;

        // Animator güncelle
        // Animator anim = GetComponent<Animator>();
        // if (anim != null) anim.SetBool("ShieldWall", isShieldWall);
        // User: Animasyon eklemeyelim, Idle zaten kalkanlı duruyor.

        
        // Network Animator
        // Network Animator
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        // Standart Mirror NetworkAnimator'ın "Animator" diye bir property'si yoktur. 
        // Animator üzerindeki değişiklikleri zaten otomatik algılar (parametre listesindeyse).


        if (agent.isOnNavMesh)
        {
            agent.stoppingDistance = 0.1f; // Formasyon için tam noktaya gitmeli
            agent.SetDestination(targetPosition);
            agent.isStopped = false;
        }
        else
        {
            Debug.LogWarning($"Unit {name} is NOT on NavMesh!");
        }
    }

    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 5f;
}
