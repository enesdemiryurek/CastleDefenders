using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class SquadSelectionScreenUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject selectionPanel; // Tüm panel (J ile aç/kapa)
    [SerializeField] private Transform availableUnitsContainer; // Sol taraf (Seçenekler)
    [SerializeField] private Transform selectedSlotsContainer; // Sağ taraf (3 Slot)
    
    [Header("Prefabs")]
    [SerializeField] private GameObject unitCardPrefab; // Sol taraftaki kartlar
    [SerializeField] private GameObject slotPrefab; // Sağ taraftaki slotlar

    private bool isVisible = false;

    private void Start()
    {
        // Başlangıçta gizle
        if (selectionPanel != null) selectionPanel.SetActive(false);

        // UI'yı oluştur
        GenerateUI();
    }

    private void Update()
    {
        // J tuşu ile aç/kapa
        if (Input.GetKeyDown(KeyCode.J))
        {
            ToggleVisibility();
        }
    }

    private void ToggleVisibility()
    {
        isVisible = !isVisible;
        if (selectionPanel != null) selectionPanel.SetActive(isVisible);
        
        // Mouse kontrolü
        Cursor.visible = isVisible;
        Cursor.lockState = isVisible ? CursorLockMode.None : CursorLockMode.Locked;
    }

    private void GenerateUI()
    {
        if (SquadManager.Instance == null) return;

        // 1. Mevcut Unitleri Listele (Sol Taraf)
        foreach (Transform child in availableUnitsContainer) Destroy(child.gameObject);

        foreach (var unit in SquadManager.Instance.allAvailableUnits)
        {
            GameObject card = Instantiate(unitCardPrefab, availableUnitsContainer);
            
            // Text ve Icon ayarla
            var texts = card.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0) texts[0].text = unit.unitName; // İsim
            if (texts.Length > 1) texts[1].text = $"HP: {unit.health} DMG: {unit.damage}"; // Statlar

            Image iconImg = card.GetComponentInChildren<Image>();
            if (iconImg != null) iconImg.sprite = unit.icon;

            // Tıklama Eventi
            Button btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnUnitCardClicked(unit));
            }
        }

        RefreshSelectedSlots();
    }

    private void OnUnitCardClicked(UnitData unit)
    {
        // Boş slot bul ve yerleştir
        for (int i = 0; i < 3; i++)
        {
            if (SquadManager.Instance.selectedSquads[i] == null)
            {
                SquadManager.Instance.SelectUnit(i, unit);
                RefreshSelectedSlots();
                return;
            }
        }
        
        Debug.Log("Slots Full! Remove one first.");
    }

    private void RefreshSelectedSlots()
    {
        // Sağ Tarafı Güncelle
        foreach (Transform child in selectedSlotsContainer) Destroy(child.gameObject);

        for (int i = 0; i < 3; i++)
        {
            GameObject slot = Instantiate(slotPrefab, selectedSlotsContainer);
            UnitData data = SquadManager.Instance.selectedSquads[i];

            if (data != null)
            {
                // Dolu Slot
                var texts = slot.GetComponentsInChildren<TextMeshProUGUI>();
                if (texts.Length > 0) texts[0].text = data.unitName;

                Image iconImg = slot.GetComponentInChildren<Image>();
                if (iconImg != null) iconImg.sprite = data.icon;

                // Kaldırma Butonu (X)
                Button btn = slot.GetComponent<Button>();
                if (btn != null)
                {
                    int index = i; // Closure capture fix
                    btn.onClick.AddListener(() => OnSlotClicked(index));
                }
            }
            else
            {
                // Boş Slot
                var texts = slot.GetComponentsInChildren<TextMeshProUGUI>();
                if (texts.Length > 0) texts[0].text = "Empty";
            }
        }
    }

    private void OnSlotClicked(int index)
    {
        // Slota tıklayınca sil (Remove)
        SquadManager.Instance.SelectUnit(index, null);
        RefreshSelectedSlots();
    }
}
