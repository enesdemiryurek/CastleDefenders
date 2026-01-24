using Mirror;
using UnityEngine;

public class SquadSpawner : NetworkBehaviour
{
    [Header("Squad Settings")]
    [SerializeField] private GameObject[] unitPrefabs; // Artık birden fazla birlik seçebilirsin
    [SerializeField] private int unitCountPerSquad = 10;
    [SerializeField] private float spacing = 1.5f;
    [SerializeField] private int unitsPerRow = 5;
    [SerializeField] private float distanceBetweenSquads = 8.0f; // Birlikler arası mesafe

    private PlayerUnitCommander commander;

    private void Awake()
    {
        commander = GetComponent<PlayerUnitCommander>();
    }

    public override void OnStartServer()
    {
        // Oyun başlayınca (Player doğunca) askeri birliği oluştur
        SpawnSquad();
    }

    [Server]
    private void SpawnSquad()
    {
        if (unitPrefabs == null || unitPrefabs.Length == 0)
        {
            Debug.LogError("SquadSpawner: Unit Prefabs are missing!");
            return;
        }

        Vector3 startPos = transform.position - (transform.forward * 3f); // Player'ın biraz arkası

        // HER BİR BİRLİK ÇEŞİDİ İÇİN DÖNGÜ
        for (int squadIndex = 0; squadIndex < unitPrefabs.Length; squadIndex++)
        {
            GameObject currentPrefab = unitPrefabs[squadIndex];
            if (currentPrefab == null) continue;

            // Her yeni birlik bir öncekinin biraz daha arkasında dursun
            Vector3 squadOffset = -(transform.forward * (squadIndex * distanceBetweenSquads)); 
            Vector3 currentSquadStartPos = startPos + squadOffset;

            for (int i = 0; i < unitCountPerSquad; i++)
            {
                // Dizilim Hesabı (Formation Grid)
                float xOffset = (i % unitsPerRow) * spacing - ((unitsPerRow * spacing) / 2f);
                float zOffset = (i / unitsPerRow) * spacing;

                Vector3 spawnPos = currentSquadStartPos + (transform.right * xOffset) - (transform.forward * zOffset);
                
                // 1. Yarat
                GameObject newUnit = Instantiate(currentPrefab, spawnPos, transform.rotation);

                // 2. Network Spawn
                NetworkServer.Spawn(newUnit, connectionToClient);

                // 3. Commander'a kaydet (Benim askerim ol)
                if (commander != null)
                {
                    UnitMovement movement = newUnit.GetComponent<UnitMovement>();
                    if (movement != null)
                    {
                        movement.SquadIndex = squadIndex; // Tim numarasını ata (0, 1, 2...)
                        commander.RegisterUnit(movement);
                    }
                }
            }
        }
    }
}
