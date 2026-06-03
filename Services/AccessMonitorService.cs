using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccessMonitorWrapper.Services;

public class AccessMonitorService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AccessMonitorService> _logger;

    public AccessMonitorService(HttpClient httpClient, ILogger<AccessMonitorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the accessibility of a web page via the AccessMonitor API.
    /// </summary>
    /// <param name="url">The URL of the page to evaluate.</param>
    /// <returns>The accessibility report as a JsonElement.</returns>
    public async Task<JsonElement> EvaluateAsync(string url)
    {
        var urlBytes = System.Text.Encoding.UTF8.GetBytes(url);
        var urlBase64 = Convert.ToBase64String(urlBytes);
        var requestUri = $"/amp/eval/{Uri.EscapeDataString(urlBase64)}";

        _logger.LogInformation("Calling AccessMonitor: {RequestUri}", requestUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        // The AccessMonitor may require a Referer header (configured via env vars)
        // The Referer is set at HttpClient level via the base configuration

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError("Timeout calling AccessMonitor for URL: {Url}", url);
            throw new AccessMonitorException("The AccessMonitor service did not respond in time.", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error connecting to AccessMonitor for URL: {Url}", url);
            throw new AccessMonitorException("Unable to connect to the AccessMonitor service.", HttpStatusCode.BadGateway);
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "AccessMonitor returned {StatusCode} for URL: {Url}. Body: {Body}",
                (int)response.StatusCode, url, body);

            throw new AccessMonitorException(
                $"AccessMonitor returned status {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (contentType == null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AccessMonitor returned unexpected Content-Type '{ContentType}' for URL: {Url}",
                contentType, url);

            throw new AccessMonitorException(
                $"AccessMonitor returned an unexpected content type: {contentType}.",
                HttpStatusCode.BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AccessMonitor response for URL: {Url}", url);
            throw new AccessMonitorException("Failed to parse the AccessMonitor response.", HttpStatusCode.BadGateway);
        }
    }

    /// <summary>
    /// Evaluates the accessibility of raw HTML via the AccessMonitor API.
    /// </summary>
    /// <param name="html">The raw HTML to evaluate.</param>
    /// <returns>A filtered JSON result containing only errors and warnings where available.</returns>
    public async Task<JsonElement> EvaluateHtmlAsync(string html)
    {
        var requestUri = "/amp/eval/html";

        _logger.LogInformation("Calling AccessMonitor HTML evaluation: {RequestUri}", requestUri);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(new { html }), Encoding.UTF8, "application/json");

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError("Timeout calling AccessMonitor for HTML validation.");
            throw new AccessMonitorException("The AccessMonitor service did not respond in time.", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error connecting to AccessMonitor for HTML validation.");
            throw new AccessMonitorException("Unable to connect to the AccessMonitor service.", HttpStatusCode.BadGateway);
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "AccessMonitor returned {StatusCode} for HTML validation. Body: {Body}",
                (int)response.StatusCode, body);

            throw new AccessMonitorException(
                $"AccessMonitor returned status {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (contentType == null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AccessMonitor returned unexpected Content-Type '{ContentType}' for HTML validation.",
                contentType);

            throw new AccessMonitorException(
                $"AccessMonitor returned an unexpected content type: {contentType}.",
                HttpStatusCode.BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.Clone();
            return FilterHtmlResponse(root);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AccessMonitor response for HTML validation.");
            throw new AccessMonitorException("Failed to parse the AccessMonitor response.", HttpStatusCode.BadGateway);
        }
    }

    private static JsonElement FilterHtmlResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root;
        }

        var filtered = new JsonObject();

        if (root.TryGetProperty("errors", out var errors))
        {
            filtered["errors"] = JsonNode.Parse(errors.GetRawText());
        }

        if (root.TryGetProperty("warnings", out var warnings))
        {
            filtered["warnings"] = JsonNode.Parse(warnings.GetRawText());
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("errors", out var dataErrors))
            {
                filtered["errors"] = JsonNode.Parse(dataErrors.GetRawText());
            }

            if (data.TryGetProperty("warnings", out var dataWarnings))
            {
                filtered["warnings"] = JsonNode.Parse(dataWarnings.GetRawText());
            }
        }

        return filtered.Count > 0
            ? JsonDocument.Parse(filtered.ToJsonString()).RootElement.Clone()
            : root;
    }
}

/// <summary>
/// Custom exception for AccessMonitor communication failures.
/// </summary>
public class AccessMonitorException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public AccessMonitorException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
