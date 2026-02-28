using UnityEngine;

/// <summary>
/// Kameranın görmeyeceği uzaklıktaki objeleri gizler
/// Main Camera'ya ekle
/// </summary>
public class CameraCulling : MonoBehaviour
{
    [Header("Culling Settings")]
    [Tooltip("Kameranın göreceği maksimum mesafe")]
    [SerializeField] private float maxRenderDistance = 1000f;
    
    [Tooltip("Küçük objelerin (asker vs.) görüneceği mesafe")]
    [SerializeField] private float smallObjectDistance = 500f;
    
    private Camera cam;

    private void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Sahneden kalma eski küçük ayarları ez (Kasıtlı olarak yükselt/sınırla)
        if (maxRenderDistance <= 500f) maxRenderDistance = 1000f;
        if (smallObjectDistance <= 200f) smallObjectDistance = 500f;

        // Daha kısa görüş mesafesi = daha az render
        cam.farClipPlane = maxRenderDistance;

        // Layer bazlı culling mesafeleri
        // Yakındaki layer'lar daha erken gizlenir
        float[] distances = new float[32];
        
        // Default: tüm layer'lar maxRenderDistance'da gizlenir
        for (int i = 0; i < 32; i++)
        {
            distances[i] = maxRenderDistance;
        }

        // "Unit" ve "Enemy" layer'larını daha yakın mesafede gizle
        int unitLayer = LayerMask.NameToLayer("Unit");
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int effectLayer = LayerMask.NameToLayer("Effects");

        if (unitLayer >= 0) distances[unitLayer] = smallObjectDistance;
        if (enemyLayer >= 0) distances[enemyLayer] = smallObjectDistance;
        if (effectLayer >= 0) distances[effectLayer] = smallObjectDistance;

        cam.layerCullDistances = distances;
        cam.layerCullSpherical = true; // Küresel culling (daha doğal)
        
        Debug.Log($"[CameraCulling] Max render: {maxRenderDistance}m, Small objects: {smallObjectDistance}m");
    }
}
