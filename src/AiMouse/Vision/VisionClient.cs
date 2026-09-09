using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiMouse.Configuration;

namespace AiMouse.Vision;

/// <summary>Posts prompt + screenshot to a local OpenAI-compatible vision endpoint.</summary>
internal sealed class VisionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    /// <summary>Normalised here too, so a hand-edited settings.json behaves like the dialog.</summary>
    private readonly string _endpoint;

    public VisionClient(AppSettings settings)
    {
        _settings = settings;
        _endpoint = EndpointResolver.Normalize(settings.Endpoint);
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 3600)),
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        }
    }

    /// <summary>Returns the model's answer, or throws <see cref="VisionException"/>.</summary>
    public async Task<string> AnalyzeAsync(string prompt, string imageDataUri, CancellationToken cancellationToken)
    {
        var request = new ChatRequest
        {
            Model = _settings.Model,
            Stream = false,
            Temperature = _settings.Temperature,
            MaxTokens = _settings.MaxTokens,
            Messages = [],
        };

        if (!string.IsNullOrWhiteSpace(_settings.SystemPrompt))
        {
            request.Messages.Add(new ChatMessage { Role = "system", Content = _settings.SystemPrompt });
        }

        request.Messages.Add(new ChatMessage
        {
            Role = "user",
            Content = new List<ContentPart>
            {
                ContentPart.FromText(prompt),
                ContentPart.FromImage(imageDataUri),
            },
        });

        string payload = JsonSerializer.Serialize(request, AppJsonContext.Default.ChatRequest);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VisionException($"The request timed out after {_settings.TimeoutSeconds}s. Is the model loaded?");
        }
        catch (HttpRequestException ex)
        {
            throw new VisionException($"Could not reach {_endpoint}.\r\n{ex.Message}");
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new VisionException($"Server responded {(int)response.StatusCode} {response.ReasonPhrase}.\r\n{Shorten(body)}");
            }

            ChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize(body, AppJsonContext.Default.ChatResponse);
            }
            catch (JsonException ex)
            {
                throw new VisionException($"Unexpected response format.\r\n{ex.Message}\r\n{Shorten(body)}");
            }

            if (parsed?.Error?.Message is { Length: > 0 } serverError)
            {
                throw new VisionException(serverError);
            }

            string? answer = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new VisionException($"The model returned an empty answer.\r\n{Shorten(body)}");
            }

            return answer.Trim();
        }
    }

    private static string Shorten(string value) => value.Length <= 800 ? value : value[..800] + " …";

    public void Dispose() => _http.Dispose();
}

internal sealed class VisionException(string message) : Exception(message);
