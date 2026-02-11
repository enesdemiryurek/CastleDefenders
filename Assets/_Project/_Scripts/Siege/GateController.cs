using Mirror;
using UnityEngine;
using UnityEngine.AI;

public class GateController : NetworkBehaviour, IDamageable
{
    [Header("Gate Settings")]
    [SerializeField] private int maxHealth = 500;
    [SerializeField] private bool startingStateOpen = false;
    
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshObstacle navMeshObstacle;
    [SerializeField] private Collider gateCollider;
    [SerializeField] private GameObject visuals;
    [SerializeField] private GameObject destroyedVersion; // Optional

    [SyncVar(hook = nameof(OnHealthChanged))]
    private int currentHealth;

    [SyncVar(hook = nameof(OnOpenChanged))]
    private bool isOpen;

    public int CurrentHealth => currentHealth;

    public override void OnStartServer()
    {
        currentHealth = maxHealth;
        isOpen = startingStateOpen;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateGateState(isOpen);
    }

    // --- INTERACTION ---
    [Command(requiresAuthority = false)] // Anyone can interact (teams logic can be added later)
    public void CmdInteract()
    {
        if (currentHealth <= 0) return; // Dead gate can't open
        isOpen = !isOpen;
    }

    // --- DAMAGE ---
    [Server]
    public void TakeDamage(int amount, Vector3? damageSource = null)
    {
        if (currentHealth <= 0) return;

        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            RpcDestroyGate();
        }
    }

    // --- SYNC HOOKS & VISUALS ---
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        // Update Health Bar if we have one attached
    }

    private void OnOpenChanged(bool oldState, bool newState)
    {
        UpdateGateState(newState);
    }

    private void UpdateGateState(bool open)
    {
        if (animator != null)
        {
            animator.SetBool("IsOpen", open);
        }

        // If open, disable obstacle so units can pass.
        // If closed, enable obstacle to block path.
        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = !open;
        }
        
        // Setup collision if needed (Optional: maybe physics collider stays?)
        // Usually gate collider stays enabled to block physics even if open (walls), 
        // but the 'door' part might disable its collider. 
        // For simplicity, we assume the collider is the door itself.
        if (gateCollider != null)
        {
           // gateCollider.enabled = !open; // Let animator handle physics or keep it?
           // Usually better to keep collider and handle it via Animation or separate "Blocker" collider.
           // For now, let's assume the NavMeshObstacle is the main blocker for AI.
        }
    }

    [ClientRpc]
    private void RpcDestroyGate()
    {
        if (visuals != null) visuals.SetActive(false);
        if (destroyedVersion != null) destroyedVersion.SetActive(true);
        if (navMeshObstacle != null) navMeshObstacle.enabled = false; // Walkable? Or debris blocks?
        if (gateCollider != null) gateCollider.enabled = false;
    }
}
