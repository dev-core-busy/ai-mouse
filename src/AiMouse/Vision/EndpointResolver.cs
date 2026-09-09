namespace AiMouse.Vision;

/// <summary>
/// Turns whatever the user typed into a chat-completions URL.
///
/// Servers advertise themselves inconsistently — Ollama prints a bare host, LM Studio
/// shows the <c>/v1</c> base, proxies hand out the full route — and posting to a base URL
/// yields a bare 404 that looks like the server is down. Normalising removes that trap.
/// </summary>
internal static class EndpointResolver
{
    private const string Route = "/chat/completions";
    private const string VersionedRoute = "/v1" + Route;

    /// <summary>
    /// Idempotent: applying it to an already complete URL changes nothing.
    /// Input that is not an absolute http(s) URL is returned untouched, so validation
    /// elsewhere still gets to report it.
    /// </summary>
    public static string Normalize(string endpoint)
    {
        string trimmed = endpoint.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        // Query strings and fragments have no meaning here and would end up in the middle
        // of the path once a segment is appended.
        string basePart = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        string path = uri.AbsolutePath.TrimEnd('/');

        if (path.EndsWith(Route, StringComparison.OrdinalIgnoreCase))
        {
            return basePart;
        }

        // A bare host carries no API version, so supply the conventional one.
        return path.Length == 0 ? basePart + VersionedRoute : basePart + Route;
    }
}
