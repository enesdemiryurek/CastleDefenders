using Mirror;
using UnityEngine;
using UnityEngine.AI;

public class SquadSpawner : NetworkBehaviour
{
    [Header("Squad Settings")]
    [SerializeField] private GameObject[] unitPrefabs; // Artık birden fazla birlik seçebilirsin
    [SerializeField] private int unitCountPerSquad = 10;
    [SerializeField] private float spacing = 0.8f; // Shield Wall (Çok Sıkı)
    [SerializeField] private int unitsPerRow = 3; // 3'erli Düzgün Sıra
    [SerializeField] private float distanceBetweenSquads = 6.0f; // Birlikler arası mesafe (Sıkılaştı)
    [SerializeField] private float maxNavMeshDistance = 5.0f; // NavMesh bulma yarıçapı

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
        // 1. Veri Kaynağını Belirle (SquadManager varsa oradan, yoksa Inspector'dan)
        GameObject[] unitsToSpawn = null;

        if (SquadManager.Instance != null)
        {
            // UnitData'dan GameObject dizisine çevir
            var selectedData = SquadManager.Instance.selectedSquads;
            unitsToSpawn = new GameObject[selectedData.Length];
            
            bool anySelected = false;
            for(int i=0; i<selectedData.Length; i++)
            {
                if (selectedData[i] != null)
                {
                    unitsToSpawn[i] = selectedData[i].unitPrefab;
                    anySelected = true;
                }
            }
            
            // Eğer SquadManager var ama hiç seçim yapılmamışsa (örn: direkt sahneden başlattık)
            // Inspector ayarlarını kullan (Fallback)
            if (!anySelected) unitsToSpawn = unitPrefabs;
        }
        else
        {
            unitsToSpawn = unitPrefabs;
        }

        if (unitsToSpawn == null || unitsToSpawn.Length == 0)
        {
            Debug.LogWarning("SquadSpawner: No units to spawn.");
            return;
        }

        // DÜZELTME: Player'ın o anki eğiminden etkilenmemek için yön vektörlerini düzleştiriyoruz (XZ Plane)
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        Vector3 startPos = transform.position - (flatForward * 5f); // Player'ın arkası (5m)

        // HER BİR BİRLİK ÇEŞİDİ İÇİN DÖNGÜ
        for (int squadIndex = 0; squadIndex < unitsToSpawn.Length; squadIndex++)
        {
            GameObject currentPrefab = unitsToSpawn[squadIndex];
            if (currentPrefab == null) continue;

            // Her yeni birlik bir öncekinin biraz daha arkasında dursun
            Vector3 squadOffset = -(flatForward * (squadIndex * distanceBetweenSquads)); 
            Vector3 currentSquadStartPos = startPos + squadOffset;

            // 3. SIRA MANTIĞI (Shield Wall Spawn)
            int rowCount = 3;
            int dynamicUnitsPerRow = Mathf.CeilToInt(unitCountPerSquad / (float)rowCount);
            if (dynamicUnitsPerRow < 1) dynamicUnitsPerRow = 1;

            for (int i = 0; i < unitCountPerSquad; i++)
            {
                // Dizilim Hesabı (Formation Grid)
                float xOffset = (i % dynamicUnitsPerRow) * spacing - ((dynamicUnitsPerRow * spacing) / 2f);
                float zOffset = (i / dynamicUnitsPerRow) * spacing;

                Vector3 spawnPos = currentSquadStartPos + (flatRight * xOffset) - (flatForward * zOffset);
                
                // FIX: NavMesh üzerinde geçerli bir nokta bul
                NavMeshHit hit;
                if (NavMesh.SamplePosition(spawnPos, out hit, maxNavMeshDistance, NavMesh.AllAreas))
                {
                    spawnPos = hit.position;
                }

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

            // DOĞDUKLARI GİBİ TAKİPE AL (Komutları benden al)
            /*
            if (commander != null)
            {
                commander.ServerSetFollowing(squadIndex, true);
            }
            */
        }
    }
}
