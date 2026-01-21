using UnityEngine;
using TMPro;

public class KeyManagement : MonoBehaviour
{
    public TMP_InputField KeyInputField;
    private const string PREFS_KEY = "SavedKey";

    void Awake()
    {
        LoadFromStorage();
    }

    private void LoadFromStorage()
    {
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            Debug.Log("Found key in PLAYER PREFS, loading now...");
            KeyInputField.text = PlayerPrefs.GetString(PREFS_KEY);
        }
    }

    public void SaveToStorage()
    {
        PlayerPrefs.SetString(PREFS_KEY, KeyInputField.text);
        PlayerPrefs.Save();
        Debug.Log("Saved key successfully to PLAYER PREFS!");
    }

    public void PasteFromClipboard()
    {
        KeyInputField.text = GUIUtility.systemCopyBuffer;
        Debug.Log("Pasted successfully from clipboard");
    }
}
