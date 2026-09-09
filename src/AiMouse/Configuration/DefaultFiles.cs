namespace AiMouse.Configuration;

/// <summary>
/// Template written next to the executable when the user asks to edit prompts.json and
/// it does not exist yet. Kept as a literal so a bare .exe stays fully self-contained.
/// settings.json has no counterpart here — the settings dialog writes it via
/// <see cref="ConfigStore.SaveSettings"/>.
/// </summary>
internal static class DefaultFiles
{
    public const string Prompts = """
        [
          {
            "Title": "Extract Text (OCR)",
            "Prompt": "Act as an OCR system. Extract all visible text from this image precisely without added commentary."
          },
          {
            "Title": "Describe Image",
            "Prompt": "Describe the contents and key visual elements of this image in detail."
          },
          {
            "Title": "Analyze Error / Code",
            "Prompt": "Analyze the error message or code shown in this snippet and propose a solution."
          }
        ]
        """;
}
