using UnityEngine;
using System.IO;
//using Firebase.Storage;
using System;
using System.Collections;

public class CameraController : MonoBehaviour
{
    public void TakePhoto()
    {
        // NativeCamera plugin handles mobile camera & gallery
        NativeCamera.TakePicture((path) =>
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("No image selected.");
                return;
            }

            byte[] jpgBytes;
            Texture2D previewTex;

#if UNITY_EDITOR
            // Editor: read file bytes directly
            jpgBytes = File.ReadAllBytes(path);
            previewTex = LoadPreviewTexture(jpgBytes);
#else
            // Device: load & resize
            Texture2D tex = NativeCamera.LoadImageAtPath(path, 1024);
            if (tex == null)
            {
                Debug.LogError("Failed to load image.");
                return;
            }

            jpgBytes = tex.EncodeToJPG(80);
            previewTex = tex;
#endif

            GameManager.Instance.uiController.ShowLoading();
            // Call OpenAI controller, which handles Firebase upload + AI request
            GameManager.Instance.openAIController.SendImage(jpgBytes, previewTex);

        }, 1024);
    }

    private Texture2D LoadPreviewTexture(byte[] bytes)
    {
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        tex.LoadImage(bytes);
        return tex;
    }
}