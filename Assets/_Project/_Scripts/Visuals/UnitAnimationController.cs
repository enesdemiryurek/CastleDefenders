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

    public override void OnStartClient()
    {
        // Eventlere abone ol
        if (health != null)
        {
            health.OnDeath += HandleDeath;
            health.EventHealthChanged += HandleHealthChanged;
        }
        if (unitAttack != null) unitAttack.OnAttack += HandleAttack;
        if (enemyAI != null) enemyAI.OnAttack += HandleAttack;
    }

    public override void OnStopClient()
    {
        // Abonelikleri temizle
        if (health != null)
        {
             health.OnDeath -= HandleDeath;
             health.EventHealthChanged -= HandleHealthChanged;
        }
        if (unitAttack != null) unitAttack.OnAttack -= HandleAttack;
        if (enemyAI != null) enemyAI.OnAttack -= HandleAttack;
    }

    // ... Update Method ...

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
            if (animator != null && networkAnimator != null)
            {
                // Canlıysa tepki ver
                if (current > 0)
                {
                    networkAnimator.SetTrigger(hitParam);
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
        networkAnimator.SetTrigger(selectedTrigger);
    }

    private void HandleDeath()
    {
        if (animator == null) return;
        networkAnimator.SetTrigger(dieParam);
    }
}
