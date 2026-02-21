using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables; // For Timeline
using UnityEngine.Video; // For Video Cutscenes

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
    [SerializeField] private PlayableDirector villageIntroTimeline; // Sinematik (opsiyonel)
    [SerializeField] private VideoClip villageVideoClip; // Köy giriş videosu
    [SerializeField] private List<EnemySpawner> villageGuardSpawners;
    [SerializeField] private List<EnemySpawner> villageWaveSpawners;
    [SerializeField] private GameObject villageBarrier; // Dağa giden yol
    [SerializeField] private GameObject villageCinematicCamera; // Sinematik kamera

    [Header("Phase 3: Mountain (Trap)")]
    [SerializeField] private VideoClip mountainVideoClip; // Dağ giriş videosu
    [SerializeField] private GameObject rockTrapObject; // Düşecek kaya
    [SerializeField] private float rockDropDelay = 3f; // Kaya düşme gecikmesi (oyuncu geçerken)
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
        // 1. VIDEO CUTSCENE Tetikle (Tüm Clientlara)
        RpcPlayVillageCutscene();

        // 2. Video süresi kadar bekle, sonra nöbetçileri başlat
        float videoLength = villageVideoClip != null ? (float)villageVideoClip.length : 5f;
        StartCoroutine(StartVillageAfterVideo(videoLength));
    }

    [ClientRpc]
    private void RpcPlayVillageCutscene()
    {
        Debug.Log("[LevelOneManager] RpcPlayVillageCutscene called!");
        
        if (CutsceneManager.Instance != null && villageVideoClip != null)
        {
            CutsceneManager.Instance.PlayCutscene(villageVideoClip);
        }
        else if (villageIntroTimeline != null)
        {
            // Fallback: Timeline kullan
            villageIntroTimeline.Play();
        }
        else
        {
            Debug.LogWarning("[LevelOneManager] Ne video ne timeline atanmış! Cutscene atlandı.");
        }
    }

    [Server]
    private IEnumerator StartVillageAfterVideo(float videoLength)
    {
        yield return new WaitForSecondsRealtime(videoLength + 1f); // Video + 1s buffer

        // Nöbetçileri Başlat
        foreach (var spawner in villageGuardSpawners)
        {
            if (spawner != null) spawner.StartSpawning();
        }

        // Büyük Dalga (Biraz gecikmeli)
        StartCoroutine(StartVillageWaveDelayed());
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
        // 1. VIDEO CUTSCENE Tetikle
        RpcPlayMountainCutscene();

        // 2. Video bittikten sonra: Kaya + Düşmanlar
        float videoLength = mountainVideoClip != null ? (float)mountainVideoClip.length : 0f;
        StartCoroutine(StartMountainAfterVideo(videoLength));
    }

    [ClientRpc]
    private void RpcPlayMountainCutscene()
    {
        if (CutsceneManager.Instance != null && mountainVideoClip != null)
        {
            CutsceneManager.Instance.PlayCutscene(mountainVideoClip);
        }
    }

    [Server]
    private IEnumerator StartMountainAfterVideo(float videoLength)
    {
        // Video bitmesini bekle
        yield return new WaitForSecondsRealtime(videoLength + 1f);

        // Kaya düşürme gecikmesi (oyuncu geçerken düşsün)
        yield return new WaitForSeconds(rockDropDelay);

        // KAYA DÜŞÜR!
        RpcDropRock();

        // Düşmanları başlat (Pincer Attack)
        foreach (var spawner in mountainFrontSpawners) { if (spawner != null) spawner.StartSpawning(); }
        yield return new WaitForSeconds(2f); // 2 saniye sonra arkadan da gelsinler
        foreach (var spawner in mountainBackSpawners) { if (spawner != null) spawner.StartSpawning(); }

        StartCoroutine(CheckMountainClearedRoutine());
    }

    [ClientRpc]
    private void RpcDropRock()
    {
        if (rockTrapObject != null)
        {
            Rigidbody rb = rockTrapObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.AddForce(Vector3.down * 10f, ForceMode.Impulse);
            }
            
            Debug.Log("[LevelOneManager] KAYA DÜŞTÜ!");
        }
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
