using Mirror;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public class EnemySpawner : NetworkBehaviour
{
    [System.Serializable]
    public struct SpawnConfig
    {
        public string name; // Inspector'da ayırt etmek için (Örn: "Tier 1 Waves")
        public Transform spawnPoint; // Nerede doğacaklar?
        public GameObject enemyPrefab; // Hangi asker?
        public int countPerWave; // Tek seferde kaç tane?
        public int rows; // Kaç sıra olsun? (Formasyon için)
        public float waveInterval; // Kaç saniyede bir?
        public int totalWaves; // Toplam kaç kez tekrarlasın?
        public float startDelay; // İlk dalga ne zaman başlasın?
    }

    [Header("Wave Configuration")]
    [SerializeField] private List<SpawnConfig> spawnConfigs = new List<SpawnConfig>();

    [Header("Formation Settings")]
    [SerializeField] private float spacing = 1.5f; // Askerler arası boşluk
    [SerializeField] private float maxNavMeshDistance = 5.0f; // NavMesh bulma yarıçapı

    [Header("Trigger Settings")]
    [SerializeField] private bool playOnAwake = true; // Tikliyse oyun başlar başlamaz spawnlar, yoksa Trigger bekler.

    public override void OnStartServer()
    {
        if (playOnAwake)
        {
            StartSpawning();
        }
    }

    [Server]
    public void StartSpawning()
    {
        // Her bir konfigürasyon için ayrı bir zamanlayıcı başlat
        foreach (var config in spawnConfigs)
        {
            StartCoroutine(SpawnRoutine(config));
        }
    }

    private IEnumerator SpawnRoutine(SpawnConfig config)
    {
        if (config.enemyPrefab == null || config.spawnPoint == null)
        {
            Debug.LogError($"[EnemySpawner] Config '{config.name}' is missing Prefab or SpawnPoint!");
            yield break;
        }

        // Başlangıç gecikmesi
        yield return new WaitForSeconds(config.startDelay);

        for (int wave = 0; wave < config.totalWaves; wave++)
        {
            SpawnWave(config);
            yield return new WaitForSeconds(config.waveInterval);
        }
    }

    private void SpawnWave(SpawnConfig config)
    {
        // Grid / Formation Hesaplama
        // rows = sıra sayısı. columns = sütun sayısı.
        // count = 15, row = 3 ise -> 5'erli 3 sıra.
        
        int columns = Mathf.CeilToInt((float)config.countPerWave / config.rows);
        
        for (int i = 0; i < config.countPerWave; i++)
        {
            // Matris pozisyonu (Satır, Sütun)
            int row = i / columns;
            int col = i % columns;

            // SpawnPoint'e göre yerel pozisyon
            // Arka arkaya (Z) ve yan yana (X) diziyoruz
            // -row * spacing: Arkaya doğru git
            // col * spacing: Yana doğru git
            // (col - columns/2f) * spacing: Ortalamak için
            
            float xOffset = (col - (columns / 2f)) * spacing;
            float zOffset = -row * spacing; // Arkaya doğru dizersen ileri koşarken çarpışmazlar

            Vector3 offset = new Vector3(xOffset, 0, zOffset);
            
            // SpawnPoint'in rotasyonuna göre ofseti döndür (Böylece SpawnPoint nereye bakıyorsa oraya doğru dizilirler)
            Vector3 finalPosition = config.spawnPoint.position + (config.spawnPoint.rotation * offset);

            // FIX: NavMesh üzerinde geçerli bir nokta bul
            NavMeshHit hit;
            if (NavMesh.SamplePosition(finalPosition, out hit, maxNavMeshDistance, NavMesh.AllAreas))
            {
                finalPosition = hit.position;
            }

            GameObject newEnemy = Instantiate(config.enemyPrefab, finalPosition, config.spawnPoint.rotation);
            NetworkServer.Spawn(newEnemy);
        }
    }
}
