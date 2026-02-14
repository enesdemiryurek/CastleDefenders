using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Köylülerin kaçış davranışı (Sinematik için)
/// </summary>
public class VillagerFlee : MonoBehaviour
{
    [Header("Flee Settings")]
    [SerializeField] private Transform fleeTarget; // Kaçacağı nokta (köy dışı)
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private bool startFleeingOnAwake = false;

    private NavMeshAgent agent;
    private Animator animator;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        if (agent != null)
        {
            agent.speed = runSpeed;
        }
    }

    private void Start()
    {
        if (startFleeingOnAwake)
        {
            StartFleeing();
        }
    }

    public void StartFleeing()
    {
        if (agent != null && fleeTarget != null)
        {
            agent.SetDestination(fleeTarget.position);

            // Koşma animasyonu
            if (animator != null)
            {
                animator.SetBool("IsRunning", true);
            }
        }
    }

    public void StopFleeing()
    {
        if (agent != null)
        {
            agent.isStopped = true;

            if (animator != null)
            {
                animator.SetBool("IsRunning", false);
            }
        }
    }
}
