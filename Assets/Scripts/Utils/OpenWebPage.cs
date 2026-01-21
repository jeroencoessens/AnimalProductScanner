using UnityEngine;

public class OpenWebPage : MonoBehaviour
{
    public void Open(string page)
    {
        Application.OpenURL(page);
    }
}
