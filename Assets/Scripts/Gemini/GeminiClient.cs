using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class GeminiClient : MonoBehaviour
{
    [Header("API Configuration")]
    private string apiKey = "YOUR_API_KEY_HERE";
    public string modelId = "gemini-2.5-flash"; 

    [Header("Prompt Configuration")]
    [TextArea(3, 10)]
    public string customPrompt = "Analyze the image.\n\nIdentify visible clothing or footwear items in the image.\n\nFor each item:\n- Identify likely animal-derived materials\n- Identify animal species involved\n- Estimate number of animals used (fractions allowed)\n- Assign confidence (low, medium, high)\n\nReturn a JSON object with these identifications";

    [Header("Reverse Image Search")]
    [Tooltip("When enabled, asks Gemini to perform a reverse image search to find the product online and scrape accurate material information.")]
    public bool enableReverseImageSearch = false;
    
    [Range(1, 5)]
    [Tooltip("Maximum number of items to perform reverse image search for (to avoid overloading the API).")]
    public int maxItemsToSearchOnline = 3;
    
    [TextArea(3, 10)]
    [Tooltip("Additional prompt text to append when reverse image search is enabled.")]
    //keep private, otherwise inspector prompt will override this
    private string reverseImageSearchPrompt = "\n\nIMPORTANT: For EACH identified item (up to {MAX_ITEMS} items), perform a separate reverse image search to identify if that specific product is available in online stores. If found, scrape the product details from the store's website to get accurate material composition instead of estimating. For each item's 'production_summary' field:\n- If found online: Start with '[PRODUCT FOUND: {product_name} from {store/brand}]' followed by the full production ethics summary (environmental impact, animal welfare, industry practices)\n- If not found online: Start with '[NO ONLINE MATCH]' and STILL PROVIDE A COMPLETE production ethics analysis based on visual inspection of the materials (covering environmental impact, animal welfare concerns, and industry practices - maximum 3 sentences)\n- If you've already searched {MAX_ITEMS} items: Skip the search, start with '[SEARCH LIMIT REACHED]' and STILL PROVIDE A COMPLETE production ethics analysis based on visual inspection";

    [Header("Debug Options")]
    public bool enableDebugLogging = true;
    public bool logFullApiResponse = false;
    public bool validateApiKeyOnStart = true;

    [Header("UI References")]
    public TMP_Text loadingDebugText;
    public TMP_Text resultText;
    public TMP_InputField apiInputField;

    private string BaseUrl => $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

    // --- These classes match the EXACT structure Gemini returns ---
    [Serializable]
    public class ItemAnalysis
    {
        public string name; // Changed from itemName
        public string material; // Changed from animalDerivedMaterials (array) to single string per item
        public string species; // Changed from animalSpecies (array) to single string
        public float animal_count; // Changed from estimatedAnimalCount
        public string confidence; // "low", "medium", "high"
        public string production_summary; // New field
    }

    [Serializable]
    public class PredictionSummary
    {
        public float total_estimated_animals;
        public int item_count;
    }

    [Serializable]
    public class PredictionResult // Your specific data
    {
        public PredictionSummary summary;
        public ItemAnalysis[] items;
        
        // Helper properties for backward compatibility and quick checks
        public bool ContainsAnimalProducts => items != null && items.Length > 0;
        public int TotalItems => summary != null ? summary.item_count : (items != null ? items.Length : 0);
        public float TotalEstimatedAnimalCount => summary != null ? summary.total_estimated_animals : 0f;
    }

    [Serializable]
    public class GeminiResponse
    {
        public Candidate[] candidates;
        public UsageMetadata usageMetadata; // Added to capture token usage
    }

    [Serializable]
    public class UsageMetadata
    {
        public int promptTokenCount;
        public int candidatesTokenCount;
        public int totalTokenCount;
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

    // --- Local Cache Structure ---
    [Serializable]
    public class MaterialCacheEntry
    {
        public string materialName;
        public string productionSummary;
    }

    [Serializable]
    public class MaterialCache
    {
        public List<MaterialCacheEntry> entries = new List<MaterialCacheEntry>();
    }

    private MaterialCache localCache;
    private string cacheFilePath;

    void Start()
    {
        cacheFilePath = Path.Combine(Application.persistentDataPath, "MaterialCache.json");
        LoadCache();

        if (validateApiKeyOnStart)
        {
            ValidateApiKey();
        }
    }

    private void LoadCache()
    {
        if (File.Exists(cacheFilePath))
        {
            try
            {
                string json = File.ReadAllText(cacheFilePath);
                localCache = JsonUtility.FromJson<MaterialCache>(json);
                if (enableDebugLogging) Debug.Log($"[GeminiClient] Loaded cache with {localCache.entries.Count} entries.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GeminiClient] Failed to load cache: {e.Message}");
                localCache = new MaterialCache();
            }
        }
        else
        {
            localCache = new MaterialCache();
        }
    }

    private void SaveCache()
    {
        try
        {
            string json = JsonUtility.ToJson(localCache, true);
            File.WriteAllText(cacheFilePath, json);
            if (enableDebugLogging) Debug.Log("[GeminiClient] Cache saved.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeminiClient] Failed to save cache: {e.Message}");
        }
    }

    private string GetCachedSummary(string materialName)
    {
        if (localCache == null || string.IsNullOrEmpty(materialName)) return null;
        var entry = localCache.entries.FirstOrDefault(e => e.materialName.Equals(materialName, StringComparison.OrdinalIgnoreCase));
        return entry?.productionSummary;
    }

    private void AddToCache(string materialName, string summary)
    {
        if (localCache == null || string.IsNullOrEmpty(materialName) || string.IsNullOrEmpty(summary)) return;
        
        // Check if already exists
        if (localCache.entries.Any(e => e.materialName.Equals(materialName, StringComparison.OrdinalIgnoreCase)))
            return;

        localCache.entries.Add(new MaterialCacheEntry { materialName = materialName, productionSummary = summary });
        SaveCache();
    }

    public void SetAPIKey()
    {
        apiKey = apiInputField.text;
        Debug.Log("API key was set!");
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

        // Construct the prompt with knowledge of cached items
        string promptToUse = string.IsNullOrWhiteSpace(promptOverride) ? customPrompt : promptOverride;
        
        // Add reverse image search prompt if enabled
        if (enableReverseImageSearch && !string.IsNullOrWhiteSpace(reverseImageSearchPrompt))
        {
            // Replace placeholder with actual max items value
            string processedSearchPrompt = reverseImageSearchPrompt.Replace("{MAX_ITEMS}", maxItemsToSearchOnline.ToString());
            promptToUse += processedSearchPrompt;
            if (enableDebugLogging)
            {
                Debug.Log($"[GeminiClient] Reverse image search enabled (max {maxItemsToSearchOnline} items) - appending additional prompt.");
            }
        }
        
        if (localCache != null && localCache.entries.Count > 0)
        {
            string knownMaterials = string.Join(", ", localCache.entries.Select(e => e.materialName));
            promptToUse += $"\n\nNOTE: I already have detailed production summaries for the following materials: {knownMaterials}. If you identify any of these, please leave the 'production_summary' field empty or null to save tokens. I will fill it in from my local database.";
        }

        // Escape the prompt for JSON
        string escapedPrompt = promptToUse.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        // GenerationConfig forces the model to be a robot and return JSON
        string jsonPayload = @"{
            ""contents"": [{
                ""parts"": [
                    {""text"": """ + escapedPrompt + @"""},
                    {""inline_data"": {""mime_type"": ""image/jpeg"", ""data"": """ + base64Image + @"""}}
                ]
            }],
            ""generationConfig"": {
                ""response_mime_type"": ""application/json"",
                ""response_schema"": {
                    ""type"": ""OBJECT"",
                    ""properties"": {
                        ""summary"": {
                            ""type"": ""OBJECT"",
                            ""properties"": {
                                ""total_estimated_animals"": {""type"": ""NUMBER""},
                                ""item_count"": {""type"": ""INTEGER""}
                            }
                        },
                        ""items"": {
                            ""type"": ""ARRAY"",
                            ""items"": {
                                ""type"": ""OBJECT"",
                                ""properties"": {
                                    ""name"": {""type"": ""STRING""},
                                    ""material"": {""type"": ""STRING""},
                                    ""species"": {""type"": ""STRING""},
                                    ""animal_count"": {""type"": ""NUMBER""},
                                    ""confidence"": {""type"": ""STRING""},
                                    ""production_summary"": {""type"": ""STRING""}
                                },
                                ""required"": [""name"", ""confidence""]
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

                    // Log Token Usage
                    if (fullResponse.usageMetadata != null)
                    {
                        Debug.Log($"[GeminiClient] Token Usage - Prompt: {fullResponse.usageMetadata.promptTokenCount}, Response: {fullResponse.usageMetadata.candidatesTokenCount}, Total: {fullResponse.usageMetadata.totalTokenCount}");
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
                    else
                    {
                        // --- CACHE LOGIC ---
                        bool cacheUpdated = false;
                        foreach (var item in result.items)
                        {
                            if (item == null) continue;

                            // Skip if no material or material is empty
                            if (string.IsNullOrEmpty(item.material)) continue;

                            // Extract the actual ethics summary from tagged responses
                            string ExtractEthicsSummary(string summary)
                            {
                                if (string.IsNullOrEmpty(summary)) return null;
                                
                                // Remove tags and extract content
                                if (summary.StartsWith("[PRODUCT FOUND:"))
                                {
                                    int endBracket = summary.IndexOf(']');
                                    return endBracket > 0 ? summary.Substring(endBracket + 1).Trim() : summary;
                                }
                                else if (summary.StartsWith("[NO ONLINE MATCH]"))
                                {
                                    return summary.Replace("[NO ONLINE MATCH]", "").Trim();
                                }
                                else if (summary.StartsWith("[SEARCH LIMIT REACHED]"))
                                {
                                    return summary.Replace("[SEARCH LIMIT REACHED]", "").Trim();
                                }
                                return summary;
                            }

                            // If summary is missing or empty after tag removal, try to load from cache
                            string currentEthicsSummary = ExtractEthicsSummary(item.production_summary);
                            
                            if (string.IsNullOrEmpty(currentEthicsSummary))
                            {
                                string cachedSummary = GetCachedSummary(item.material);
                                if (!string.IsNullOrEmpty(cachedSummary))
                                {
                                    // Preserve the tag if present, append cached summary
                                    if (!string.IsNullOrEmpty(item.production_summary) && 
                                        (item.production_summary.StartsWith("[")))
                                    {
                                        int endBracket = item.production_summary.IndexOf(']');
                                        if (endBracket > 0)
                                        {
                                            string tag = item.production_summary.Substring(0, endBracket + 1);
                                            item.production_summary = tag + " " + cachedSummary;
                                        }
                                        else
                                        {
                                            item.production_summary = cachedSummary;
                                        }
                                    }
                                    else
                                    {
                                        item.production_summary = cachedSummary;
                                    }
                                    if (enableDebugLogging) Debug.Log($"[GeminiClient] CACHE HIT: Filled summary for '{item.material}' from local cache.");
                                }
                                else
                                {
                                    if (enableDebugLogging) Debug.Log($"[GeminiClient] CACHE MISS: No summary found for '{item.material}' in cache.");
                                }
                            }
                            // If summary is present, save the clean version to cache if not already there
                            else if (!string.IsNullOrEmpty(currentEthicsSummary))
                            {
                                if (GetCachedSummary(item.material) == null)
                                {
                                    AddToCache(item.material, currentEthicsSummary);
                                    cacheUpdated = true;
                                    if (enableDebugLogging) Debug.Log($"[GeminiClient] CACHE UPDATE: Added new summary for '{item.material}' to cache.");
                                }
                                else
                                {
                                     if (enableDebugLogging) Debug.Log($"[GeminiClient] CACHE EXISTS: Summary for '{item.material}' already exists, skipping update.");
                                }
                            }
                        }
                        
                        if (enableDebugLogging)
                        {
                            Debug.Log($"[GeminiClient] Successfully parsed result! Found {result.items.Length} item(s)");
                            foreach (var item in result.items)
                            {
                                if (item != null)
                                {
                                    Debug.Log($"[GeminiClient]   - {item.name}: {item.material}, {item.animal_count} animals, confidence: {item.confidence}");
                                }
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
            index++;
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