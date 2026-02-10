using UnityEngine;
using Mirror;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "CastleDefenders/Character Data")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public Sprite icon;
    public NetworkIdentity characterPrefab; // NetworkIdentity olmak zorunda (Spawn için)
    [TextArea] public string description;
}
