using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CameraManager : MonoBehaviour
{
    public GeminiClient geminiClient;
    public GameObject initialPanel, loadingPanel, resultsPanel;
    public TextMeshProUGUI resultText;
    public RawImage previewDisplay;

    public void OnTakeClick()
    {
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Select Image", "", "jpg,png,jpeg");
        if (!string.IsNullOrEmpty(path)) ProcessImage(path);
#else
        if (NativeCamera.IsCameraBusy()) return;
        NativeCamera.TakePicture((path) => {
            if (path != null) ProcessImage(path);
        }, maxSize: 1024);
#endif
    }

    public void OnGalleryClick()
    {
#if UNITY_EDITOR
        OnTakeClick(); 
#else
        if (NativeGallery.IsMediaPickerBusy()) return;
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (path != null) ProcessImage(path);
        }, title: "Select a Food Image", mime: "image/*");
#endif
    }

    private void ProcessImage(string path)
    {
        initialPanel.SetActive(false);
        loadingPanel.SetActive(true);

        // 1. Load the Texture for the UI using the NativeGallery helper (Reliable for iOS)
        // This ensures the preview is shown even if System.IO fails on iOS
        Texture2D pickedTex = NativeGallery.LoadImageAtPath(path, maxSize: 1024, markTextureNonReadable: false);
        
        if (pickedTex != null)
        {
            if (previewDisplay.texture != null) Destroy(previewDisplay.texture);
            previewDisplay.texture = pickedTex;
            
            if (previewDisplay.TryGetComponent<AspectRatioFitter>(out var fitter))
                fitter.aspectRatio = (float)pickedTex.width / pickedTex.height;

            // 2. Convert the loaded texture to bytes for Gemini
            // This is safer than File.ReadAllBytes(path) for iOS gallery files
            byte[] imageBytes = pickedTex.EncodeToJPG();

            StartCoroutine(geminiClient.AnalyzeImage(imageBytes, (result) => {
                ShowResults(result);
            }, (error) => {
                loadingPanel.SetActive(false);
                initialPanel.SetActive(true);
                resultText.text = error;
            }));
        }
        else
        {
            Debug.LogError("Failed to load image at path: " + path);
            loadingPanel.SetActive(false);
            initialPanel.SetActive(true);
        }
    }

    private void ShowResults(GeminiClient.PredictionResult result)
    {
        loadingPanel.SetActive(false);
        resultsPanel.SetActive(true);

        if (result != null)
        {
            string items = (result.animalItems != null) ? string.Join(", ", result.animalItems) : "None";
            string verdictColor = result.containsAnimalProducts ? "#FF5555" : "#55FF55";
            string verdictText = result.containsAnimalProducts ? "NON-VEGAN" : "VEGAN / CLEAN";

            resultText.text = $"<b>Verdict:</b> <color={verdictColor}>{verdictText}</color>\n\n" +
                              $"<b>Items:</b> {items}\n" +
                              $"<b>Est. Animals:</b> {result.estimatedAnimalCount}";
        }

        if(UIManager.instance != null)
            UIManager.instance.SetTimeSpentText();
    }
}