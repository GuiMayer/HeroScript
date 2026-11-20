using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HeroScript.Mcp;

public static class McpSafetyPolicy
{
    public static Uri LocalUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !uri.IsLoopback || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("HeroScript MCP requires a local HTTP API origin.");
        return uri;
    }

    public static string Segment(string value) => value is "." or ".." || string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A non-empty identifier is required.") : Uri.EscapeDataString(value);
    public static int Limit(int value, int maximum = 100) => value >= 1 && value <= maximum
        ? value : throw new ArgumentOutOfRangeException(nameof(value), $"Use 1–{maximum}.");
}

public sealed class HeroScriptApiClient : IDisposable
{
    private readonly HttpClient _http;
    public HeroScriptApiClient(Uri origin, HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = McpSafetyPolicy.LocalUri(origin.AbsoluteUri),
            Timeout = TimeSpan.FromSeconds(45),
            MaxResponseContentBufferSize = 8 * 1024 * 1024
        };
    }

    public Task<JsonElement> Get(string route, CancellationToken token) => Send(HttpMethod.Get, route, null, token);
    public Task<JsonElement> Post(string route, object? body, CancellationToken token) => Send(HttpMethod.Post, route, body, token);
    public Task<JsonElement> ValidateContent(object body, CancellationToken token) => Send(HttpMethod.Post, "/api/v1/content/validate", body, token, true);

    private async Task<JsonElement> Send(HttpMethod method, string route, object? body, CancellationToken token, bool allowValidationFailure = false)
    {
        if (!route.StartsWith("/api/v1/", StringComparison.Ordinal) || route.Contains('\\'))
            throw new ArgumentException("Only explicit HeroScript API routes are supported.");
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await _http.SendAsync(request, token);
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode && !(allowValidationFailure && response.StatusCode == HttpStatusCode.UnprocessableEntity))
                throw new InvalidOperationException($"HeroScript API HTTP {(int)response.StatusCode}: {text[..Math.Min(text.Length, 1800)]}");
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("HeroScript API unavailable. Start the local engine and verify HEROSCRIPT_MCP_API_URL.", ex);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("HeroScript API request exceeded its time budget.");
        }
    }

    public void Dispose() => _http.Dispose();
}
