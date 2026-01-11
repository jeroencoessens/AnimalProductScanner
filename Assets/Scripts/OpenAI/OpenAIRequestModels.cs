using System;
using System.Collections.Generic;

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
    public string role; // only for message
    public List<InputContent> content; // only for message
    public string image_url; // only for input_image
}


[Serializable]
public class ImageData
{
    public string data;
}


[Serializable]
public class InputContent
{
    public string type;
    public string text;      // only for input_text
    public ImageData image;  // only for input_image
}


[Serializable]
public class ImageUrl
{
    public string url;
}
