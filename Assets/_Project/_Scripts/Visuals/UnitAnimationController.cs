using Mirror;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NetworkAnimator))] // Mirror'ın animasyon senkronizasyon component'i
public class UnitAnimationController : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Health health;
    [SerializeField] private UnitAttack unitAttack;
    [SerializeField] private EnemyAI enemyAI;

    [Header("Animator Parameters")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string[] attackParams = { "Attack" };
    [SerializeField] private string dieParam = "Die";

    private Animator animator;
    private NetworkAnimator networkAnimator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        networkAnimator = GetComponent<NetworkAnimator>();

        if (health == null) health = GetComponent<Health>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        
        // UnitAttack veya EnemyAI hangisi varsa onu al
        if (unitAttack == null) unitAttack = GetComponent<UnitAttack>();
        if (enemyAI == null) enemyAI = GetComponent<EnemyAI>();
    }

    private void RegisterEvents()
    {
        if (eventsRegistered) return;
        eventsRegistered = true;

        if (health != null)
        {
            health.OnDeath += HandleDeath;
            health.EventHealthChanged += HandleHealthChanged;
        }
        if (unitAttack != null) unitAttack.OnAttack += HandleAttack;
        if (enemyAI != null) enemyAI.OnAttack += HandleAttack;
    }

    private void UnregisterEvents()
    {
        if (!eventsRegistered) return;
        eventsRegistered = false;

        if (health != null)
        {
             health.OnDeath -= HandleDeath;
             health.EventHealthChanged -= HandleHealthChanged;
        }
        if (unitAttack != null) unitAttack.OnAttack -= HandleAttack;
        if (enemyAI != null) enemyAI.OnAttack -= HandleAttack;
    }

    private void OnDestroy()
    {
        UnregisterEvents();
    }

    private Vector3 lastPosition;
    private bool eventsRegistered = false;

    private void Start()
    {
        lastPosition = transform.position;
        RegisterEvents();
        // ... (rest of start)

        // ... (rest of start)
        // Robot gibi aynı anda yürümemeleri için rastgele offset VE hız değişimi
        if (animator != null)
        {
            // 1. Rastgele yerden başla
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.fullPathHash, 0, Random.Range(0f, 1f));

            // 2. Animasyon hızını daha belirgin değiştir (0.8x ile 1.25x arası)
            // Bu sayede zamanla birbirlerinden koparlar, asla senkron olmazlar.
            animator.speed = Random.Range(0.8f, 1.25f);
        }
    }

    private void Update()
    {
        // Server veya Client fark etmez, hareket hızını pozisyon değişiminden hesapla.
        // Bu sayede Client'lar NavMeshAgent kullanmasa bile (NetworkTransform ile gelse bile) yürür.
        if (animator != null)
        {
            float moveDistance = Vector3.Distance(transform.position, lastPosition);
            float currentSpeed = moveDistance / Time.deltaTime;
            
            // Yumuşatma (Lerp) - Titremeyi önler
            float smoothedSpeed = Mathf.Lerp(animator.GetFloat(speedParam), currentSpeed, Time.deltaTime * 10f);
            
            // Hassasiyet eşiği (Gürültüyü önle)
            if (smoothedSpeed < 0.1f) smoothedSpeed = 0f;

            animator.SetFloat(speedParam, smoothedSpeed);
            
            lastPosition = transform.position;
        }
    }

    [SerializeField] private string hitParam = "Hit";
    private int lastKnownHealth = -1;

    private void HandleHealthChanged(int current, int max)
    {
        // İlk açılışta health set edilirken Hit oynamasın
        if (lastKnownHealth == -1)
        {
            lastKnownHealth = current;
            return;
        }

        // Can azaldıysa = HASAR
        if (current < lastKnownHealth)
        {
            if (animator != null)
            {
                // Canlıysa tepki ver
                if (current > 0)
                {
                    // KNOCKDOWN FIX: Yere düşmüş biri Hit titremesi yapmasın
                    if (health != null && health.isKnockedDown)
                    {
                        // Hit animasyonunu iptal et
                    }
                    else
                    {
                        // SyncVar hook hem Server'da hem tüm Client'larda çalışır!
                        // Bu yüzden NetworkAnimator'a gerek yok, direkt Animator'ı tetikle.
                        // (NetworkAnimator server-owned objeler için client'tan çalışmıyordu!)
                        animator.SetTrigger(hitParam);
                    }
                }
            }
        }
        lastKnownHealth = current;
    }

    private void HandleAttack()
    {
        if (animator == null || attackParams.Length == 0) return;
        
        // Rastgele bir saldırı seç
        int randomIndex = Random.Range(0, attackParams.Length);
        string selectedTrigger = attackParams[randomIndex];
        
        // Düşmanlar (Server tarafından yönetilir) direkt Base Animator'ı tetiklemeli
        // Oyuncular (Client Authority) NetworkAnimator'ı tetiklemeli
        if (isServer && !isOwned)
        {
            animator.SetTrigger(selectedTrigger);
        }
        else
        {
            networkAnimator.SetTrigger(selectedTrigger);
        }
    }

    private void HandleDeath()
    {
        if (animator == null) return;
        networkAnimator.SetTrigger(dieParam);
    }
}
