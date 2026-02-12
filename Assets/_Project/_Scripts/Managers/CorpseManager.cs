using UnityEngine;
using System.Collections.Generic;
using Mirror;

public class CorpseManager : MonoBehaviour
{
    public static CorpseManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<CorpseManager>();
                if (_instance == null)
                {
                    GameObject obj = new GameObject("CorpseManager");
                    _instance = obj.AddComponent<CorpseManager>();
                }
            }
            return _instance;
        }
    }
    private static CorpseManager _instance;

    [Header("Settings")]
    [SerializeField] private int maxCorpses = 30;

    private Queue<GameObject> corpseQueue = new Queue<GameObject>();

    private void Awake()
    {
        if (_instance == null) _instance = this;
        else if (_instance != this) Destroy(gameObject);
        
        DontDestroyOnLoad(gameObject);
    }

    public void RegisterCorpse(GameObject corpse)
    {
        corpseQueue.Enqueue(corpse);

        if (corpseQueue.Count > maxCorpses)
        {
            GameObject oldest = corpseQueue.Dequeue();
            if (oldest != null)
            {
                if (NetworkServer.active) NetworkServer.Destroy(oldest);
                else Destroy(oldest);
            }
        }
    }
}
