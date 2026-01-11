using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class GeminiClient : MonoBehaviour
{
    public string apiKey = "YOUR_API_KEY_HERE";
    public string modelId = "gemini-2.0-flash"; // Current 2026 stable model

    [TextArea(3, 10)]
    public string customPrompt = "Identify if the items in this image contain animal products.";

    private string BaseUrl => $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

    // --- These classes match the EXACT structure Gemini returns ---
    [Serializable]
    public class PredictionResult // Your specific data
    {
        public bool containsAnimalProducts;
        public string[] animalItems;
        public int estimatedAnimalCount;
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

    public IEnumerator AnalyzeImage(byte[] imageBytes, Action<PredictionResult> onParsedResult, Action<string> onError)
    {
        string base64Image = Convert.ToBase64String(imageBytes);

        // GenerationConfig forces the model to be a robot and return JSON
        string jsonPayload = @"{
            ""contents"": [{
                ""parts"": [
                    {""text"": """ + customPrompt + @"""},
                    {""inline_data"": {""mime_type"": ""image/jpeg"", ""data"": """ + base64Image + @"""}}
                ]
            }],
            ""generationConfig"": {
                ""response_mime_type"": ""application/json"",
                ""response_schema"": {
                    ""type"": ""OBJECT"",
                    ""properties"": {
                        ""containsAnimalProducts"": {""type"": ""BOOLEAN""},
                        ""animalItems"": {""type"": ""ARRAY"", ""items"": {""type"": ""STRING""}},
                        ""estimatedAnimalCount"": {""type"": ""INTEGER""}
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

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try {
                    // 1. Parse the whole API wrapper
                    GeminiResponse fullResponse = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                    
                    // 2. Extract the 'text' string which contains YOUR JSON
                    string innerJson = fullResponse.candidates[0].content.parts[0].text;
                    
                    // 3. Parse YOUR JSON into your result class
                    PredictionResult result = JsonUtility.FromJson<PredictionResult>(innerJson);
                    
                    onParsedResult?.Invoke(result);
                }
                catch (Exception e) {
                    onError?.Invoke("Parse Error: " + e.Message);
                }
            }
            else
            {
                onError?.Invoke($"API Error: {request.error}\n{request.downloadHandler.text}");
            }
        }
    }
}