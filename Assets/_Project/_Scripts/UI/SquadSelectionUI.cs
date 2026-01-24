using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Mirror;

[RequireComponent(typeof(CanvasGroup))]
public class SquadSelectionUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Transform iconContainer;
    [SerializeField] private GameObject squadIconPrefab; // Prefab: Image(Frame) -> Text
    
    [Header("Visual Settings")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.84f, 0f, 1f); // Altın
    [SerializeField] private Color unselectedColor = new Color(0.3f, 0.3f, 0.3f, 1f); // Koyu Gri
    [SerializeField] private float selectedScale = 1.2f;
    [SerializeField] private float displayDuration = 3.0f; // 3 saniye sonra kaybol

    private List<Image> spawnedIcons = new List<Image>();
    private PlayerUnitCommander commander;
    private CanvasGroup canvasGroup;
    private float currentDisplayTimer = 0f;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f; // Başlangıçta gizli
    }

    private void Start()
    {
        FindCommander();
    }

    private void FindCommander()
    {
        if (commander == null && NetworkClient.localPlayer != null)
        {
            var p = NetworkClient.localPlayer.GetComponent<PlayerUnitCommander>();
            if (p != null)
            {
                commander = p;
                InitializeUI();
                commander.OnSquadSelected += ShowAndSelect;
                Debug.Log("Squad UI Connected to Commander");
            }
        }
    }

    private void Update()
    {
        if (commander == null) FindCommander();

        // Timer Logic
        if (currentDisplayTimer > 0)
        {
            currentDisplayTimer -= Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 1f, Time.deltaTime * 10f); // Fade In
        }
        else
        {
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 0f, Time.deltaTime * 5f); // Fade Out
        }
    }

    private void InitializeUI()
    {
        foreach (Transform child in iconContainer) Destroy(child.gameObject);
        spawnedIcons.Clear();

        for (int i = 0; i < 3; i++)
        {
            GameObject newIcon = Instantiate(squadIconPrefab, iconContainer);
            
            Text textComp = newIcon.GetComponentInChildren<Text>();
            if (textComp != null) textComp.text = (i + 1).ToString();

            Image img = newIcon.GetComponent<Image>();
            if (img != null)
            {
                spawnedIcons.Add(img);
                img.color = unselectedColor; // Başlangıçta hepsi sönük
            }
        }
    }

    private void ShowAndSelect(int selectedIndex)
    {
        // 1. Timer'ı resetle (Görünür yap)
        currentDisplayTimer = displayDuration;

        // 2. Görselleri güncelle
        for (int i = 0; i < spawnedIcons.Count; i++)
        {
            if (spawnedIcons[i] == null) continue;
            
            bool isSelected = (i == selectedIndex);

            // Renk
            spawnedIcons[i].color = isSelected ? selectedColor : unselectedColor;

            // Büyüme (Scale)
            float targetScale = isSelected ? selectedScale : 1.0f;
            spawnedIcons[i].rectTransform.localScale = Vector3.one * targetScale;
        }
    }

    private void OnDestroy()
    {
        if (commander != null)
        {
            commander.OnSquadSelected -= ShowAndSelect;
        }
    }
}
