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

    // POST /api/validate  — valida por URL
    public async Task<JsonElement> EvaluateAsync(string url)
    {
        // O AccessMonitor espera o URL percent-encoded no path
        var requestUri = $"/amp/eval/{Uri.EscapeDataString(url)}";

        _logger.LogInformation("Calling AccessMonitor: GET {RequestUri}", requestUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        var response = await SendAsync(request, url);
        var body = await ReadBodyAsync(response, url);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.Clone();
            return StripPagecode(root);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AccessMonitor response for URL: {Url}", url);
            throw new AccessMonitorException("Failed to parse the AccessMonitor response.", HttpStatusCode.BadGateway);
        }
    }

    // POST /api/validate/html  — valida por HTML
    public async Task<JsonElement> EvaluateHtmlAsync(string html)
    {
        var requestUri = "/amp/eval/html";

        _logger.LogInformation("Calling AccessMonitor: POST {RequestUri}", requestUri);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8,
            "application/json"
        );

        var response = await SendAsync(request, "html");
        var body = await ReadBodyAsync(response, "html");

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AccessMonitor response for HTML validation.");
            throw new AccessMonitorException("Failed to parse the AccessMonitor response.", HttpStatusCode.BadGateway);
        }
    }

    // --- Helpers ---

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string context)
    {
        try
        {
            return await _httpClient.SendAsync(request);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken == CancellationToken.None)
        {
            _logger.LogError("Timeout calling AccessMonitor for: {Context}", context);
            throw new AccessMonitorException("The AccessMonitor service did not respond in time.", HttpStatusCode.GatewayTimeout);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Request cancelled while calling AccessMonitor for: {Context}", context);
            throw new AccessMonitorException("The AccessMonitor service did not respond in time.", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error connecting to AccessMonitor for: {Context}", context);
            throw new AccessMonitorException("Unable to connect to the AccessMonitor service.", HttpStatusCode.BadGateway);
        }
    }

    private async Task<string> ReadBodyAsync(HttpResponseMessage response, string context)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "AccessMonitor returned {StatusCode} for: {Context}. Body: {Body}",
                (int)response.StatusCode, context, body);

            throw new AccessMonitorException(
                $"AccessMonitor returned status {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType == null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AccessMonitor returned unexpected Content-Type '{ContentType}' for: {Context}",
                contentType, context);

            throw new AccessMonitorException(
                $"AccessMonitor returned an unexpected content type: {contentType}.",
                HttpStatusCode.BadGateway);
        }

        return body;
    }

    /// <summary>
    /// Remove o campo "pagecode" da resposta — é o HTML cru da página avaliada,
    /// pode ter vários MB e não tem utilidade na resposta da API.
    /// Tudo o resto é devolvido tal como o AccessMonitor enviou.
    /// </summary>
    private static JsonElement StripPagecode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return root;

        var result = new JsonObject();

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "data" && prop.Value.ValueKind == JsonValueKind.Object)
            {
                var data = new JsonObject();
                foreach (var dataProp in prop.Value.EnumerateObject())
                {
                    if (dataProp.Name != "pagecode")
                        data[dataProp.Name] = JsonNode.Parse(dataProp.Value.GetRawText());
                }
                result["data"] = data;
            }
            else
            {
                result[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
            }
        }

        return JsonDocument.Parse(result.ToJsonString()).RootElement.Clone();
    }
}

public class AccessMonitorException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public AccessMonitorException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}