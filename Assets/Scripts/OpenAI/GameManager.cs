using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public CameraController cameraController;
    public OpenAIController openAIController;
    public UIController uiController;

    private void Awake()
    {
        Instance = this;
    }
}