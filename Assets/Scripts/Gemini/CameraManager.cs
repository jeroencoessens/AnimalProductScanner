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
        // Desktop logic using Unity Editor tools
        string path = EditorUtility.OpenFilePanel("Select Image to Test", "", "jpg,png,jpeg");
        if (!string.IsNullOrEmpty(path)) ProcessImage(path);
#else
        // Mobile logic using NativeCamera
        if (NativeCamera.IsCameraBusy()) return;

        // TakePicture returns void. The result is handled in the callback (path) => { ... }
        NativeCamera.TakePicture((path) =>
        {
            if (path != null)
            {
                ProcessImage(path);
            }
            else
            {
                Debug.Log("User cancelled camera or no image captured.");
            }
        }, maxSize: 1024); // Limit size for faster AI upload
#endif
    }

    private void ProcessImage(string path)
    {
        initialPanel.SetActive(false);
        loadingPanel.SetActive(true);

        byte[] imageBytes = File.ReadAllBytes(path);

        // Display locally for user review
        DisplayImagePreview(imageBytes);

        // Upload to Gemini
        StartCoroutine(geminiClient.AnalyzeImage(imageBytes, (result) => {
            ShowResults(result);
        }, (error) => {
            loadingPanel.SetActive(false);
            initialPanel.SetActive(true);
            resultText.text = error;
        }));
    }

    private void DisplayImagePreview(byte[] bytes)
    {
        // Cleanup old texture to save memory
        if (previewDisplay.texture != null) Destroy(previewDisplay.texture);

        Texture2D tex = new Texture2D(2, 2);
        if (tex.LoadImage(bytes)) 
        {
            previewDisplay.texture = tex;
            if (previewDisplay.TryGetComponent<AspectRatioFitter>(out var fitter))
            {
                fitter.aspectRatio = (float)tex.width / tex.height;
            }
        }
    }

    private void ShowResults(GeminiClient.PredictionResult result)
    {
        loadingPanel.SetActive(false);
        resultsPanel.SetActive(true);

        if (result != null)
        {
            string itemsList = (result.animalItems != null && result.animalItems.Length > 0) 
                ? string.Join(", ", result.animalItems) 
                : "None detected";

            resultText.text = $"<b>Verdict:</b> {(result.containsAnimalProducts ? "<color=red>Animal Products Found</color>" : "<color=green>No Animal Products</color>")}\n\n" +
                              $"<b>Items:</b> {itemsList}\n" +
                              $"<b>Est. Animals:</b> {result.estimatedAnimalCount}";
        }
    }
}