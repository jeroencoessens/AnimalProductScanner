using UnityEngine;
using TMPro;

public class TimeSpent : MonoBehaviour
{
    public float secondsSpent;
    public TMP_Text timeSpentText;

    void OnEnable()
    {
        secondsSpent = 0;
    }

    void Update()
    {
        secondsSpent += Time.deltaTime;
        timeSpentText.text = $"Time spent: {secondsSpent:F2}s";
    }
}
