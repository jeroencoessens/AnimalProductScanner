using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class GeminiClient : MonoBehaviour
{
    [Header("API Configuration")]
    public string apiKey = "YOUR_API_KEY_HERE";
    public string modelId = "gemini-1.5-flash"; // Valid Gemini model name

    [Header("Prompt Configuration")]
    [TextArea(3, 10)]
    public string customPrompt = "Analyze the image.\n\nIdentify visible clothing or footwear items in the image.\n\nFor each item:\n- Identify likely animal-derived materials\n- Identify animal species involved\n- Estimate number of animals used (fractions allowed)\n- Assign confidence (low, medium, high)\n\nReturn a JSON object with these identifications";

    [Header("Debug Options")]
    public bool enableDebugLogging = true;
    public bool logFullApiResponse = false;
    public bool validateApiKeyOnStart = true;

    [Header("UI References")]
    public TMP_Text loadingDebugText;
    public TMP_Text resultText;

    private string BaseUrl => $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

    // --- These classes match the EXACT structure Gemini returns ---
    [Serializable]
    public class ItemAnalysis
    {
        public string itemName;
        public string[] animalDerivedMaterials;
        public string[] animalSpecies;
        public float estimatedAnimalCount; // Using float to support fractions
        public string confidence; // "low", "medium", "high"
        public string specificMethodOfCreation; // "wild caught", "farm raised", "other"
    }

    [Serializable]
    public class PredictionResult // Your specific data
    {
        public ItemAnalysis[] items;
        
        // Helper properties for backward compatibility and quick checks
        public bool ContainsAnimalProducts => items != null && items.Length > 0;
        public int TotalItems => items != null ? items.Length : 0;
        public float TotalEstimatedAnimalCount
        {
            get
            {
                if (items == null) return 0f;
                float total = 0f;
                foreach (var item in items)
                {
                    if (item != null) total += item.estimatedAnimalCount;
                }
                return total;
            }
        }
    }

    [Serializable]
    public class GeminiResponse
    {
        public Candidate[] candidates;
    }

    [Serializable]
    public class Candidate
    {
        public Content content;
    }

    [Serializable]
    public class Content
    {
        public Part[] parts;
    }

    [Serializable]
    public class Part
    {
        public string text;
    }

    void Start()
    {
        if (validateApiKeyOnStart)
        {
            ValidateApiKey();
        }
    }

    /// <summary>
    /// Validates the API key format and logs warnings if invalid
    /// </summary>
    public void ValidateApiKey()
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
        {
            Debug.LogWarning("[GeminiClient] API Key is not set! Please set your Gemini API key in the GeminiClient component.");
            return;
        }

        if (apiKey.Length < 20)
        {
            Debug.LogWarning("[GeminiClient] API Key appears to be too short. Please verify it's correct.");
        }
        else
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[GeminiClient] API Key validated (length: {apiKey.Length}, starts with: {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...)");
            }
        }
    }

    /// <summary>
    /// Tests the API key by making a simple request to the Gemini API
    /// </summary>
    public IEnumerator TestApiKey(Action<bool> onResult, Action<string> onError = null)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
        {
            string errorMsg = "API Key is not set.";
            Debug.LogError($"[GeminiClient] {errorMsg}");
            onError?.Invoke(errorMsg);
            onResult?.Invoke(false);
            yield break;
        }

        Debug.Log("[GeminiClient] Testing API key...");
        UpdateStatus("Testing API Key...");
        
        // Create a minimal test request
        string testPayload = @"{
            ""contents"": [{
                ""parts"": [{""text"": ""Say 'test successful' if you can read this.""}]
            }]
        }";

        string testUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

        using (UnityWebRequest request = new UnityWebRequest(testUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(testPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[GeminiClient] ✓ API Key test successful!");
                UpdateStatus("API Key Valid!");
                onResult?.Invoke(true);
            }
            else
            {
                string errorMsg = $"API Key test failed: {request.error}\nResponse Code: {request.responseCode}\nResponse: {request.downloadHandler.text}";
                Debug.LogError($"[GeminiClient] ✗ {errorMsg}");
                ReportError(errorMsg);
                onError?.Invoke(errorMsg);
                onResult?.Invoke(false);
            }
        }
    }

    public IEnumerator AnalyzeImage(byte[] imageBytes, Action<PredictionResult> onParsedResult, Action<string> onError, string promptOverride = null)
    {
        UpdateStatus("Initializing analysis...");

        // Validate API key
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
        {
            string errorMsg = "API Key is not set. Please set your Gemini API key in the GeminiClient component.";
            Debug.LogError($"[GeminiClient] {errorMsg}");
            ReportError(errorMsg);
            onError?.Invoke(errorMsg);
            yield break;
        }

        // Validate model ID
        if (string.IsNullOrEmpty(modelId))
        {
            string errorMsg = "Model ID is not set.";
            Debug.LogError($"[GeminiClient] {errorMsg}");
            ReportError(errorMsg);
            onError?.Invoke(errorMsg);
            yield break;
        }

        UpdateStatus("Preparing image...");
        string base64Image = Convert.ToBase64String(imageBytes);
        if (enableDebugLogging)
        {
            Debug.Log($"[GeminiClient] Starting analysis - Model: {modelId}, Image size: {imageBytes.Length} bytes ({imageBytes.Length / 1024f:F2} KB)");
        }

        string promptToUse = string.IsNullOrWhiteSpace(promptOverride) ? customPrompt : promptOverride;

        // GenerationConfig forces the model to be a robot and return JSON
        string jsonPayload = @"{
            ""contents"": [{
                ""parts"": [
                    {""text"": """ + promptToUse + @"""},
                    {""inline_data"": {""mime_type"": ""image/jpeg"", ""data"": """ + base64Image + @"""}}
                ]
            }],
            ""generationConfig"": {
                ""response_mime_type"": ""application/json"",
                ""response_schema"": {
                    ""type"": ""OBJECT"",
                    ""properties"": {
                        ""items"": {
                            ""type"": ""ARRAY"",
                            ""items"": {
                                ""type"": ""OBJECT"",
                                ""properties"": {
                                    ""itemName"": {""type"": ""STRING""},
                                    ""animalDerivedMaterials"": {""type"": ""ARRAY"", ""items"": {""type"": ""STRING""}},
                                    ""animalSpecies"": {""type"": ""ARRAY"", ""items"": {""type"": ""STRING""}},
                                    ""estimatedAnimalCount"": {""type"": ""NUMBER""},
                                    ""confidence"": {""type"": ""STRING""},
                                    ""specificMethodOfCreation"": {""type"": ""STRING""}
                                },
                                ""required"": [""itemName"", ""confidence""]
                            }
                        }
                    }
                }
            }
        }";

        using (UnityWebRequest request = new UnityWebRequest(BaseUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            UpdateStatus("Sending request to Gemini...");
            if (enableDebugLogging)
            {
                Debug.Log("[GeminiClient] Sending request to Gemini API...");
            }
            
            float startTime = Time.realtimeSinceStartup;

            // Start the fake loading messages
            Coroutine loadingRoutine = StartCoroutine(CycleLoadingMessages());

            yield return request.SendWebRequest();

            // Stop the fake loading messages
            if (loadingRoutine != null) StopCoroutine(loadingRoutine);

            float requestTime = Time.realtimeSinceStartup - startTime;

            if (request.result == UnityWebRequest.Result.Success)
            {
                UpdateStatus("Processing response...");
                if (enableDebugLogging)
                {
                    Debug.Log($"[GeminiClient] API request successful (took {requestTime:F2}s), parsing response...");
                }
                
                string responseText = request.downloadHandler.text;
                
                if (logFullApiResponse)
                {
                    Debug.Log($"[GeminiClient] Full API Response:\n{responseText}");
                }
                else if (enableDebugLogging)
                {
                    Debug.Log($"[GeminiClient] Response preview: {responseText.Substring(0, Math.Min(500, responseText.Length))}...");
                }
                
                try {
                    UpdateStatus("Parsing results...");
                    // 1. Parse the whole API wrapper
                    GeminiResponse fullResponse = JsonUtility.FromJson<GeminiResponse>(responseText);
                    
                    // 2. Validate response structure
                    if (fullResponse == null)
                    {
                        throw new Exception("Failed to parse API response - response is null");
                    }
                    
                    if (fullResponse.candidates == null || fullResponse.candidates.Length == 0)
                    {
                        throw new Exception("API response has no candidates. Full response: " + responseText);
                    }
                    
                    if (fullResponse.candidates[0].content == null)
                    {
                        throw new Exception("API response candidate has no content");
                    }
                    
                    if (fullResponse.candidates[0].content.parts == null || fullResponse.candidates[0].content.parts.Length == 0)
                    {
                        throw new Exception("API response candidate has no parts");
                    }
                    
                    if (string.IsNullOrEmpty(fullResponse.candidates[0].content.parts[0].text))
                    {
                        throw new Exception("API response part has no text content");
                    }
                    
                    // 3. Extract the 'text' string which contains YOUR JSON
                    string innerJson = fullResponse.candidates[0].content.parts[0].text;
                    
                    if (logFullApiResponse || enableDebugLogging)
                    {
                        Debug.Log($"[GeminiClient] Extracted inner JSON: {innerJson}");
                    }
                    
                    // 4. Parse YOUR JSON into your result class
                    PredictionResult result = JsonUtility.FromJson<PredictionResult>(innerJson);
                    
                    if (result == null)
                    {
                        throw new Exception("Failed to parse inner JSON into PredictionResult");
                    }
                    
                    // Validate parsed result
                    if (result.items == null)
                    {
                        Debug.LogWarning("[GeminiClient] Result parsed but items array is null. This might be expected if no items were found.");
                        result.items = new ItemAnalysis[0];
                    }
                    else if (enableDebugLogging)
                    {
                        Debug.Log($"[GeminiClient] Successfully parsed result! Found {result.items.Length} item(s)");
                        foreach (var item in result.items)
                        {
                            if (item != null)
                            {
                                Debug.Log($"[GeminiClient]   - {item.itemName}: {item.animalDerivedMaterials?.Length ?? 0} materials, {item.estimatedAnimalCount} animals, confidence: {item.confidence}");
                            }
                        }
                    }
                    
                    UpdateStatus("Analysis complete!");
                    onParsedResult?.Invoke(result);
                }
                catch (Exception e) {
                    string errorMsg = $"Parse Error: {e.Message}\n\nResponse text: {request.downloadHandler.text}";
                    Debug.LogError($"[GeminiClient] {errorMsg}");
                    Debug.LogException(e);
                    ReportError(errorMsg);
                    onError?.Invoke(errorMsg);
                }
            }
            else
            {
                string errorMsg = $"API Error: {request.error}\nResponse Code: {request.responseCode}\nResponse: {request.downloadHandler.text}";
                Debug.LogError($"[GeminiClient] {errorMsg}");
                
                // Provide helpful error messages for common issues
                if (request.responseCode == 400)
                {
                    errorMsg += "\n\nTip: This might be due to an invalid API key, model name, or request format.";
                }
                else if (request.responseCode == 401 || request.responseCode == 403)
                {
                    errorMsg += "\n\nTip: Your API key might be invalid or expired. Please check your Google Cloud Console.";
                }
                else if (request.responseCode == 429)
                {
                    errorMsg += "\n\nTip: Rate limit exceeded. Please wait a moment and try again.";
                }
                
                ReportError(errorMsg);
                onError?.Invoke(errorMsg);
            }
        }
    }

    private IEnumerator CycleLoadingMessages()
    {
        string[] messages = {
            "Analyzing image pixels...",
            "Identifying objects...",
            "Detecting materials...",
            "Consulting database...",
            "Formulating responses...",
            "Summarizing results...",
            "Preparing user interface...",
            "Finalizing analysis..."
        };

        int index = 0;
        UpdateStatus("Sending request to Gemini...");
        yield return new WaitForSeconds(2.5f);

        while (index < messages.Length)
        {
            UpdateStatus(messages[index]);
            index = (index + 1) % messages.Length;
            yield return new WaitForSeconds(2f);
        }
    }

    private void UpdateStatus(string status)
    {
        if (loadingDebugText != null)
        {
            loadingDebugText.text = status;
        }
    }

    private void ReportError(string error)
    {
        if (resultText != null)
        {
            resultText.text = error;
        }
    }
}