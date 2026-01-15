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


    [Header("Context Mode")]
    public RawImage contextPreviewDisplay;
    public GameObject contextCheckmark;

    [Tooltip("Assign the panel that contains the context input field and confirm button.")]
    public GameObject contextPanel;

    [Tooltip("Assign the TMP InputField where users type additional context.")]
    public TMP_InputField contextInputField;

    [Tooltip("When true, user will be prompted to add context before analysis.")]
    public bool provideMoreContext = false;

    [Header("Arrows")]
    public GameObject arrowsObject;


    [Header("Debug Options")]
    [Tooltip("When enabled, bypasses the AI call and shows 'UI test' in results. Useful for testing UI flow without API calls.")]
    public bool bypassAICall = false;
    public GameObject aiCallCheckmark;
    public int FPS = 60;


    // Pending image data when waiting for context input
    private byte[] pendingImageBytes;

    private float previewMaxWidth;
    private float previewMaxHeight;
    private bool previewBoundsInitialized;

    void Awake()
    {
        Application.targetFrameRate = FPS;
    }

    private void CachePreviewBounds()
    {
        if (previewBoundsInitialized)
            return;

        RectTransform rt = previewDisplay.rectTransform;
        previewMaxWidth = rt.rect.width;
        previewMaxHeight = rt.rect.height;

        previewBoundsInitialized = true;
    }

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

    private bool IsUsageLimitError(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        string errorLower = errorMessage.ToLower();
        
        // Check for common usage/quota limit indicators
        return errorLower.Contains("usage limit") ||
               errorLower.Contains("quota") ||
               errorLower.Contains("rate limit") ||
               errorLower.Contains("exceeded") && (errorLower.Contains("billing") || errorLower.Contains("quota")) ||
               errorLower.Contains("resource exhausted") ||
               errorMessage.Contains("429"); // Response code 429 is rate limit/quota exceeded
    }

    #region Context Mode UI Endpoints

    /// <summary>
    /// Toggle the "Provide More Context" mode. Hook this up to your toggle/checkbox button's OnClick event.
    /// </summary>
    public void OnToggleProvideMoreContext()
    {
        provideMoreContext = !provideMoreContext;
        Debug.Log($"[CameraManager] Provide More Context mode: {(provideMoreContext ? "ON" : "OFF")}");
        contextCheckmark.SetActive(provideMoreContext);
    }

    /// <summary>
    /// Called when user presses "Check with Context" button in the context panel.
    /// Takes the text from the input field and sends it along with the pending image to Gemini.
    /// </summary>
    public void OnContextConfirmClick()
    {
        if (pendingImageBytes == null || pendingImageBytes.Length == 0)
        {
            Debug.LogError("[CameraManager] No pending image to analyze!");
            if (contextPanel != null) contextPanel.SetActive(false);
            initialPanel.SetActive(true);
            return;
        }

        string userContext = "";
        if (contextInputField != null)
        {
            userContext = contextInputField.text;
        }

        if (contextPanel != null) contextPanel.SetActive(false);
        loadingPanel.SetActive(true);

        StartAnalysis(pendingImageBytes, userContext);
        pendingImageBytes = null; // Clear pending data
    }

    /// <summary>
    /// Called when user wants to cancel context input and go back to initial screen.
    /// </summary>
    public void OnContextCancelClick()
    {
        Debug.Log("[CameraManager] Context input cancelled.");
        pendingImageBytes = null;
        if (contextPanel != null) contextPanel.SetActive(false);
        initialPanel.SetActive(true);
    }

    #endregion

    /// <summary>
    /// Starts the Gemini analysis with optional user context appended to the prompt.
    /// </summary>
    private void StartAnalysis(byte[] imageBytes, string userContext)
    {
        string promptOverride = null;

        // If user provided context, append it to the base prompt
        if (!string.IsNullOrWhiteSpace(userContext))
        {
            promptOverride = geminiClient.customPrompt + "\n\nAdditional context from user: " + userContext;
            Debug.Log($"[CameraManager] Analyzing with user context: {userContext}");
        }

        StartCoroutine(geminiClient.AnalyzeImage(imageBytes, (result) => {
            ShowResults(result);
        }, (error) => {
            // Check if this is a usage/quota limit error
            bool isUsageLimitError = IsUsageLimitError(error);
            
            if (isUsageLimitError)
            {
                // Show the image in results panel with usage limit message
                Debug.LogWarning("[CameraManager] API usage limit exceeded. Showing image with error message.");
                loadingPanel.SetActive(false);
                resultsPanel.SetActive(true);
                if (resultText != null)
                {
                    resultText.text = "AI usage limit exceeded, try again later.";
                }
                if (UIManager.instance != null)
                    UIManager.instance.SetTimeSpentText();
            }
            else
            {
                // Regular error handling - return to initial panel
                loadingPanel.SetActive(false);
                initialPanel.SetActive(true);
                if (resultText != null)
                {
                    resultText.text = $"<color=#FF5555><b>Error:</b></color>\n{error}";
                }
                Debug.LogError($"[CameraManager] Analysis failed: {error}");
            }
        }, promptOverride));
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
            
            if (previewDisplay != null && contextPreviewDisplay != null)
            {
                previewDisplay.texture = pickedTex;
                contextPreviewDisplay.texture = pickedTex;
                FitTextureInsideRawImage(pickedTex);
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

            // Debug mode: bypass AI call and show test result
            if (bypassAICall)
            {
                Debug.Log("[CameraManager] Debug mode: Bypassing AI call and showing UI test result.");
                loadingPanel.SetActive(false);
                resultsPanel.SetActive(true);
                if (resultText != null)
                {
                    resultText.text = "UI test";
                }
                if (UIManager.instance != null)
                    UIManager.instance.SetTimeSpentText();
                return;
            }

            // Context mode: show context panel instead of immediately analyzing
            if (provideMoreContext)
            {
                Debug.Log("[CameraManager] Context mode enabled. Showing context panel.");
                pendingImageBytes = imageBytes;
                loadingPanel.SetActive(false);
                
                if (contextPanel != null)
                {
                    contextPanel.SetActive(true);
                    // Clear any previous context text
                    if (contextInputField != null)
                    {
                        contextInputField.text = "";
                    }
                }
                else
                {
                    Debug.LogWarning("[CameraManager] Context panel not assigned! Proceeding without context.");
                    StartAnalysis(imageBytes, null);
                }
                return;
            }

            StartAnalysis(imageBytes, null);
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

    private void FitTextureInsideRawImage(Texture2D tex)
    {
        if (previewDisplay == null || tex == null)
            return;

        CachePreviewBounds();

        RectTransform rt = previewDisplay.rectTransform;
        RectTransform contextRt = contextPreviewDisplay.rectTransform;

        float imageWidth = tex.width;
        float imageHeight = tex.height;

        float scale = Mathf.Min(
            previewMaxWidth / imageWidth,
            previewMaxHeight / imageHeight
        );

        float finalWidth = imageWidth * scale;
        float finalHeight = imageHeight * scale;

        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, finalWidth);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, finalHeight);
        contextRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, finalWidth);
        contextRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, finalHeight);
    }

    private int currentItemIndex = 0;
    private GeminiClient.PredictionResult geminiResult;

    private void ShowResults(GeminiClient.PredictionResult result)
    {
        loadingPanel.SetActive(false);
        resultsPanel.SetActive(true);
        geminiResult = result;

        if (geminiResult != null && geminiResult.items != null && geminiResult.items.Length > 0)
        {
            currentItemIndex = 0;
            ShowItem(currentItemIndex);
        }
        else
        {
            // No items found or result is null
            resultText.text = "<b>Analysis Complete</b>\n\n" +
                             "<color=#55FF55>Error while scanning the image.</color>\n\n" +
                             "Please try again.";
        }

        if(UIManager.instance != null)
            UIManager.instance.SetTimeSpentText();
    }

    public void OnNextItemClick()
    {
        Debug.Log("Next item clicked");
        if(currentItemIndex < geminiResult.items.Length - 1)
        {
            currentItemIndex++;
            ShowItem(currentItemIndex);
        }
        else
        {
            Debug.Log("No more items");
        }
    }

    public void OnPreviousItemClick()
    {
        Debug.Log("Previous item clicked");
        if(currentItemIndex > 0)
        {
            currentItemIndex--;
            ShowItem(currentItemIndex);
        }
        else
        {
            Debug.Log("No previous items");
        }
    }

    private void ShowItem(int index)
    {
        Debug.Log("Showing item " + index);
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        bool hasMoreThanOneItem = geminiResult.TotalItems > 1;
        arrowsObject.SetActive(hasMoreThanOneItem);
        if(hasMoreThanOneItem)
        {
            sb.AppendLine($"<b>{geminiResult.TotalItems} items in image, use the arrows to navigate results.</b>");
            sb.AppendLine($"<b>Total Estimated Animals:</b> {geminiResult.TotalEstimatedAnimalCount:F2}");
            sb.AppendLine();
        }
        
        var item = geminiResult.items[index];
        if (item == null) 
        {
            Debug.Log("Item is null");
            return;
        }

        sb.AppendLine($"<b>--- Item {index + 1}: {item.itemName ?? "Unknown"} ---</b>");
        
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

        // Animal species
        sb.AppendLine();
        if (item.specificMethodOfCreation != null && item.specificMethodOfCreation.Length > 0)
        {
            sb.AppendLine($"<b>How is it made:</b> {string.Join(", ", item.specificMethodOfCreation)}");
        }
        
         resultText.text = sb.ToString();
    }

    public void OnMoreInfoClicked()
    {
        Debug.Log("More info clicked");
    }

    public void OnToggleDebugBypassAICall()
    {
        bypassAICall = !bypassAICall;
        Debug.Log($"AI call bypass: {(bypassAICall ? "ON" : "OFF")}");
        aiCallCheckmark.SetActive(bypassAICall);
    }
}