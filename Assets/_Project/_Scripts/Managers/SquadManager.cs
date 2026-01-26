using UnityEngine;
using System.Collections.Generic;

public class SquadManager : Singleton<SquadManager>
{
    [Header("Configuration")]
    public List<UnitData> allAvailableUnits; // Inspector'da dolduracağın tüm birimler
    
    // Seçilen 3 birliği tutan dizi (Boş olabilir)
    public UnitData[] selectedSquads = new UnitData[3];

    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject); // Sahne değişince yok olmasın (TheKeep -> Game)
    }

    public void SelectUnit(int slotIndex, UnitData unit)
    {
        if (slotIndex >= 0 && slotIndex < 3)
        {
            selectedSquads[slotIndex] = unit;
            Debug.Log($"Slot {slotIndex} set to {unit.unitName}");
        }
    }

    public bool IsReadyToBattle()
    {
        // En az 1 birlik seçili olmalı mı? İsteğe bağlı.
        // Şimdilik en az 1 slot doluysa true dönsün.
        foreach (var unit in selectedSquads)
        {
            if (unit != null) return true;
        }
        return false;
    }
}
