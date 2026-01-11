using System;
using System.Collections.Generic;

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

// Parsed game data
[Serializable]
public class AnalysisResult
{
    public List<ItemResult> items;
    public List<AnimalTotal> total_animals;
}

[Serializable]
public class ItemResult
{
    public string item;
    public List<string> materials;
    public List<AnimalUse> animals;
    public string confidence;
}

[Serializable]
public class AnimalUse
{
    public string species;
    public float estimated_count;
}

[Serializable]
public class AnimalTotal
{
    public string species;
    public float estimated_count;
}