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

    [Header("Ranged Settings")]
    [SerializeField] private bool isRanged = false;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;

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

        // 4. Saldırı (Melee vs Ranged)
        if (isRanged)
        {
            StartCoroutine(SpawnProjectileDelayed(0.4f)); 
        }
        else
        {
            // Hedefe hasar ver (Melee)
            IDamageable damageable = currentTarget.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage);
                OnAttack?.Invoke();
            }
        }
    }

    private System.Collections.IEnumerator SpawnProjectileDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (projectilePrefab != null && projectileSpawnPoint != null)
        {
            // Oku SpawnPoint'in açısıyla fırlat (Böylece kullanıcı düzeltebilir)
            GameObject proj = Instantiate(projectilePrefab, projectileSpawnPoint.position, projectileSpawnPoint.rotation);
            NetworkServer.Spawn(proj);
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
        // 0. Mevcut hedef hala geçerli mi? (Sticky Targeting)
        // Eğer zaten bir hedefim varsa ve hala canlıysa/menzildeyse değiştirme.
        // Bu sayede düşmanlar "kararsız" gibi titremez, birine kilitlenir.
        if (currentTarget != null)
        {
            IDamageable currentHp = currentTarget.GetComponent<IDamageable>();
            float distToCurrent = Vector3.Distance(transform.position, currentTarget.position);
            
            // Hedef hala canlıysa ve aşırı uzaklaşmadıysa (Aggro * 1.5) devam et
            if (currentHp != null && currentHp.CurrentHealth > 0 && distToCurrent < aggroRange * 1.5f)
            {
                return;
            }
        }

        // 1. Etraftaki "Canlıları" ara (Unit veya Player)
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, aggroRange, targetLayers);
        
        System.Collections.Generic.List<Transform> validTargets = new System.Collections.Generic.List<Transform>();

        foreach (var hit in hitColliders)
        {
            if (hit.transform == transform) continue;

            // Unit veya Player kontrolü
            if (hit.GetComponentInParent<PlayerController>() != null || hit.GetComponentInParent<UnitMovement>() != null)
            {
                validTargets.Add(hit.transform);
            }
        }

        // 2. Hedef Seçimi (Rastgelelik ekle ki hepsi tek kişiye dalmasın)
        if (validTargets.Count > 0)
        {
            // En yakındakini değil, menzildeki rastgele birini seç
            currentTarget = validTargets[Random.Range(0, validTargets.Count)];
        }
        else
        {
            // Kimse yoksa hedef-> TAHT
            if (Throne.Instance != null)
                currentTarget = Throne.Instance.transform;
            else
                currentTarget = null;
        }
    }

    private void MoveToTarget()
    {
        if (currentTarget == null) return;

        if (!agent.isOnNavMesh) return;

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        
        // --- RANGED MANTIK (Bannerlord Tarzı) ---
        if (isRanged)
        {
            // Eğer saldırı menzilindeysek DUR ve SIK
            if (dist <= attackRange)
            {
                agent.isStopped = true;
                
                // Hedefe Dön (LookAt) - Sadece Y ekseninde
                Vector3 lookPos = currentTarget.position;
                lookPos.y = transform.position.y;
                transform.LookAt(lookPos);
                
                AttackTarget();
            }
            else
            {
                // Menzile girene kadar koş
                agent.isStopped = false;
                agent.SetDestination(currentTarget.position);
            }
        }
        // --- MELEE MANTIK (Klasik) ---
        else
        {
            agent.SetDestination(currentTarget.position);

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
        Gizmos.color = isRanged ? Color.cyan : Color.red;
        Gizmos.DrawWireSphere(transform.position, aggroRange);
    }
}
