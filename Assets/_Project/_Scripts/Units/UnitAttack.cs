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
    [Header("Ranged Settings")]
    [SerializeField] private bool isRanged = false;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;

    private NavMeshAgent agent;
    private float lastAttackTime;
    private bool isDead = false;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += () => 
            {
                 isDead = true; 
                 StopAllCoroutines(); // Saldırıları durdur
                 CancelInvoke();
            };
        }
    }

    private void Update()
    {
        if (isDead) return;
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
                return; // En yakındakine vurup döngüden çık
            }
        }
    }

    public event System.Action OnAttack;

    private void Attack(IDamageable target)
    {
        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        
        // 1. Hedefe Dön (LookAt)
        if (target is MonoBehaviour targetMono)
        {
            Vector3 lookPos = targetMono.transform.position;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);
        }

        // 2. Durdur (Animasyon bitene kadar)
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            // Menzilli ise biraz daha hızlı toparlayabilir veya animasyona göre ayarlanır
            Invoke(nameof(ResumeMovement), 0.75f); 
        }

        // 3. Event Tetikle (Animasyon başlasın)
        OnAttack?.Invoke();

        // 4. Saldırı Türüne Göre İşlem (GECİKMELİ)
        // Animasyonun "Shoot" noktasına gelmesi için azıcık bekleyip oku fırlatalım
        if (isRanged)
        {
            StartCoroutine(SpawnProjectileDelayed(target, 0.4f)); 
        }
        else
        {
            // Melee için direkt hasar (veya buraya da gecikme eklenebilir)
            target.TakeDamage(damage, transform.position);
        }
    }

    private System.Collections.IEnumerator SpawnProjectileDelayed(IDamageable target, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (target != null && projectilePrefab != null && projectileSpawnPoint != null)
        {
            // Hedef hala yaşıyor mu kontrol et (MonoBehaviour ise)
            if (target is MonoBehaviour targetMono && targetMono != null)
            {
                GameObject proj = Instantiate(projectilePrefab, projectileSpawnPoint.position, projectileSpawnPoint.rotation);
                NetworkServer.Spawn(proj);

                // Hedef Pozisyonu + Sapma (Realizm)
                Vector3 targetPos = targetMono.transform.position;
                
                 // Hafif rastgelelik ekle (Tam 12'den vurmasınlar)
                float accuracy = 1.0f; // Sapma miktarı (metre)
                Vector3 spread = Random.insideUnitSphere * accuracy;
                spread.y = 0; // Yüksekliği çok bozma
                targetPos += spread;

                // Projectile'i Fırlat
                BallisticProjectile bp = proj.GetComponent<BallisticProjectile>();
                if (bp != null)
                {
                    bp.Launch(targetPos);
                }
            }
        }
    }

    private void ResumeMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isRanged ? Color.cyan : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
