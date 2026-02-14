using UnityEngine;
using UnityEngine.AI;
using Mirror;

/// <summary>
/// Düşman devriye sistemi - Waypoint'ler arasında dolaşır
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyPatrol : NetworkBehaviour
{
    [Header("Patrol Settings")]
    [SerializeField] private Transform[] waypoints; // Devriye noktaları
    [SerializeField] private float waypointReachDistance = 1f;
    [SerializeField] private float waitTimeAtWaypoint = 2f;
    [SerializeField] private bool randomOrder = false;
    [SerializeField] private bool pingPongMode = true; // Geri dön (1→2→3→4→3→2→1)

    [Header("Patrol Zone (Optional)")]
    [SerializeField] private float patrolZoneRadius = 20f; // Devriye bölgesi yarıçapı
    [SerializeField] private bool restrictToPatrolZone = true; // Sadece bölge içinde saldır

    private NavMeshAgent agent;
    private Animator animator;
    private EnemyAI enemyAI;
    private int currentWaypointIndex = 0;
    private int direction = 1; // 1 = ileri, -1 = geri
    private float waitTimer = 0f;
    private bool isWaiting = false;
    private Vector3 patrolCenter; // Devriye merkezini sakla
    private float originalAggroRange; // Orijinal aggro range'i sakla

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        enemyAI = GetComponent<EnemyAI>();

        // Devriye merkezini hesapla (waypoint'lerin ortası)
        if (waypoints.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var wp in waypoints)
            {
                if (wp != null) sum += wp.position;
            }
            patrolCenter = sum / waypoints.Length;
        }
        else
        {
            patrolCenter = transform.position;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        // EnemyAI'ı devre dışı bırak (Devriye sırasında saldırmasın)
        if (enemyAI != null && restrictToPatrolZone)
        {
            enemyAI.enabled = false;
        }

        if (waypoints.Length > 0)
        {
            GoToNextWaypoint();
        }
    }

    [Server]
    private void Update()
    {
        if (waypoints.Length == 0) return;

        // Oyuncu devriye bölgesine girdi mi kontrol et
        if (restrictToPatrolZone && enemyAI != null && !enemyAI.enabled)
        {
            CheckPlayerInPatrolZone();
        }

        // Bekleme durumu
        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                isWaiting = false;
                GoToNextWaypoint();
            }
            return;
        }

        // Waypoint'e ulaştı mı?
        if (!agent.pathPending && agent.remainingDistance <= waypointReachDistance)
        {
            // Bekle
            isWaiting = true;
            waitTimer = waitTimeAtWaypoint;

            // Animasyon: Dur
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }
        }
        else
        {
            // Yürüyor - Animasyon hızını ayarla
            if (animator != null && agent != null)
            {
                float speed = agent.velocity.magnitude;
                animator.SetFloat("Speed", speed);
            }
        }
    }

    [Server]
    private void CheckPlayerInPatrolZone()
    {
        // Oyuncuları bul
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        
        foreach (var player in players)
        {
            float distance = Vector3.Distance(player.transform.position, patrolCenter);
            
            if (distance <= patrolZoneRadius)
            {
                // Oyuncu bölgeye girdi! EnemyAI'ı aktif et
                if (enemyAI != null)
                {
                    enemyAI.enabled = true;
                    this.enabled = false; // Devriyeyi durdur
                    Debug.Log($"[EnemyPatrol] Player entered patrol zone! Activating combat AI.");
                }
                break;
            }
        }
    }

    [Server]
    private void GoToNextWaypoint()
    {
        if (waypoints.Length == 0) return;

        // Rastgele mod
        if (randomOrder)
        {
            currentWaypointIndex = Random.Range(0, waypoints.Length);
        }
        // Ping-pong mod (1→2→3→4→3→2→1)
        else if (pingPongMode)
        {
            currentWaypointIndex += direction;

            // Sona ulaştı, geri dön
            if (currentWaypointIndex >= waypoints.Length - 1)
            {
                currentWaypointIndex = waypoints.Length - 1;
                direction = -1; // Geri git
            }
            // Başa ulaştı, ileri git
            else if (currentWaypointIndex <= 0)
            {
                currentWaypointIndex = 0;
                direction = 1; // İleri git
            }
        }
        // Normal loop mod (1→2→3→4→1)
        else
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }

        agent.SetDestination(waypoints[currentWaypointIndex].position);
    }

    // Debug için (Scene view'da devriye bölgesini göster)
    private void OnDrawGizmosSelected()
    {
        if (restrictToPatrolZone)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(patrolCenter, patrolZoneRadius);
        }
    }
}
