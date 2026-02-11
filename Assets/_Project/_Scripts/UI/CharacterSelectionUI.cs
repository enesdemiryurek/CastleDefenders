using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text characterNameText;
    [SerializeField] private TMP_Text characterDescriptionText;
    [SerializeField] private Image characterPreviewImage;
    [SerializeField] private Transform characterListContainer;
    [SerializeField] private GameObject characterButtonPrefab;
    [SerializeField] private Button spawnButton;

    private CustomNetworkManager networkManager;
    private int selectedCharacterIndex = 0;

    private void Start()
    {
        networkManager = NetworkManager.singleton as CustomNetworkManager;
        if (networkManager == null)
        {
            Debug.LogError("CustomNetworkManager not found!");
            return;
        }

        PopulateCharacterList();
        UpdateCharacterInfo(0); // Default selection
        
        spawnButton.onClick.AddListener(ConfirmSelection);
    }

    private void PopulateCharacterList()
    {
        // Clear existing buttons
        foreach (Transform child in characterListContainer)
        {
            Destroy(child.gameObject);
        }

        // Create new buttons
        for (int i = 0; i < networkManager.characters.Count; i++)
        {
            int index = i; // Closure capture fix
            CharacterData data = networkManager.characters[i];
            
            GameObject btnObj = Instantiate(characterButtonPrefab, characterListContainer);
            Button btn = btnObj.GetComponent<Button>();
            
            // Setup Button Text/Icon if available in prefab
            TMP_Text btnText = btnObj.GetComponentInChildren<TMP_Text>();
            if (btnText != null) btnText.text = data.characterName;
            
            Image btnIcon = btnObj.transform.Find("Icon")?.GetComponent<Image>();
            if (btnIcon != null) btnIcon.sprite = data.icon;

            // Add click listener
            btn.onClick.AddListener(() =>
            {
                selectedCharacterIndex = index;
                UpdateCharacterInfo(index);
            });
        }
    }

    private void UpdateCharacterInfo(int index)
    {
        if (index < 0 || index >= networkManager.characters.Count) return;

        CharacterData data = networkManager.characters[index];
        
        if (characterNameText != null) characterNameText.text = data.characterName;
        if (characterDescriptionText != null) characterDescriptionText.text = data.description;
        if (characterPreviewImage != null) characterPreviewImage.sprite = data.icon;
    }

    private void ConfirmSelection()
    {
        // Send message to server to spawn character
        CustomNetworkManager.CharacterMessage msg = new CustomNetworkManager.CharacterMessage
        {
            characterIndex = selectedCharacterIndex
        };

        NetworkClient.Send(msg);
        
        // Hide UI after selection (Optional, depending on game flow)
        gameObject.SetActive(false);
    }
}
