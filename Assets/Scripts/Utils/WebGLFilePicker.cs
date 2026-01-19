using UnityEngine;
using System;
using System.Runtime.InteropServices;

public class WebGLFilePicker : MonoBehaviour
{
    // Singleton instance
    public static WebGLFilePicker Instance { get; private set; }

    private Action<string> onImagePicked;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void UploadImageFile(string gameObjectName, string methodName);
#endif

    public void PickImage(Action<string> callback)
    {
        onImagePicked = callback;
#if UNITY_WEBGL && !UNITY_EDITOR
        UploadImageFile(gameObject.name, "OnFileSelected");
#else
        Debug.LogWarning("WebGLFilePicker only works on WebGL builds.");
#endif
    }

    // Called from JavaScript
    public void OnFileSelected(string base64Image)
    {
        if (string.IsNullOrEmpty(base64Image))
        {
            Debug.LogError("WebGLFilePicker: Received empty image data.");
            return;
        }

        onImagePicked?.Invoke(base64Image);
    }
}