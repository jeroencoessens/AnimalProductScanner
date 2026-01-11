using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;
    public TimeSpent timeSpent;
    public TMP_Text timeSpentText;
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    public void SetTimeSpentText()
    {
        if (!instance) return;
        timeSpentText.text = $"Time spent analyzing: {timeSpent.secondsSpent:F2}s";
    }
}
