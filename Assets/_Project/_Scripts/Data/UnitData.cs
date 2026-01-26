using UnityEngine;

[CreateAssetMenu(fileName = "NewUnitData", menuName = "CastleDefenders/Unit Data")]
public class UnitData : ScriptableObject
{
    public string unitName;
    public GameObject unitPrefab;
    public Sprite icon;
    [TextArea] public string description;
    
    [Header("Stats Display")]
    public int health;
    public int damage;
    public float speed;
}
