using Mirror;
using UnityEngine;
using UnityEngine.AI;

public class UnitAttack : NetworkBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private int damage = 15;
    [Header("Settings")]
    [SerializeField] private float attackRange = 4.0f; // Menzil Artırıldı (Eski: 2.0f)
    [SerializeField] private float attackCooldown = 1.5f;
    
    [Header("Ranged Settings")]
    [SerializeField] private bool isRanged = false;
    [SerializeField] private float rangedAttackRange = 25.0f; // Okçular için uzun menzil
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;

    private float lastAttackTime;
    
    public float GetRange() => isRanged ? rangedAttackRange : attackRange;
    public void TryAttack(Transform target) => Attack(target.GetComponent<IDamageable>());
    
    // Logic Component Reference (The Brain)
    private UnitMovement movement;

    private void Awake()
    {
        movement = GetComponent<UnitMovement>();
    }

    public bool CanAttack()
    {
        return Time.time - lastAttackTime >= attackCooldown;
    }

    [Server]
    public void Attack(IDamageable target)
    {
        if (!CanAttack()) return;
        lastAttackTime = Time.time;

        // 1. Durdur & Dön (Hareket varsa)
        if (target is MonoBehaviour targetMono)
        {
            // Yüzünü hedefe dön
            Vector3 lookPos = targetMono.transform.position;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);
        }

        // 2. Event Tetikle (Animasyon başlasın)
        // UnitMovement üzerinden senkronize etmek daha doğrudur ama 
        // burası sadece execution olduğu için NetworkAnimator kullanabiliriz.
        TriggerAttackAnimation();

        // 3. Hasar veya Mermi (Gecikmeli)
        if (isRanged)
        {
            StartCoroutine(SpawnProjectileDelayed(target, 0.4f)); 
        }
        else
        {
            // Melee: Direkt hasar ama animasyonun "Vurma" anına denk gelmeli (0.5s gibi)
            StartCoroutine(DealMeleeDamageDelayed(target, 0.5f));
        }
    }

    private void TriggerAttackAnimation()
    {
        TriggerAnimation("Attack");

        // Event Tetikle (AnimationController için)
        OnAttack?.Invoke();
    }

    // Genel amaçlı animasyon tetikleyicisi (NetworkAnimator varsa onu tercih eder)
    public void TriggerAnimation(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName)) return;

        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null) netAnim.SetTrigger(triggerName);
        else
        {
            Animator anim = GetComponent<Animator>();
            if (anim != null) anim.SetTrigger(triggerName);
        }
    }
    
    // AnimationController'ın dinlemesi için event
    public event System.Action OnAttack;

    private System.Collections.IEnumerator DealMeleeDamageDelayed(IDamageable target, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (target != null && target is MonoBehaviour targetMono && targetMono != null) // Hala hayatta mı?
        {
            // Mesafe kontrolü (Vururken kaçtı mı?)
            if (Vector3.Distance(transform.position, targetMono.transform.position) <= 3.5f)
            {
                target.TakeDamage(damage, transform.position);
            }
        }
    }

    private System.Collections.IEnumerator SpawnProjectileDelayed(IDamageable target, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (target != null && projectilePrefab != null && projectileSpawnPoint != null)
        {
            if (target is MonoBehaviour targetMono && targetMono != null)
            {
                GameObject proj = Instantiate(projectilePrefab, projectileSpawnPoint.position, projectileSpawnPoint.rotation);
                NetworkServer.Spawn(proj);

                Vector3 targetPos = targetMono.transform.position;
                float accuracy = 1.0f; 
                Vector3 spread = Random.insideUnitSphere * accuracy;
                spread.y = 0; 
                targetPos += spread;

                BallisticProjectile bp = proj.GetComponent<BallisticProjectile>();
                if (bp != null)
                {
                    bp.SetShooter(gameObject); // Vuran biziz
                    bp.Launch(targetPos);
                }
            }
        }
    }
}
