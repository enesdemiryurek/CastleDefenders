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

    private void Update()
    {
        // 1. Görsel Hizalama (Client & Server)
        AlignModelToGround();

        // 2. Sunucu Mantığı (Hareket & Charge)
        if (!isServer) return;

        if (isDead) return;
        if (!isCharging) return;
        
        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        Transform target = FindNearestEnemy();
        if (target != null)
        {
            if (agent.isOnNavMesh)
            {
                agent.SetDestination(target.position);
            }
        }
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

    [Server] // Bu fonksiyon sadece Server'da çalıştırılabilir
    public void MoveTo(Vector3 targetPosition)
    {
        // Hareket emri gelince charge biter
        StopCharging();

        if (agent.isOnNavMesh)
        {
            agent.SetDestination(targetPosition);
            agent.isStopped = false;
        }
        else
        {
            Debug.LogWarning($"Unit {name} is NOT on NavMesh!");
        }
    }
}
