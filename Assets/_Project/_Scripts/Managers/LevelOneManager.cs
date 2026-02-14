using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables; // For Timeline

public class LevelOneManager : NetworkBehaviour
{
    public static LevelOneManager Instance;

    public enum LevelPhase { Camp, Village, Mountain, Castle, Completed }

    [Header("State")]
    [SyncVar] public LevelPhase currentPhase = LevelPhase.Camp;

    [Header("Phase 1: Camp (Ambush)")]
    [SerializeField] private List<EnemySpawner> campSpawners;
    [SerializeField] private GameObject campBarrier; // Köye giden yolu kapatan engel

    [Header("Phase 2: Village")]
    [SerializeField] private PlayableDirector villageIntroTimeline; // Sinematik
    [SerializeField] private List<EnemySpawner> villageGuardSpawners;
    [SerializeField] private List<EnemySpawner> villageWaveSpawners;
    [SerializeField] private GameObject villageBarrier; // Dağa giden yol
    [SerializeField] private GameObject villageCinematicCamera; // Sinematik kamera

    [Header("Phase 3: Mountain (Trap)")]
    [SerializeField] private GameObject rockTrapObject; // Düşecek kaya
    [SerializeField] private List<EnemySpawner> mountainFrontSpawners;
    [SerializeField] private List<EnemySpawner> mountainBackSpawners; // Pincer
    [SerializeField] private GameObject castleBarrier;

    [Header("Phase 4: Castle")]
    [SerializeField] private List<EnemySpawner> castleSpawners;
    [SerializeField] private WinZone winZone;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Sinematik kamerayı baştan kapat (Timeline açacak)
        if (villageCinematicCamera != null)
        {
            villageCinematicCamera.SetActive(false);
        }
    }

    [Server]
    public void StartPhase(LevelPhase phase)
    {
        if (phase <= currentPhase && phase != LevelPhase.Camp) return; // Geriye gitme veya aynı şeyi tetikleme

        currentPhase = phase;
        Debug.Log($"[LevelOneManager] Starting Phase: {phase}");

        switch (phase)
        {
            case LevelPhase.Camp:
                StartCampPhase();
                break;
            case LevelPhase.Village:
                StartVillagePhase();
                break;
            case LevelPhase.Mountain:
                StartMountainPhase();
                break;
            case LevelPhase.Castle:
                StartCastlePhase();
                break;
        }
    }

    // --- PHASE 1: CAMP ---
    [Server]
    private void StartCampPhase()
    {
        // 1. Düşmanları Başlat
        foreach (var spawner in campSpawners)
        {
            if(spawner != null) spawner.StartSpawning();
        }

        // 2. Düşmanların ölmesini bekle (Coroutine)
        StartCoroutine(CheckCampClearedRoutine());
    }

    [Server]
    private IEnumerator CheckCampClearedRoutine()
    {
        // Basit kontrol: Spawnerlar bitti mi ve yaşayan düşman var mı?
        // Daha detaylı bir EnemyManager sistemi olmadığı için, belirli aralıklarla sahnedeki "Enemy" tag'li objeleri sayabiliriz
        // veya Spawner'dan "bitti" eventini dinleriz.
        // Hızlı çözüm: Sahnedeki EnemyAI sayısına bak.
        
        yield return new WaitForSeconds(5f); // Spawn olsunlar diye bekle

        while (true)
        {
            yield return new WaitForSeconds(2f);
            
            EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            if (enemies.Length == 0)
            {
                // Temizlendi!
                Debug.Log("[LevelOneManager] Camp Cleared! Opening path to Village.");
                if (campBarrier != null) NetworkServer.Destroy(campBarrier);
                break;
            }
        }
    }

    // --- PHASE 2: VILLAGE ---
    [Server]
    private void StartVillagePhase()
    {
        // 1. Sinematik Tetikle (Clientlara Söyle)
        RpcPlayVillageCinematic();

        // 2. Nöbetçileri Başlat
        foreach (var spawner in villageGuardSpawners)
        {
            if (spawner != null) spawner.StartSpawning();
        }

        // 3. Büyük Dalga (Biraz gecikmeli)
        StartCoroutine(StartVillageWaveDelayed());
    }

    [ClientRpc]
    private void RpcPlayVillageCinematic()
    {
        Debug.Log("[LevelOneManager] RpcPlayVillageCinematic called!");
        
        if (villageIntroTimeline != null)
        {
            Debug.Log("[LevelOneManager] Playing village cinematic!");
            villageIntroTimeline.Play();
        }
        else
        {
            Debug.LogError("[LevelOneManager] villageIntroTimeline is NULL! Assign VillageCutscene object in Inspector!");
        }
    }

    [Server]
    private IEnumerator StartVillageWaveDelayed()
    {
        yield return new WaitForSeconds(10f); // Sinematik süresi veya oyuncunun ilerlemesi
        foreach (var spawner in villageWaveSpawners)
        {
            if (spawner != null) spawner.StartSpawning();
        }

        // Köy temizlenince yolu aç
        StartCoroutine(CheckVillageClearedRoutine());
    }

    [Server]
    private IEnumerator CheckVillageClearedRoutine()
    {
        yield return new WaitForSeconds(5f);
        while (true)
        {
            yield return new WaitForSeconds(2f);
            EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            // Sadece bu bölgedekileri saymak lazım ama şimdilik "Tüm düşmanlar ölsün" mantığı basit düzeyde yeterli
            // İleride Zone bazlı sayım yapılabilir.
            if (enemies.Length == 0)
            {
                Debug.Log("[LevelOneManager] Village Cleared! Opening path to Mountain.");
                if (villageBarrier != null) NetworkServer.Destroy(villageBarrier);
                break;
            }
        }
    }


    // --- PHASE 3: MOUNTAIN ---
    [Server]
    private void StartMountainPhase()
    {
        // 1. Kaya Tuzağı
        if (rockTrapObject != null)
        {
            Rigidbody rb = rockTrapObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false; 
                rb.useGravity = true;
                rb.AddForce(Vector3.down * 10f, ForceMode.Impulse); // İt
            }
        }

        // 2. Pincer Attack (Ön ve Arka)
        foreach (var spawner in mountainFrontSpawners) { if (spawner != null) spawner.StartSpawning(); }
        foreach (var spawner in mountainBackSpawners) { if (spawner != null) spawner.StartSpawning(); }

        StartCoroutine(CheckMountainClearedRoutine());
    }

    [Server]
    private IEnumerator CheckMountainClearedRoutine()
    {
        yield return new WaitForSeconds(5f);
        while (true)
        {
            yield return new WaitForSeconds(2f);
            EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            if (enemies.Length == 0)
            {
                Debug.Log("[LevelOneManager] Mountain Cleared! Opening path to Castle.");
                if (castleBarrier != null) NetworkServer.Destroy(castleBarrier);
                break;
            }
        }
    }

    // --- PHASE 4: CASTLE ---
    [Server]
    private void StartCastlePhase()
    {
        foreach (var spawner in castleSpawners)
        {
            if (spawner != null) spawner.StartSpawning();
        }
        
        // WinZone zaten LevelManager tarafından kontrol ediliyor.
        // Burası sadece son dalgayı başlatır.
    }
}
