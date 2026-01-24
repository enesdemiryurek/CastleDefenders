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
    private readonly List<UnitMovement> myUnits = new List<UnitMovement>();
    
    // SyncList to track which squad indices are currently in "Follow Mode"
    // Valid indices: 0, 1, 2...
    private readonly SyncList<int> followingSquads = new SyncList<int>();

    private int selectedSquadIndex = 0; // Client-side selection

    [Header("UI Feedback")]
    [SerializeField] private bool showSelectionLog = true;

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
        }

        // SERVER SIDE LOGIC (Continuous Follow)
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
        
        // 'X' -> Belirlenen yere git ve dizil
        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            PerformRaycastAndCommand_Move();
        }

        // 'C' -> Beni takip et (Modu açar)
        if (Keyboard.current.cKey.wasPressedThisFrame)
        {
            CmdStartFollowing(selectedSquadIndex);
        }

        // 'V' -> Serbest Saldırı (Takibi bırakır)
        if (Keyboard.current.vKey.wasPressedThisFrame)
        {
            CmdAttackOrder(selectedSquadIndex);
        }
    }

    public event System.Action<int> OnSquadSelected;

    private void ChangeSelection(int index)
    {
        selectedSquadIndex = index;
        if (showSelectionLog) Debug.Log($"Seçili Birlik: {index + 1}. Takım");
        
        OnSquadSelected?.Invoke(index);
    }

    private void PerformRaycastAndCommand_Move()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, groundLayer))
        {
             // Debug: Nereye tıkladık?
            Debug.DrawLine(ray.origin, hit.point, Color.red, 2.0f);
            
            CmdMoveUnits(selectedSquadIndex, hit.point, transform.rotation);
        }
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
        
        int i = 0;
        int rows = 5;
        float spacing = 2f;

        // Sadece seçili takımı filtrele
        var selectedUnits = myUnits.FindAll(u => u.SquadIndex == squadIndex);

        foreach (UnitMovement unit in selectedUnits)
        {
            if (unit == null) continue;
            
            // X ve Z hesabı
            float xOffset = (i % rows) * spacing - (rows * spacing / 2f);
            float zOffset = (i / rows) * spacing;

            Vector3 finalPos = targetPosition + (formationRotation * new Vector3(xOffset, 0, -zOffset));
            
            unit.MoveTo(finalPos);
            i++;
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

        // Her takip eden grup için ayrı ayrı işlem yapabiliriz ama
        // basitleştirmek için hepsini arkamıza dizelim.
        // Gelişmiş versiyonda: Squad 1 sağa, Squad 2 sola gibi offsetler verilebilir.
        // Şimdilik: Hepsini tek bir ordu gibi arkaya diziyor ama SADECE LISTEDE OLANLARI.

        int i = 0;
        int rows = 5;
        float spacing = 2f;

        foreach (UnitMovement unit in myUnits)
        {
            if (unit == null) continue;
            
            // Eğer bu unit'in mensup olduğu squad takip listesinde YOKSA, dokunma.
            if (!followingSquads.Contains(unit.SquadIndex)) continue;

            float xOffset = (i % rows) * spacing - (rows * spacing / 2f);
            float zOffset = (i / rows) * spacing + 3f;
            
            Vector3 targetPos = leaderPos - (leaderRot * Vector3.forward * zOffset) + (leaderRot * Vector3.right * xOffset);
            
            unit.MoveTo(targetPos);
            i++;
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
