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
            return BuildSimpleReport(doc.RootElement);
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


    private static JsonElement BuildSimpleReport(JsonElement root)
    {
        var d = FindDataNode(root);

        var score = d.TryGetProperty("score",   out var s) ? s.GetString() ?? "N/A" : "N/A";
        var conform = d.TryGetProperty("conform", out var c) ? c.GetString() ?? ""    : "";

        var errors= new JsonArray();
        var warnings = new JsonArray();

        if (d.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in nodes.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var result in entry.Value.EnumerateArray())
                {
                    if (!result.TryGetProperty("verdict", out var vp)) continue;
                    var verdict = vp.GetString() ?? "";
                    if (verdict != "failed" && verdict != "warning") continue;

                    var desc = result.TryGetProperty("description", out var dp) ? dp.GetString() ?? "" : "";
                    var code = result.TryGetProperty("resultCode",  out var rp) ? rp.GetString() ?? "" : "";

                    var elems = new JsonArray();
                    if (result.TryGetProperty("elements", out var ee) && ee.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in ee.EnumerateArray())
                        {
                            var o = new JsonObject();
                            if (el.TryGetProperty("htmlCode", out var hc))  o["htmlCode"] = hc.GetString();
                            if (el.TryGetProperty("pointer",  out var pt))  o["pointer"]  = pt.GetString();
                            elems.Add(o);
                        }
                    }

                    var issue = new JsonObject
                    {
                        ["criterion"] = entry.Name,
                        ["description"] = desc,
                        ["resultCode"] = code,
                        ["elements"] = elems
                    };

                    if (verdict == "failed") errors.Add(issue);
                    else                     warnings.Add(issue);
                }
            }
        }

        var cp = conform.Split('@');
        var out_ = new JsonObject
        {
            ["score"] = score,
            ["conform"] = new JsonObject
            {
                ["A"] = cp.Length > 0 ? cp[0] : "0",
                ["AA"] = cp.Length > 1 ? cp[1] : "0",
                ["AAA"] = cp.Length > 2 ? cp[2] : "0",
            },
            ["errors"] = errors,
            ["warnings"] = warnings,
        };

        return JsonDocument.Parse(out_.ToJsonString()).RootElement.Clone();
    }


    private static JsonElement FindDataNode(JsonElement el, int depth = 0)
    {
        if (depth > 3) return el;
        if (el.TryGetProperty("nodes", out _) || (el.TryGetProperty("score", out _) && !el.TryGetProperty("pagecode", out _)))
            return el;
        if (el.TryGetProperty("data", out var child) && child.ValueKind == JsonValueKind.Object)
            return FindDataNode(child, depth + 1);
        return el;
    }
}


public class AccessMonitorException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public AccessMonitorException(string message, HttpStatusCode statusCode) : base(message)
        => StatusCode = statusCode;
}