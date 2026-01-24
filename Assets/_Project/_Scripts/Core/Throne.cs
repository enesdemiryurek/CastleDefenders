using UnityEngine;

public class Throne : MonoBehaviour
{
    // Singleton instance to find the Throne easily from anywhere
    public static Throne Instance;

    private void Awake()
    {
        Instance = this;
    }
}
