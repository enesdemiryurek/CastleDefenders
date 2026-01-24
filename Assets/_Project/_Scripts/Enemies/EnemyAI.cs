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

    [Header("Visuals")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;
    [SerializeField] private string[] attackTriggers = { "Attack" }; // Birden fazla saldırı animasyonu için

    private NavMeshAgent agent;
    private Transform currentTarget;
    private float lastUpdateTime;
    private float lastAttackTime;

    // ... (Awake and other methods remain same) ...

    private void AttackTarget()
    {
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

        // 4. Hedefe hasar ver
        IDamageable damageable = currentTarget.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damage);
            OnAttack?.Invoke();
        }
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();
    }

    public override void OnStartServer()
    {
        agent.enabled = true;
    }

    private void Update()
    {
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

    private void FindBestTarget()
    {
        // 1. Etraftaki "Canlıları" ara (Unit veya Player)
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, aggroRange, targetLayers);
        float closestDistance = float.MaxValue;
        Transform bestCandidate = null;

        foreach (var hit in hitColliders)
        {
            // Kendimize kitlememek için kontrol
            if (hit.transform == transform) continue;

            // ÖNEMLİ: Sadece 'Canlı' olanları hedef al (Player veya Unit)
            // Eğer Yeri (Ground) veya Duvarı hedef alırsak Enemy olduğu yerde kalır.
            if (hit.GetComponent<PlayerController>() == null && hit.GetComponent<UnitMovement>() == null)
            {
                continue;
            }

            float dist = Vector3.Distance(transform.position, hit.transform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                bestCandidate = hit.transform;
            }
        }

        // 2. Eğer yakında kimse yoksa hedef-> TAHT
        if (bestCandidate != null)
        {
            currentTarget = bestCandidate;
        }
        else
        {
            if (Throne.Instance != null)
                currentTarget = Throne.Instance.transform;
        }
    }

    private void MoveToTarget()
    {
        if (currentTarget == null) return;

        // NavMesh üzerinde hareket et
        if (agent.isOnNavMesh)
        {
            agent.SetDestination(currentTarget.position);
            
            // Saldırı Mesafesinde miyiz?
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            if (dist <= attackRange)
            {
                agent.isStopped = true;
                AttackTarget();
            }
            else
            {
                agent.isStopped = false;
            }
        }
    }

    public event System.Action OnAttack;



    private void ResumeMovement()
    {
        if (agent != null && agent.enabled)
        {
            agent.isStopped = false;
        }
    }

    // Gizmos ile Aggro alanını çiz (Debug için)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, aggroRange);
    }
}
