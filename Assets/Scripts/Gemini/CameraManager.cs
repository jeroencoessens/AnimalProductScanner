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

    // --- BUTTON: TAKE NEW PHOTO ---
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

    // --- BUTTON: PICK FROM GALLERY ---
    public void OnGalleryClick()
    {
#if UNITY_EDITOR
        // Same as TakeClick for desktop testing
        OnTakeClick(); 
#else
        if (NativeGallery.IsMediaPickerBusy()) return;

        NativeGallery.Permission permission = NativeGallery.GetImageFromGallery((path) =>
        {
            if (path != null) ProcessImage(path);
        }, title: "Select a Food Image", mime: "image/*");
#endif
    }

    private void ProcessImage(string path)
    {
        initialPanel.SetActive(false);
        loadingPanel.SetActive(true);

        byte[] imageBytes = File.ReadAllBytes(path);
        DisplayImagePreview(imageBytes);

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
        if (previewDisplay.texture != null) Destroy(previewDisplay.texture);
        Texture2D tex = new Texture2D(2, 2);
        if (tex.LoadImage(bytes)) 
        {
            previewDisplay.texture = tex;
            if (previewDisplay.TryGetComponent<AspectRatioFitter>(out var fitter))
                fitter.aspectRatio = (float)tex.width / tex.height;
        }
    }

    private void ShowResults(GeminiClient.PredictionResult result)
    {
        loadingPanel.SetActive(false);
        resultsPanel.SetActive(true);

        if (result != null)
        {
            string items = (result.animalItems != null) ? string.Join(", ", result.animalItems) : "None";
            resultText.text = $"<b>Verdict:</b> {(result.containsAnimalProducts ? "<color=red>Non-Vegan</color>" : "<color=green>Vegan</color>")}\n" +
                              $"<b>Items:</b> {items}\n" +
                              $"<b>Est. Animals:</b> {result.estimatedAnimalCount}";
        }

        UIManager.instance.SetTimeSpentText();
    }
}