using System.Text.Json.Serialization;
using AiMouse.Configuration;
using AiMouse.Vision;

namespace AiMouse;

/// <summary>
/// Source-generated serializer metadata. Reflection-based serialization is switched
/// off in the csproj, so every type crossing the JSON boundary must be listed here —
/// including <see cref="string"/> and <see cref="List{ContentPart}"/>, which are the
/// two runtime types of <see cref="ChatMessage.Content"/>.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<PromptItem>))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(List<ContentPart>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(object))]
internal partial class AppJsonContext : JsonSerializerContext;
