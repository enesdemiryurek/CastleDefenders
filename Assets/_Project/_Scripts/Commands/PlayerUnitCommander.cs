using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkIdentity))]
public class PlayerUnitCommander : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float rayDistance = 100f;

    [Header("Debug")]
    [SerializeField] private GameObject debugMarkerPrefab;

    private Camera mainCamera;
    
    // Server-side list tracking all units belonging to this commander
    // Client da bilsin diye SyncList yapıyoruz (Preview için gerekli)
    public readonly SyncList<UnitMovement> myUnits = new SyncList<UnitMovement>();
    
    // SyncList to track which squad indices are currently in "Follow Mode"
    // Valid indices: 0, 1, 2...
    private readonly SyncList<int> followingSquads = new SyncList<int>();

    private int selectedSquadIndex = 0; // Client-side selection

    [Header("UI Feedback")]
    [SerializeField] private bool showSelectionLog = true;

    // --- FORMATION PREVIEW VARIABLES ---
    [Header("Formation Preview")]
    [SerializeField] private GameObject formationMarkerPrefab; 
    private List<GameObject> previewMarkers = new List<GameObject>();
    private bool isCommandMode = false;
    private Vector3 commandCursorPos; // Sanal cursor pozisyonu (Dünya koordinatlarında)
    private float commandCursorDist = 10f; // Oyuncudan ne kadar uzakta?
    private Quaternion commandRotation; // Formasyonun dönüşü

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        // CLIENT SIDE INPUT
        if (isLocalPlayer)
        {
            HandleInput();
            
            if (isCommandMode)
            {
                UpdateCommandModeLogic();
            }
        }

        // SERVER SIDE LOGIC
        if (NetworkServer.active)
        {
            UpdateFollowFormation();
        }
    }
    
    // Spawner tarafından çağrılacak
    [Server]
    public void RegisterUnit(UnitMovement unit)
    {
        if (!myUnits.Contains(unit))
        {
            myUnits.Add(unit);
        }
    }

    private void HandleInput()
    {
        // 1-9 arası Tim Seçimi
        if (Keyboard.current.digit1Key.wasPressedThisFrame) ChangeSelection(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) ChangeSelection(1);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) ChangeSelection(2);
        
        // 'X' -> KOMUT MODU (Toggle)
        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            ToggleCommandMode(!isCommandMode);
        }

        // Eğer Command Mode açıksa, diğer komutları (Attack/Follow) engelle veya modu kapat
        if (isCommandMode)
        {
            // Sol Tık -> ONAYLA ve GÖNDER
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                CmdMoveUnits(selectedSquadIndex, commandCursorPos, commandRotation);
                ToggleCommandMode(false); // Moddan çık
            }

            // Sağ Tık -> İPTAL
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                ToggleCommandMode(false);
            }
        }
        else
        {
            // 'C' -> Beni takip et
            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                CmdStartFollowing(selectedSquadIndex);
            }

            // 'V' -> Serbest Saldırı
            if (Keyboard.current.vKey.wasPressedThisFrame)
            {
                CmdAttackOrder(selectedSquadIndex);
            }
        }
    }

    private void ToggleCommandMode(bool state)
    {
        isCommandMode = state;
        
        // Oyuncuyu Kilitle/Aç
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null) pc.InputEnabled = !state;

        if (isCommandMode)
        {
            // Mod açılınca cursor'ı oyuncunun önüne koy
            commandCursorDist = 10f;
            commandCursorPos = transform.position + transform.forward * commandCursorDist;
            
            // Yüksekliği ayarla
             if (Physics.Raycast(commandCursorPos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, groundLayer))
             {
                 commandCursorPos = hit.point;
             }
             
             commandRotation = transform.rotation; // Oyuncunun baktığı yöne bak
        }
        else
        {
            ClearPreview();
        }
    }

    private void UpdateCommandModeLogic()
    {
        // 1. WASD ile Cursor Kontrolü
        float moveInput = 0f;
        float rotateInput = 0f;

        if (Keyboard.current.wKey.isPressed) moveInput += 1f;
        if (Keyboard.current.sKey.isPressed) moveInput -= 1f;
        if (Keyboard.current.dKey.isPressed) rotateInput += 1f; // Sağa dön
        if (Keyboard.current.aKey.isPressed) rotateInput -= 1f; // Sola dön

        // Mesafeyi (İleri/Geri) ayarla
        if (Mathf.Abs(moveInput) > 0.1f)
        {
            commandCursorDist += moveInput * Time.deltaTime * 15f; // Hız
            commandCursorDist = Mathf.Clamp(commandCursorDist, 2f, 50f); // Min-Max mesafe
        }

        // Rotasyonu (Sağ/Sol) ayarla
        if (Mathf.Abs(rotateInput) > 0.1f)
        {
            commandRotation *= Quaternion.Euler(0, rotateInput * Time.deltaTime * 90f, 0);
        }

        // Pozisyonu Kameranın baktığı yöne göre, belirlenen mesafede güncelle
        // Oyuncu fareyi çevirdikçe "Line" da döner.
        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0; // Yere paralel
        camForward.Normalize();

        Vector3 targetPoint = transform.position + camForward * commandCursorDist;

        // Zemine Yapıştır
        if (Physics.Raycast(targetPoint + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, groundLayer))
        {
            commandCursorPos = hit.point;
        }
        else
        {
            commandCursorPos = targetPoint;
        }

        // 2. Görselleştirme (Preview)
        UpdatePreviewVisuals();
    }

    private void UpdatePreviewVisuals()
    {
        // 1. Seçili Asker Sayısı
        int count = 0;
        foreach(var u in myUnits) 
        {
            if(u != null && u.SquadIndex == selectedSquadIndex) count++;
        }

        if (count == 0) return;

        // 2. Noktaları Hesapla (Artık commandCursorPos kullanıyoruz)
        List<Vector3> points = CalculateFormationPoints(commandCursorPos, commandRotation, count, 1.0f);

        // 3. Markerları Yerleştir
        EnsureMarkerPool(points.Count);

        for (int i = 0; i < previewMarkers.Count; i++)
        {
            if (i < points.Count)
            {
                previewMarkers[i].SetActive(true);
                
                // Zemin Yüksekliği (Hafif Raycast)
                Vector3 p = points[i];
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit gh, 10f, groundLayer))
                {
                    p.y = gh.point.y + 0.1f;
                }
                
                previewMarkers[i].transform.position = p;
                previewMarkers[i].transform.rotation = commandRotation;
            }
            else
            {
                previewMarkers[i].SetActive(false);
            }
        }
    }

    private void EnsureMarkerPool(int count)
    {
        // Yeterli değilse ekle
        while (previewMarkers.Count < count)
        {
            GameObject marker;
            if (formationMarkerPrefab != null)
            {
                marker = Instantiate(formationMarkerPrefab);
            }
            else
            {
                // Prefab yoksa ilkel (Primitive) oluştur (Fallback)
                marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                marker.transform.rotation = Quaternion.Euler(90, 0, 0); // Yere yatır
                marker.transform.localScale = Vector3.one * 0.5f;
                // Collider'ı sil ki Raycast'i engellemesin
                Destroy(marker.GetComponent<Collider>());
                marker.GetComponent<Renderer>().material.color = new Color(0, 0.5f, 1f, 0.5f); // Mavi
            }
            previewMarkers.Add(marker);
        }
    }

    private void ClearPreview()
    {
        foreach (var m in previewMarkers) m.SetActive(false);
    }

    public event System.Action<int> OnSquadSelected;

    private void ChangeSelection(int index)
    {
        selectedSquadIndex = index;
        if (showSelectionLog) Debug.Log($"Seçili Birlik: {index + 1}. Takım");
        
        UpdateVisualSelection(); // Görsel Outline Güncelle
        OnSquadSelected?.Invoke(index);
    }
    
    // --- SELECTION VISUALS (Quick Outline) ---
    private void UpdateVisualSelection()
    {
        // 1. Tüm Unitleri Bul (Client tarafında) - İleride optimize edilebilir
        UnitMovement[] allUnits = FindObjectsByType<UnitMovement>(FindObjectsSortMode.None);

        foreach (var unit in allUnits)
        {
            // Sadece benim askerlerim
            if (!unit.isOwned) continue;

            // Modelin üzerindeki Renderer'ı bul (veya Outline scriptini)
            Outline outline = unit.GetComponentInChildren<Outline>();
            
            // Eğer Outline yoksa ekle (Otomatik Setup)
            if (outline == null)
            {
                // Unitte "Model" veya "Mesh" adında bir child yoksa direkt kendine ekleriz
                // Ama genelde Animator'ın olduğu yerdedir.
                Renderer rend = unit.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    outline = rend.gameObject.AddComponent<Outline>();
                    outline.OutlineMode = Outline.Mode.OutlineAll;
                    outline.OutlineColor = Color.green; // Seçili Rengi
                    outline.OutlineWidth = 5f;
                }
            }

            if (outline != null)
            {
                if (unit.SquadIndex == selectedSquadIndex)
                {
                    outline.enabled = true; // Seçili
                }
                else
                {
                    outline.enabled = false; // Seçili Değil
                }
            }
        }
    }

    // --- SHARED MATH (SERVER & CLIENT) ---
    public static List<Vector3> CalculateFormationPoints(Vector3 center, Quaternion rotation, int unitCount, float spacing)
    {
        List<Vector3> points = new List<Vector3>();
        
        // USER REQUEST: "36 asker var 12 12 12 dursunlar"
        // Yani DERİNLİK (Row Count) sabit 3 olacak, GENİŞLİK (Col Count) ona göre artacak.
        int rowCount = 3;
        int unitsPerRow = Mathf.CeilToInt(unitCount / (float)rowCount);
        
        // Güvenlik: En az 1 sütun olsun
        if (unitsPerRow < 1) unitsPerRow = 1;

        for (int i = 0; i < unitCount; i++)
        {
            // X (Genişlik) ve Z (Derinlik)
            int col = i % unitsPerRow;
            int row = i / unitsPerRow;

            float xOffset = col * spacing - ((unitsPerRow * spacing) / 2f);
            float zOffset = row * spacing;

            Vector3 finalPos = center + (rotation * new Vector3(xOffset, 0, -zOffset));
            points.Add(finalPos);
        }
        return points;
    }

    [Command]
    private void CmdMoveUnits(int squadIndex, Vector3 targetPosition, Quaternion formationRotation)
    {
        // Bu grup için takip modunu kapat
        if (followingSquads.Contains(squadIndex))
        {
            followingSquads.Remove(squadIndex);
        }

        myUnits.RemoveAll(u => u == null);
        
        var selectedUnits = myUnits.FindAll(u => u.SquadIndex == squadIndex);
        if (selectedUnits.Count == 0) return;

        // Shared Math ile aynı noktaları hesapla
        List<Vector3> points = CalculateFormationPoints(targetPosition, formationRotation, selectedUnits.Count, 1.1f);
        
        for (int i = 0; i < selectedUnits.Count; i++)
        {
            if (i < points.Count)
            {
                selectedUnits[i].MoveTo(points[i], formationRotation, true);
            }
        }
    }

    [Server]
    public void ServerSetFollowing(int squadIndex, bool state)
    {
        if (state)
        {
            if (!followingSquads.Contains(squadIndex)) followingSquads.Add(squadIndex);
        }
        else
        {
            if (followingSquads.Contains(squadIndex)) followingSquads.Remove(squadIndex);
        }
    }

    [Command]
    private void CmdStartFollowing(int squadIndex)
    {
        if (!followingSquads.Contains(squadIndex))
        {
            followingSquads.Add(squadIndex);
        }
        
        lastFollowUpdate = 0; // Hemen güncelle
    }

    private float lastFollowUpdate;
    private const float FOLLOW_UPDATE_INTERVAL = 0.25f;

    [Server]
    private void UpdateFollowFormation()
    {
        if (Time.time - lastFollowUpdate < FOLLOW_UPDATE_INTERVAL) return;
        lastFollowUpdate = Time.time;
        
        if (followingSquads.Count == 0) return;

        myUnits.RemoveAll(u => u == null);

        Vector3 leaderPos = transform.position;
        Quaternion leaderRot = transform.rotation;
        
        // Sıkı düzen (Dipdibe)
        float spacing = 1.0f;
        float squadGap = 2.0f; // Birlikler arası boşluk
        float currentZOffset = 3.0f; // Oyuncunun ne kadar arkasından başlasın?

        // Her takip eden grup (Squad) için ayrı blok oluştur
        // followingSquads listesindeki sıraya göre dizelim (veya SquadIndex'e göre sort edebiliriz)
        // Şimdilik 0, 1, 2... sırayla dizmek daha mantıklı.
        
        List<int> sortedSquads = new List<int>(followingSquads);
        // sortedSquads.Sort(); // İPTAL: Kullanıcının ekleme sırasına saygı duy (Archers -> Infantry ise öyle kalsın)

        foreach (int squadIndex in sortedSquads)
        {
            var unitsInSquad = myUnits.FindAll(u => u.SquadIndex == squadIndex);
            if (unitsInSquad.Count == 0) continue;

            // 3 SIRA KURALI (Shield Wall: Genişlik artar, Derinlik sabit 3)
            int rowCount = 3;
            int unitsPerRow = Mathf.CeilToInt(unitsInSquad.Count / (float)rowCount);
            if (unitsPerRow < 1) unitsPerRow = 1;

            // Spacing (Sıkı: 0.8f)
            float localSpacing = 0.8f; 
            
            for (int i = 0; i < unitsInSquad.Count; i++)
            {
                int col = i % unitsPerRow;
                int row = i / unitsPerRow;

                float xOffset = col * localSpacing - ((unitsPerRow * localSpacing) / 2f);
                float rowDepth = row * localSpacing;
                
                // Oyuncunun arkasına doğru (Z ekseninde geriye)
                float totalZ = currentZOffset + rowDepth;

                Vector3 targetPos = leaderPos - (leaderRot * Vector3.forward * totalZ) + (leaderRot * Vector3.right * xOffset);
                
                unitsInSquad[i].MoveTo(targetPos);
            }

            // Bir sonraki birlik için offset güncelle
            int totalRows = Mathf.CeilToInt(unitsInSquad.Count / (float)unitsPerRow);
            currentZOffset += (totalRows * localSpacing) + squadGap;
        }
    }

    [Command]
    private void CmdAttackOrder(int squadIndex)
    {
        // Bu grup için takip modunu kapat
        if (followingSquads.Contains(squadIndex))
        {
            followingSquads.Remove(squadIndex);
        }

        myUnits.RemoveAll(u => u == null);
        
        var selectedUnits = myUnits.FindAll(u => u.SquadIndex == squadIndex);

        foreach (UnitMovement unit in selectedUnits)
        {
             unit.StartCharging(); 
        }
    }
}
