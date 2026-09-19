namespace NetworkIntelligence.Contracts;

/// <summary>A blank override selects the built-in provider; custom providers use the same transfer protocol.</summary>
public static class SpeedTestProvider
{
    public const string DefaultEndpoint = "https://speed.cloudflare.com";

    public static string Resolve(string? customEndpoint)
    {
        if (string.IsNullOrWhiteSpace(customEndpoint)) return DefaultEndpoint;
        string endpoint = customEndpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Use an HTTPS provider base URL without credentials, a query or fragment, or leave it blank for automatic selection.");
        return uri.AbsoluteUri.TrimEnd('/');
    }
}
