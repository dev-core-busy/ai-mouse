using System.Text.Json.Serialization;

namespace AiMouse.Vision;

// Minimal OpenAI-compatible chat-completions payloads. Property names are pinned
// explicitly so the wire format never depends on a serializer naming policy.

internal sealed class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Either a plain string (system messages) or an array of content parts
    /// (multimodal user messages), which is why this is serialised as an object.
    /// </summary>
    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;
}

internal sealed class ContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageUrl? ImageUrl { get; set; }

    public static ContentPart FromText(string text) => new() { Type = "text", Text = text };

    public static ContentPart FromImage(string dataUri) => new()
    {
        Type = "image_url",
        ImageUrl = new ImageUrl { Url = dataUri },
    };
}

internal sealed class ImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

internal sealed class ChatResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }

    [JsonPropertyName("error")]
    public ChatError? Error { get; set; }
}

internal sealed class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatResponseMessage? Message { get; set; }
}

internal sealed class ChatResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class ChatError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
