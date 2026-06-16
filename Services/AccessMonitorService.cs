using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccessMonitorWrapper.Services;

public class AccessMonitorService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AccessMonitorService> _logger;

    public AccessMonitorService(HttpClient httpClient, IConfiguration config, ILogger<AccessMonitorService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<JsonElement> EvaluateAsync(string url)
    {
        var requestUri = $"/amp/eval/{Uri.EscapeDataString(url)}";
        _logger.LogInformation("AccessMonitor GET {Uri}", requestUri);

        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));
        AddReferer(req);

        var body = await SendAndReadAsync(req, url);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> EvaluateHtmlAsync(string html)
    {
        var requestUri = "/amp/eval/html";
        _logger.LogInformation("AccessMonitor POST {Uri}", requestUri);

        using var req = new HttpRequestMessage(HttpMethod.Post, requestUri);
        req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(new { html }), Encoding.UTF8, "application/json");
        AddReferer(req);

        var body = await SendAndReadAsync(req, "html");

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AccessMonitor HTML response. Body snippet: {Body}",
                body[..Math.Min(body.Length, 300)]);
            throw new AccessMonitorException("Resposta inválida do AccessMonitor.", HttpStatusCode.BadGateway);
        }
    }

    private void AddReferer(HttpRequestMessage req)
    {
        var referer = _config["AccessMonitor:Referer"] ?? _httpClient.BaseAddress?.ToString() ?? "http://localhost:3000";
        req.Headers.TryAddWithoutValidation("Referer", referer);
    }

    private async Task<string> SendAndReadAsync(HttpRequestMessage req, string context)
    {
        HttpResponseMessage res;
        try
        {
            res = await _httpClient.SendAsync(req);
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout calling AccessMonitor for: {Context}", context);
            throw new AccessMonitorException("O AccessMonitor não respondeu a tempo.", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Connection error calling AccessMonitor for: {Context}", context);
            throw new AccessMonitorException("Não foi possível ligar ao AccessMonitor.", HttpStatusCode.BadGateway);
        }

        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("AccessMonitor {Status} for {Context}: {Body}", (int)res.StatusCode, context, body[..Math.Min(body.Length, 300)]);
            throw new AccessMonitorException($"AccessMonitor devolveu erro {(int)res.StatusCode}.", res.StatusCode);
        }

        var ct = res.Content.Headers.ContentType?.MediaType ?? "";
        if (!ct.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("AccessMonitor unexpected Content-Type '{CT}' for {Context}", ct, context);
            throw new AccessMonitorException($"Tipo de conteúdo inesperado: {ct}.", HttpStatusCode.BadGateway);
        }

        return body;
    }
}

public class AccessMonitorException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public AccessMonitorException(string message, HttpStatusCode statusCode) : base(message)
        => StatusCode = statusCode;
}