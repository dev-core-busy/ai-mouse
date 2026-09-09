using System.Text.Json;

namespace AiMouse.Configuration;

/// <summary>
/// Loads <c>prompts.json</c> and <c>settings.json</c> from the directory holding the
/// executable. Missing or malformed files silently fall back to built-in defaults so
/// the tool stays usable when dropped onto a machine on its own.
/// </summary>
internal static class ConfigStore
{
    public const string PromptsFileName = "prompts.json";
    public const string SettingsFileName = "settings.json";

    /// <summary>Tolerates PascalCase and camelCase keys in hand-written config files.</summary>
    private static readonly JsonSerializerOptions ReadOptions = new(AppJsonContext.Default.Options)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>The file stays hand-editable, so it is written back indented.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new(AppJsonContext.Default.Options)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Directory of the running executable. <c>AppContext.BaseDirectory</c> would point
    /// into the single-file extraction folder, so the process path is used instead.
    /// </summary>
    public static string BaseDirectory
    {
        get
        {
            string? path = Environment.ProcessPath;
            string? directory = path is null ? null : Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(directory) ? System.AppContext.BaseDirectory : directory;
        }
    }

    public static string PromptsPath => Path.Combine(BaseDirectory, PromptsFileName);

    public static string SettingsPath => Path.Combine(BaseDirectory, SettingsFileName);

    public static IReadOnlyList<PromptItem> LoadPrompts(out string? error)
    {
        error = null;

        if (!File.Exists(PromptsPath))
        {
            return PromptItem.Defaults;
        }

        try
        {
            string json = File.ReadAllText(PromptsPath);
            List<PromptItem>? items = JsonSerializer.Deserialize<List<PromptItem>>(json, ReadOptions);

            items?.RemoveAll(static item => string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Prompt));

            if (items is null || items.Count == 0)
            {
                error = $"{PromptsFileName} contained no usable entries — using defaults.";
                return PromptItem.Defaults;
            }

            return items;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{PromptsFileName} could not be read ({ex.Message}) — using defaults.";
            return PromptItem.Defaults;
        }
    }

    /// <summary>Writes <c>settings.json</c>. Returns <c>null</c> on success.</summary>
    public static string? SaveSettings(AppSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, WriteOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{SettingsFileName} could not be written ({ex.Message}). Is the folder read-only?";
        }
    }

    public static AppSettings LoadSettings(out string? error)
    {
        error = null;

        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{SettingsFileName} could not be read ({ex.Message}) — using defaults.";
            return new AppSettings();
        }
    }
}
