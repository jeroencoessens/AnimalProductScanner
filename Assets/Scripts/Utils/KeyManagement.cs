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
            KeyInputField.text = PlayerPrefs.GetString(PREFS_KEY);
        }
    }

    public void SaveToStorage()
    {
        PlayerPrefs.SetString(PREFS_KEY, KeyInputField.text);
        PlayerPrefs.Save();
    }
}
