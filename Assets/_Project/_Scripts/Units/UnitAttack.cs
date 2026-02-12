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
    [SerializeField] private float rangedAttackRange = 50.0f; // Okçular için uzun menzil (User Request: 2 katına çıkarıldı)
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;

    private float lastAttackTime;
    
    public float GetRange() => isRanged ? rangedAttackRange : attackRange;
    public bool IsRanged => isRanged; // Public Accessor
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
    public void FireVolley(Vector3 targetPoint)
    {
        if (!CanAttack()) return;
        if (!isRanged) return; // Sadece okçular yağdırabilir

        lastAttackTime = Time.time;

        // 1. Dön
        Vector3 lookPos = targetPoint;
        lookPos.y = transform.position.y;
        transform.LookAt(lookPos);

        // 2. Animasyon
        TriggerAttackAnimation();

        // 3. Ateş (Alan Hedefli)
        StartCoroutine(SpawnVolleyProjectileDelayed(targetPoint, 0.4f));
    }

    [Server]
    public void Attack(IDamageable target)
    {
        if (!CanAttack()) return;
        lastAttackTime = Time.time;
        // ... (rest of Attack method)
        // 1. Durdur & Dön (Hareket varsa)
        if (target is MonoBehaviour targetMono)
        {
            // Yüzünü hedefe dön
            Vector3 lookPos = targetMono.transform.position;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);
        }

        // 2. Event Tetikle (Animasyon başlasın)
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
                // ... (Instantiate & Launch logic)
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
                    bp.SetShooter(gameObject); 
                    bp.Launch(targetPos, 2.0f); // Normal atış (Düşük kavis)
                }
            }
        }
    }

    private System.Collections.IEnumerator SpawnVolleyProjectileDelayed(Vector3 targetPos, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (projectilePrefab != null && projectileSpawnPoint != null)
        {
            GameObject proj = Instantiate(projectilePrefab, projectileSpawnPoint.position, projectileSpawnPoint.rotation);
            NetworkServer.Spawn(proj);

            // Volley Spread (Daha geniş dağılım - Yağmur etkisi)
            // User Request: "Alanı büyült"
            float spreadRadius = 6.0f; 
            Vector3 spread = Random.insideUnitSphere * spreadRadius;
            spread.y = 0; 
            Vector3 finalPos = targetPos + spread;

            BallisticProjectile bp = proj.GetComponent<BallisticProjectile>();
            if (bp != null)
            {
                bp.SetShooter(gameObject); 
                // User Request: "Daha bombeli atsınlar" -> 8m yükseklik
                bp.Launch(finalPos, 8.0f);
            }
        }
    }
}
