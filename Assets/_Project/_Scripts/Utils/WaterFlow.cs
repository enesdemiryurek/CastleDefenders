using UnityEngine;

public class WaterFlow : MonoBehaviour
{
    public float speedX = 0.1f;
    public float speedY = 0.1f;
    private Material waterMat;

    void Start() { waterMat = GetComponent<Renderer>().material; }

    void Update()
    {
        Vector2 offset = waterMat.GetTextureOffset("_BaseMap");
        offset.x += speedX * Time.deltaTime;
        offset.y += speedY * Time.deltaTime;
        waterMat.SetTextureOffset("_BaseMap", offset);
        waterMat.SetTextureOffset("_BumpMap", offset); // Normal map'i de kaydırır
    }
}