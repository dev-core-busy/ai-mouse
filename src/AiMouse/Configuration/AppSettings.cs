namespace AiMouse.Configuration;

/// <summary>Contents of <c>settings.json</c>, sitting next to the executable.</summary>
internal sealed class AppSettings
{
    /// <summary>
    /// OpenAI-compatible chat completions endpoint. Works with Ollama
    /// (<c>http://localhost:11434/v1/chat/completions</c>) and LM Studio
    /// (<c>http://localhost:1234/v1/chat/completions</c>) alike.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434/v1/chat/completions";

    /// <summary>Vision-capable model name as the server reports it.</summary>
    public string Model { get; set; } = "llama3.2-vision";

    /// <summary>Optional bearer token; local servers usually ignore it.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional system message prepended to every request.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public int MaxTokens { get; set; } = 2048;

    public double Temperature { get; set; } = 0.2d;

    /// <summary>Pixels the pointer must travel before the gesture takes over.</summary>
    public int DragThreshold { get; set; } = 8;

    /// <summary>Put the model's answer on the clipboard as soon as it arrives.</summary>
    public bool CopyResultToClipboard { get; set; }
}
