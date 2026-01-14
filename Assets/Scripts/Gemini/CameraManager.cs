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
        }, title: "Select an Image", mime: "image/*");
#endif
    }

    private void ProcessImage(string path)
    {
        if (geminiClient == null)
        {
            Debug.LogError("[CameraManager] GeminiClient is not assigned!");
            return;
        }

        initialPanel.SetActive(false);
        loadingPanel.SetActive(true);

        // 1. Load the Texture for the UI using the NativeGallery helper (Reliable for iOS)
        // This ensures the preview is shown even if System.IO fails on iOS
        Texture2D pickedTex = NativeGallery.LoadImageAtPath(path, maxSize: 1024, markTextureNonReadable: false);
        
        if (pickedTex != null)
        {
            // Clean up previous texture
            if (previewDisplay != null && previewDisplay.texture != null)
            {
                Destroy(previewDisplay.texture);
            }
            
            if (previewDisplay != null)
            {
                previewDisplay.texture = pickedTex;
                
                if (previewDisplay.TryGetComponent<AspectRatioFitter>(out var fitter))
                    fitter.aspectRatio = (float)pickedTex.width / pickedTex.height;
            }

            // 2. Convert the loaded texture to bytes for Gemini
            // This is safer than File.ReadAllBytes(path) for iOS gallery files
            byte[] imageBytes = pickedTex.EncodeToJPG();
            
            if (imageBytes == null || imageBytes.Length == 0)
            {
                Debug.LogError("[CameraManager] Failed to encode image to JPG");
                loadingPanel.SetActive(false);
                initialPanel.SetActive(true);
                if (resultText != null)
                {
                    resultText.text = "Error: Failed to process image. Please try again.";
                }
                return;
            }

            StartCoroutine(geminiClient.AnalyzeImage(imageBytes, (result) => {
                ShowResults(result);
            }, (error) => {
                loadingPanel.SetActive(false);
                initialPanel.SetActive(true);
                if (resultText != null)
                {
                    resultText.text = $"<color=#FF5555><b>Error:</b></color>\n{error}";
                }
                Debug.LogError($"[CameraManager] Analysis failed: {error}");
            }));
        }
        else
        {
            Debug.LogError($"[CameraManager] Failed to load image at path: {path}");
            loadingPanel.SetActive(false);
            initialPanel.SetActive(true);
            if (resultText != null)
            {
                resultText.text = "Error: Could not load the selected image. Please try another image.";
            }
        }
    }

    private void ShowResults(GeminiClient.PredictionResult result)
    {
        loadingPanel.SetActive(false);
        resultsPanel.SetActive(true);

        if (result != null && result.items != null && result.items.Length > 0)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            
            // Determine overall verdict
            bool hasAnimalProducts = result.ContainsAnimalProducts;
            string verdictColor = hasAnimalProducts ? "#FF5555" : "#55FF55";
            string verdictText = hasAnimalProducts ? "NON-VEGAN" : "VEGAN / CLEAN";
            
            sb.AppendLine($"<b>Overall Verdict:</b> <color={verdictColor}>{verdictText}</color>");
            sb.AppendLine($"<b>Items Found:</b> {result.TotalItems}");
            sb.AppendLine($"<b>Total Estimated Animals:</b> {result.TotalEstimatedAnimalCount:F2}");
            sb.AppendLine();

            // Display each item's details
            for (int i = 0; i < result.items.Length; i++)
            {
                var item = result.items[i];
                if (item == null) continue;

                sb.AppendLine($"<b>━━━ Item {i + 1}: {item.itemName ?? "Unknown"} ━━━</b>");
                
                // Animal-derived materials
                if (item.animalDerivedMaterials != null && item.animalDerivedMaterials.Length > 0)
                {
                    sb.AppendLine($"<b>Materials:</b> {string.Join(", ", item.animalDerivedMaterials)}");
                }
                else
                {
                    sb.AppendLine($"<b>Materials:</b> <color=#55FF55>No animal-derived materials detected</color>");
                }

                // Animal species
                if (item.animalSpecies != null && item.animalSpecies.Length > 0)
                {
                    sb.AppendLine($"<b>Species:</b> {string.Join(", ", item.animalSpecies)}");
                }

                // Animal count
                if (item.estimatedAnimalCount > 0)
                {
                    sb.AppendLine($"<b>Animals Used:</b> {item.estimatedAnimalCount:F2}");
                }

                // Confidence
                string confidenceColor = item.confidence?.ToLower() switch
                {
                    "high" => "#55FF55",
                    "medium" => "#FFAA55",
                    "low" => "#FF5555",
                    _ => "#AAAAAA"
                };
                sb.AppendLine($"<b>Confidence:</b> <color={confidenceColor}>{item.confidence?.ToUpper() ?? "UNKNOWN"}</color>");
                
                if (i < result.items.Length - 1)
                {
                    sb.AppendLine();
                }
            }
            
            resultText.text = sb.ToString();
        }
        else
        {
            // No items found or result is null
            resultText.text = "<b>Analysis Complete</b>\n\n" +
                             "<color=#55FF55>No clothing or footwear items detected in the image.</color>\n\n" +
                             "Please try scanning an image with visible clothing or footwear items.";
        }

        if(UIManager.instance != null)
            UIManager.instance.SetTimeSpentText();
    }
}