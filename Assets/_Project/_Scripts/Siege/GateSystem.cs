using UnityEngine;
using Mirror;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(NavMeshObstacle))]
public class GateSystem : NetworkBehaviour, IDamageable
{
    [Header("Gate Settings")]
    [SerializeField] private int maxHealth = 1000;
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private Vector3 rotationAxis = Vector3.up; // YENİ: Dönüş eksenini seçebilsin (User: Yukarı açılıyor dedi)
    [SerializeField] private float doorSpeed = 2f;
    [SerializeField] private bool startOpen = false;

    [Header("Baking Helper")]
    [SerializeField] private bool bakedOpen = false; // BAKE İÇİN AÇIK BIRAKILDIYSA, OYUN BAŞINDA KAPAT

    [Header("Interaction")]
    [SerializeField] private float interactionRange = 5f;
    [SerializeField] private Key interactKey = Key.E;

    [SyncVar(hook = nameof(OnHealthChanged))]
    private int currentHealth;

    [SyncVar(hook = nameof(OnStateChanged))]
    private bool isOpen = false;

    private NavMeshObstacle obstacle;
    private Collider[] gateColliders; 
    private Quaternion initialLeftRot;
    private Quaternion initialRightRot;
    private Quaternion targetLeftRot;
    private Quaternion targetRightRot;

    public event System.Action OnDeath;
    public int CurrentHealth => currentHealth; 
    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        obstacle = GetComponent<NavMeshObstacle>();
        gateColliders = GetComponentsInChildren<Collider>(); 
        
        if (gateColliders.Length == 0) Debug.LogError($"[GateSystem] '{name}' objesinde veya çocuklarında hiç Collider bulunamadı! İçinden geçilir.");

        // --- BAKED OPEN LOGIC ---
        // Eğer Bake almak için açık bıraktıysak, "Kapalı" halini hesaplayalım
        // Sol Kapı: Açılırken -Angle dönüyor -> Kapanmak için +Angle dönmeli
        // Sağ Kapı: Açılırken +Angle dönüyor -> Kapanmak için -Angle dönmeli
        
        if (leftDoor)
        {
            if (bakedOpen)
            {
                 // Şu an AÇIK duruyor (90 derece). Bizim "Kapalı Referansımız" (initialLeftRot) 0 olmalı.
                 // Açık = Kapalı * Rotate(-90)
                 // Kapalı = Açık * Rotate(90)
                 initialLeftRot = leftDoor.localRotation * Quaternion.Euler(rotationAxis * openAngle);
                 leftDoor.localRotation = initialLeftRot; // Görsel olarak hemen kapat
            }
            else
            {
                initialLeftRot = leftDoor.localRotation;
            }
        }

        if (rightDoor)
        {
             if (bakedOpen)
             {
                 // Sağ Kapı Açık = Kapalı * Rotate(90)
                 // Kapalı = Açık * Rotate(-90)
                 initialRightRot = rightDoor.localRotation * Quaternion.Euler(rotationAxis * -openAngle);
                 rightDoor.localRotation = initialRightRot; // Görsel olarak hemen kapat
             }
             else
             {
                initialRightRot = rightDoor.localRotation;
             }
        }
    }

    public override void OnStartServer()
    {
        currentHealth = maxHealth;
        // isOpen = startOpen; // ARTIKstartOpen'ı kullanmayalım, Inspector ne diyorsa o olsun (Default: False)
        if (startOpen) isOpen = true; // Eğer özellikle istenirse

        UpdateObstacle();
    }

    private void Update()
    {
        // Kapı Hareketi (Client & Server)
        MoveDoors();

        // Interaction (Client Side)
        if (isClient && !IsDead)
        {
            CheckInteraction();
        }
    }

    private void CheckInteraction()
    {
        if (Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
        {
            Debug.Log("[GateSystem] E Tuşuna Basıldı.");
            // Yakınlık Kontrolü (Player)
            var player = NetworkClient.localPlayer;
            if (player == null) 
            {
                Debug.LogWarning("[GateSystem] LocalPlayer BULUNAMADI!");
                return;
            }

            float dist = Vector3.Distance(transform.position, player.transform.position);
            Debug.Log($"[GateSystem] Mesafe: {dist} / {interactionRange}");

            if (dist <= interactionRange)
            {
                CmdToggleGate();
            }
            else
            {
                 Debug.Log("[GateSystem] Mesafe Yetersiz!");
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);

        // Kırmızı Kutu: Gate Kapalıyken Yolu Kapatan Alan
        // Kapı AÇIKSA (isOpen=true) bu alan yok olur.
        NavMeshObstacle obs = GetComponent<NavMeshObstacle>();
        if (obs != null)
        {
            Gizmos.color = isOpen ? Color.green : Color.red; // Kapalıysa Kırmızı, Açıksa Yeşil
            Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.matrix = rotationMatrix;
            Gizmos.DrawWireCube(obs.center, obs.size);
            Gizmos.matrix = Matrix4x4.identity; // Reset Matrix
        }

        // --- GÖRÜNMEZ DUVARLARI GÖRÜNÜR YAP ---
        if (gateColliders != null)
        {
            foreach (var col in gateColliders)
            {
                if (col == null) continue;
                // Aktif olanlar MAVİ (Geçilmez), Pasif olanlar GRİ (Geçilir)
                Gizmos.color = col.enabled ? Color.blue : Color.gray;
                Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
            }
        }
    }

    [Command(requiresAuthority = false)] // Herkes açabilsin (Takım kontrolü eklenebilir)
    private void CmdToggleGate()
    {
        if (IsDead) return;
        isOpen = !isOpen;
        UpdateObstacle();
    }

    private void UpdateObstacle()
    {
        // Kapı AÇIKSA -> Obstacle KAPALI (Geçiş Var)
        // Kapı KAPALIYSA -> Obstacle AÇIK (Geçiş Yok)
        if (obstacle != null)
        {
            obstacle.enabled = !isOpen; 
            obstacle.carving = !isOpen; // NavMesh'i sadece kapalıyken del

            Debug.Log($"[GateSystem] Kapı Durumu: {(isOpen?"AÇIK":"KAPALI")} -> Obstacle Enabled: {obstacle.enabled}");
        }

        // FİZİKSEL GEÇİŞ: Kapı açıksa Collider'ları kapat ki içinden geçebilelim
        if (gateColliders != null)
        {
            foreach(var col in gateColliders)
            {
                if(col != null) col.enabled = !isOpen;
            }
        }
    }

    private void MoveDoors()
    {
        if (leftDoor == null || rightDoor == null) return;

        // Eksen kullanımı (Vector3.up yerine rotationAxis)
        Quaternion leftTarget = isOpen ? initialLeftRot * Quaternion.Euler(rotationAxis * -openAngle) : initialLeftRot;
        Quaternion rightTarget = isOpen ? initialRightRot * Quaternion.Euler(rotationAxis * openAngle) : initialRightRot;
        
        // Eğer Kırıldıysa (Dead) yere yatır veya yok et (Şimdilik yere yatıralım - X ekseninde)
        // Kırılma yönü genelde sabittir ama istersen bunu da ayarlarız.
        if (IsDead)
        {
             leftTarget = initialLeftRot * Quaternion.Euler(90, 0, 0); // Kırılınca Yere Yatsın
             rightTarget = initialRightRot * Quaternion.Euler(90, 0, 0);
        }

        leftDoor.localRotation = Quaternion.Slerp(leftDoor.localRotation, leftTarget, Time.deltaTime * doorSpeed);
        rightDoor.localRotation = Quaternion.Slerp(rightDoor.localRotation, rightTarget, Time.deltaTime * doorSpeed);
    }

    // --- IDamageable Implementation ---

    [Server]
    public void TakeDamage(int amount, Vector3? source = null)
    {
        if (IsDead || isOpen) return; // Açık kapıya vurulmaz (veya vurulur ama gereksiz)

        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
    }

    private void Die()
    {
        OnDeath?.Invoke();
        isOpen = true; // Kırılınca açılmış sayılır
        UpdateObstacle();
        RpcOnBroken();
    }

    [ClientRpc]
    private void RpcOnBroken()
    {
        // Efekt, ses vs.
        Debug.Log("KAPI KIRILDI!");
    }

    private void OnHealthChanged(int oldVal, int newVal)
    {
        // UI updates?
    }

    private void OnStateChanged(bool oldVal, bool newVal)
    {
        // State değişince Obstacle/Collider durumunu güncelle (Client'ta da)
        UpdateObstacle();
    }
    
    public Vector3 GetPosition() => transform.position;

    [ContextMenu("DEBUG: Gate Info")]
    public void DebugGateInfo()
    {
        Debug.Log($"--- GATE DEBUG INFO ---");
        
        // Edit Mode Desteği: Değişkenler null ise tekrar bul
        if (obstacle == null) obstacle = GetComponent<NavMeshObstacle>();
        if (gateColliders == null || gateColliders.Length == 0) gateColliders = GetComponentsInChildren<Collider>();

        Debug.Log($"State: {(isOpen ? "OPEN" : "CLOSED")}, Health: {currentHealth}");
        
        if (obstacle != null)
            Debug.Log($"Obstacle: Enabled={obstacle.enabled}, Carving={obstacle.carving}, Center={obstacle.center}, Size={obstacle.size}");
        else
            Debug.LogError("Obstacle: MISSING (NavMeshObstacle Scriptsiz Obje Mi?)");

        if (gateColliders != null && gateColliders.Length > 0)
        {
            Debug.Log($"Colliders Found: {gateColliders.Length}");
            foreach (var col in gateColliders)
            {
                if (col == null) continue;
                // Trigger ise içinden geçilir!
                string status = col.isTrigger ? "TRIGGER (YOL VERİR!)" : "SOLID (BLOCKS)";
                Debug.Log($" - Collider: '{col.name}' | Enabled: {col.enabled} | Status: {status} | Layer: {LayerMask.LayerToName(col.gameObject.layer)}");
            }
        }
        else
        {
            Debug.LogError("Colliders Array is NULL or EMPTY (Hiç Collider Yok!)");
        }
        Debug.Log("-----------------------");
    }
}
