using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIController : MonoBehaviour
{
    public GameObject panelCamera;
    public GameObject panelLoading;
    public GameObject panelResult;

    public RawImage previewImage;
    public TMP_Text resultText;

    public void ShowLoading()
    {
        panelCamera.SetActive(false);
        panelLoading.SetActive(true);
        panelResult.SetActive(false);
    }

    public void ShowResult(Texture2D preview, string aiResponse)
    {
        panelCamera.SetActive(false);
        panelLoading.SetActive(false);
        panelResult.SetActive(true);

        previewImage.texture = preview;
        resultText.text = ExtractText(aiResponse);
    }

    public void BackToCamera()
    {
        panelCamera.SetActive(true);
        panelLoading.SetActive(false);
        panelResult.SetActive(false);
    }

    private string ExtractText(string rawJson)
    {
        // naive extraction for now
        int index = rawJson.IndexOf("content");
        if (index < 0) return rawJson;
        return rawJson;
    }
    
    public void ShowStructuredResult(Texture2D preview, AnalysisResult result)
    {
        panelCamera.SetActive(false);
        panelLoading.SetActive(false);
        panelResult.SetActive(true);

        previewImage.texture = preview;

        StringBuilder sb = new StringBuilder();

        foreach (var item in result.items)
        {
            sb.AppendLine(item.item);
            foreach (var animal in item.animals)
                sb.AppendLine($"- {animal.species}: {animal.estimated_count}");
        }

        resultText.text = sb.ToString();
    }

    public void ShowRawFallback(Texture2D preview, string raw)
    {
        panelCamera.SetActive(false);
        panelLoading.SetActive(false);
        panelResult.SetActive(true);

        previewImage.texture = preview;
        resultText.text = "Analysis failed.\n\n" + raw;
    }

}