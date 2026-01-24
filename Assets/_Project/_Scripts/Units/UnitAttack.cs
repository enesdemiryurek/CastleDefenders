using Mirror;
using UnityEngine;
using UnityEngine.AI;

public class UnitAttack : NetworkBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private int damage = 15;
    [SerializeField] private float attackCooldown = 1.0f;
    [SerializeField] private LayerMask enemyLayer;

    private NavMeshAgent agent;
    private float lastAttackTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void Update()
    {
        if (!NetworkServer.active) return;

        TryAttackNearestEnemy();
    }

    private void TryAttackNearestEnemy()
    {
        // 1. Etraftaki Düşmanları Ara
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, enemyLayer);
        
        foreach (var hit in hits)
        {
            IDamageable target = hit.GetComponent<IDamageable>();
            
            // Eğer Canlı ve Düşmansa
            if (target != null && hit.GetComponent<EnemyAI>() != null)
            {
                Attack(target);
                return; // En yakındakine vurup döngüden çık (Hepsine aynı anda vurmasın)
            }
        }
    }

    public event System.Action OnAttack;

    private void Attack(IDamageable target)
    {
        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        
        // 1. Hedefe Dön (LookAt)
        // Hedefin pozisyonunu al ama Y eksenini sabitle (yukarı bakmasın)
        if (target is MonoBehaviour targetMono)
        {
            Vector3 lookPos = targetMono.transform.position;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);
        }

        // 2. Durdur (Kayarak vurmasın)
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            Invoke(nameof(ResumeMovement), 0.75f); // Animasyon bitince yürüsün (tahmini 0.75sn)
        }

        // 3. Hasar Ver
        target.TakeDamage(damage);

        // 4. Event Tetikle (Animasyon için)
        OnAttack?.Invoke();
    }

    private void ResumeMovement()
    {
        if (agent != null && agent.enabled)
        {
            agent.isStopped = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
