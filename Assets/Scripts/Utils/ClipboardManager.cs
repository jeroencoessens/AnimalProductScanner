using UnityEngine;
using System;
using System.Runtime.InteropServices;

public class ClipboardManager : MonoBehaviour
{
    // Singleton instance
    private static ClipboardManager _instance;

    public static ClipboardManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<ClipboardManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("ClipboardManager");
                    _instance = go.AddComponent<ClipboardManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    private Action<string> onPasteCallback;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void WebGLGetClipboardText(string gameObjectName, string methodName);
#endif

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Requests text from the system clipboard.
    /// </summary>
    /// <param name="callback">Action to execute with the pasted text.</param>
    public void PasteFromClipboard(Action<string> callback)
    {
        onPasteCallback = callback;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Call the JavaScript function
        WebGLGetClipboardText(gameObject.name, "OnClipboardTextReceived");
#else
        // Use Unity's built-in clipboard access for Editor, Standalone, and Mobile
        string text = GUIUtility.systemCopyBuffer;
        OnClipboardTextReceived(text);
#endif
    }

    // Called from JavaScript (WebGL) or internally (Native)
    public void OnClipboardTextReceived(string text)
    {
        if (onPasteCallback != null)
        {
            onPasteCallback.Invoke(text);
            onPasteCallback = null; // Clear callback after use
        }
    }
}