using UnityEngine;
using Mirror;

[RequireComponent(typeof(BoxCollider))]
public class WinZone : NetworkBehaviour
{
    private void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return;

        // Sadece PlayerController (Oyuncu) kabul et. Askerleri (UnitMovement) sayma.
        if (other.GetComponent<PlayerController>() != null || other.GetComponentInParent<PlayerController>() != null)
        {
            // Tekrarlı girişleri engellemek için kontrol eklenebilir ama basitçe +/- mantığı kurduk
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.PlayerEnteredWinZone();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!isServer) return;

        if (other.GetComponent<PlayerController>() != null || other.GetComponentInParent<PlayerController>() != null)
        {
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.PlayerExitedWinZone();
            }
        }
    }
}
