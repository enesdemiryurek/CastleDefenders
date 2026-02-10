using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(OffMeshLink))]
public class SiegeLadder : MonoBehaviour
{
    [Header("Ladder Settings")]
    [SerializeField] private Transform startPoint; // Merdivenin dibi (Yerde)
    [SerializeField] private Transform endPoint;   // Merdivenin tepesi (Surda)
    [SerializeField] private bool biDirectional = true; // Hem inip hem çıkılabilsin mi?

    private OffMeshLink offMeshLink;

    private void Awake()
    {
        UpdateLink();
    }

    private void OnValidate()
    {
        // Editörde noktaları otomatik oluştur (eğer yoksa)
        if (startPoint == null) 
        {
            GameObject s = new GameObject("StartPoint");
            s.transform.SetParent(transform);
            s.transform.localPosition = Vector3.zero;
            startPoint = s.transform;
        }

        if (endPoint == null)
        {
            GameObject e = new GameObject("EndPoint");
            e.transform.SetParent(transform);
            e.transform.localPosition = new Vector3(0, 10f, 2f); // Tahmini yukarıda
            endPoint = e.transform;
        }
        
        // Hata ayıklama için sürekli güncelle (Editörde)
        UpdateLink();
    }

    [ContextMenu("Setup Link")]
    public void UpdateLink()
    {
        if(offMeshLink == null) offMeshLink = GetComponent<OffMeshLink>();
        
        if (offMeshLink != null && startPoint != null && endPoint != null)
        {
            offMeshLink.autoUpdatePositions = false; // Elle atadığımız için false yapıyoruz!
            offMeshLink.startTransform = startPoint;
            offMeshLink.endTransform = endPoint;
            offMeshLink.biDirectional = biDirectional;
            offMeshLink.costOverride = -1; 
            offMeshLink.activated = true;

            // --- NAVMESH VALIDATION ---
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(startPoint.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                Debug.LogError($"[SiegeLadder] ❌ HATA: 'Start Point' (Zemin) NavMesh üzerinde değil! Lütfen Bake al veya noktayı yere yaklaştır.");
            }
            
            if (!NavMesh.SamplePosition(endPoint.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                Debug.LogError($"[SiegeLadder] ❌ HATA: 'End Point' (Sur Tepesi) NavMesh üzerinde değil! SUR DUVARINI 'Static' yapıp BAKE aldın mı?");
            }
            else
            {
                // Başarılıysa linki tam oturtalım
                endPoint.position = hit.position; 
                Debug.Log($"[SiegeLadder] ✅ Merdiven Bağlantısı Başarılı! (OffMeshLink OK)");
            }
        }
    }
    
    private void OnDrawGizmos()
    {
        if (startPoint != null && endPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(startPoint.position, endPoint.position);
            Gizmos.DrawSphere(startPoint.position, 0.3f);
            Gizmos.DrawSphere(endPoint.position, 0.3f);
        }
    }
}
