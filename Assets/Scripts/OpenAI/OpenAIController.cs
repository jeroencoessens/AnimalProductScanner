using UnityEngine;
//using Firebase.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Text;

public class OpenAIController : MonoBehaviour
{
    [TextArea(12, 25)]
    public string analysisPrompt;

    public string API_KEY = "YOUR_OPENAI_KEY";
    private const string RESPONSES_ENDPOINT = "https://api.openai.com/v1/responses";

    // Entry point for camera or file selection
    public void SendImage(byte[] imageBytes, Texture2D preview)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError("Image bytes are empty");
            return;
        }

        StartCoroutine(UploadToFirebaseAndSend(imageBytes, preview));
    }

    private IEnumerator UploadToFirebaseAndSend(byte[] imageBytes, Texture2D preview)
    {
        // --- Step 1: Upload image to Firebase Storage ---
        string fileName = "images/" + Guid.NewGuid().ToString() + ".jpg";
        //FirebaseStorage storage = FirebaseStorage.DefaultInstance;
        //StorageReference storageRef = storage.GetReference(fileName);
//
        //bool uploadCompleted = false;
        //string publicUrl = null;
        //string uploadError = null;
//
        //var metadata = new MetadataChange
        //{
        //    ContentType = "image/jpeg"
        //};

        //var uploadTask = storageRef.PutBytesAsync(imageBytes, metadata);
        //uploadTask.ContinueWith(task =>
        //{
        //    if (task.IsCompleted)
        //    {
        //        storageRef.GetDownloadUrlAsync().ContinueWith(urlTask =>
        //        {
        //            if (urlTask.IsCompleted)
        //            {
        //                publicUrl = urlTask.Result.ToString();
        //                Debug.Log("Firebase image URL: " + publicUrl);
        //            }
        //            else
        //            {
        //                uploadError = urlTask.Exception?.Message;
        //            }
//
        //            uploadCompleted = true;
        //        });
        //    }
        //    else
        //    {
        //        uploadError = task.Exception?.Message;
        //        uploadCompleted = true;
        //    }
        //});
//
        //// Wait for upload to finish
        //while (!uploadCompleted) yield return null;
//
        //if (!string.IsNullOrEmpty(uploadError) || string.IsNullOrEmpty(publicUrl))
        //{
        //    Debug.LogError("Firebase upload failed: " + uploadError);
        //    yield break;
        //}

        // --- Step 2: Build OpenAI Responses request ---
        OpenAIRequest request = new OpenAIRequest
        {
            model = "gpt-4.1",
            temperature = 0.1f,
            max_output_tokens = 400,
            input = new List<InputItem>
            {
                new InputItem
                {
                    type = "message",
                    role = "user",
                    content = new List<InputContent>
                    {
                        new InputContent
                        {
                            type = "input_text",
                            text = analysisPrompt
                        }
                    }
                },
                new InputItem
                {
                    type = "input_image",
                    //image_url = publicUrl
                }
            }
        };

        string json = JsonUtility.ToJson(request, false);

        UnityWebRequest req = new UnityWebRequest(RESPONSES_ENDPOINT, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Authorization", "Bearer " + API_KEY);
        req.SetRequestHeader("Content-Type", "application/json");

        Debug.Log("OpenAI request JSON:\n" + JsonUtility.ToJson(request, true));

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("OpenAI Responses API error:\n" + req.downloadHandler.text);
            yield break;
        }

        ParseAndDisplay(req.downloadHandler.text, preview);
    }

    private void ParseAndDisplay(string rawResponse, Texture2D preview)
    {
        try
        {
            AIResponseRoot root = JsonUtility.FromJson<AIResponseRoot>(rawResponse);

            string jsonText = null;

            foreach (var output in root.output)
            {
                foreach (var content in output.content)
                {
                    if (content.type == "output_text")
                    {
                        jsonText = content.text;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(jsonText))
                throw new Exception("No output_text found");

            AnalysisResult result = JsonUtility.FromJson<AnalysisResult>(jsonText);

            GameManager.Instance.uiController.ShowStructuredResult(preview, result);
        }
        catch (Exception e)
        {
            Debug.LogError("Parse failure:\n" + e.Message);
            GameManager.Instance.uiController.ShowRawFallback(preview, rawResponse);
        }
    }

    // --- Supporting classes ---
    [Serializable]
    public class OpenAIRequest
    {
        public string model;
        public float temperature;
        public int max_output_tokens;
        public List<InputItem> input;
    }

    [Serializable]
    public class InputItem
    {
        public string type; // "message" or "input_image"
        public string role; // only for messages
        public List<InputContent> content; // only for messages
        public string image_url; // only for input_image
    }

    [Serializable]
    public class InputContent
    {
        public string type; // "input_text"
        public string text;
    }

    [Serializable]
    public class AIResponseRoot
    {
        public List<AIOutput> output;
    }

    [Serializable]
    public class AIOutput
    {
        public List<AIContent> content;
    }

    [Serializable]
    public class AIContent
    {
        public string type;
        public string text;
    }
}
