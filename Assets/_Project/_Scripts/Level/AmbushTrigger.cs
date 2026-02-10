using UnityEngine;
using Mirror;

[RequireComponent(typeof(BoxCollider))]
public class AmbushTrigger : NetworkBehaviour
{
    [Header("Ambush Settings")]
    [SerializeField] private EnemySpawner targetSpawner; // Tetiklenince çalışacak spawner
    [SerializeField] private bool oneTimeUse = true;

    private bool triggered = false;

    private void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        
        // Spawner'ı başlangıçta kapalı yapabiliriz, ya da Spawner scriptine "Wait For Trigger" özelliği ekleyebiliriz.
        // Şimdilik Spawner'ın "Auto Start"ını kapatıp buradan tetikleyeceğiz varsayıyoruz.
    }

    private void OnTriggerEnter(Collider other)
    {
        // DEBUG: Ne girdi?
        Debug.Log($"[AmbushTrigger] Trigger'a giren obje: {other.name}, Tag: {other.tag}, Root: {other.transform.root.name}");

        if (!isServer) return;
        if (triggered && oneTimeUse) return;

        // Sadece Oyuncular tetikleyebilir (Askerler tetiklemesin, sürpriz kaçar)
        if (other.GetComponent<PlayerController>() != null)
        {
            TriggerAmbush();
        }
    }

    private void TriggerAmbush()
    {
        triggered = true;
        Debug.Log($"[AmbushTrigger] Ambush triggered by player!");

        if (targetSpawner != null)
        {
            // Spawner'ı manuel başlat
            targetSpawner.StartSpawning();
        }
    }
}
