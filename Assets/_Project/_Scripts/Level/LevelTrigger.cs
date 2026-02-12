using UnityEngine;
using Mirror;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NetworkIdentity))]
public class LevelTrigger : NetworkBehaviour
{
    [SerializeField] private LevelOneManager.LevelPhase phaseToTrigger;
    [SerializeField] private bool oneTimeUse = true;

    private bool triggered = false;

    private void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        
        // NetworkIdentity yoksa veya Server değilsek çalışma
        if (netIdentity == null || !isServer) return;

        if (triggered && oneTimeUse) return;

        // Sadece Player tetikler
        if (other.GetComponent<PlayerController>() != null)
        {
            triggered = true;
            if (LevelOneManager.Instance != null)
            {
                LevelOneManager.Instance.StartPhase(phaseToTrigger);
            }
            else
            {
                Debug.LogError("[LevelTrigger] LevelOneManager Instance not found!");
            }
        }
    }
}
