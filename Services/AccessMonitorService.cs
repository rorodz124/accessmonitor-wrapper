using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccessMonitorWrapper.Models;

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

    public async Task<AccessMonitorResponse> EvaluateAsync(string url)
    {
        var requestUri = $"/amp/eval/{Uri.EscapeDataString(url)}";

        _logger.LogInformation("Calling AccessMonitor: {RequestUri}", requestUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

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
            var root = document.RootElement.Clone();

            // Log keys available (excluding pagecode which is huge)
            if (root.TryGetProperty("data", out var dataEl))
            {
                var keys = dataEl.EnumerateObject().Select(p => p.Name).Where(k => k != "pagecode");
                _logger.LogInformation("AccessMonitor data keys for URL {Url}: {Keys}", url, string.Join(", ", keys));
            }

            var sanitizedReport = StripPagecode(root);
            return BuildSummaryResponse(sanitizedReport);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AccessMonitor response for URL: {Url}", url);
            throw new AccessMonitorException("Failed to parse the AccessMonitor response.", HttpStatusCode.BadGateway);
        }
    }

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

    /// <summary>
    /// Returns the full response but removes "pagecode" from "data" — it's the raw HTML
    /// of the evaluated page and can be several MB, serving no purpose in the API response.
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

    private static AccessMonitorResponse BuildSummaryResponse(JsonElement report)
    {
        return new AccessMonitorResponse
        {
            Score = GetFloat(report, [
                "score",
                "result",
                "statistics.globalScore",
                "globalScore",
                "resultGrade",
                "data.score",
                "data.result",
                "data.statistics.globalScore",
                "data.globalScore",
                "data.resultGrade",
                "data.data.score",
                "data.data.result",
                "data.data.statistics.globalScore",
                "data.data.globalScore",
                "data.data.resultGrade"
            ]),
            Passed = GetCount(report, [
                "passed",
                "metrics.passed",
                "statistics.passed",
                "summary.passed",
                "totals.passed",
                "assertions_passed",
                "data.passed",
                "data.metrics.passed",
                "data.statistics.passed",
                "data.summary.passed",
                "data.totals.passed",
                "data.assertions_passed",
                "data.data.passed",
                "data.data.metrics.passed",
                "data.data.statistics.passed",
                "data.data.summary.passed",
                "data.data.totals.passed",
                "data.data.assertions_passed"
            ], ["pass", "passed", "success", "successful", "ok", "true"]),
            Warnings = GetCount(report, [
                "warnings",
                "warning",
                "metrics.warnings",
                "statistics.warnings",
                "summary.warnings",
                "totals.warnings",
                "assertions_warnings",
                "data.warnings",
                "data.warning",
                "data.metrics.warnings",
                "data.statistics.warnings",
                "data.summary.warnings",
                "data.totals.warnings",
                "data.assertions_warnings",
                "data.data.warnings",
                "data.data.warning",
                "data.data.metrics.warnings",
                "data.data.statistics.warnings",
                "data.data.summary.warnings",
                "data.data.totals.warnings",
                "data.data.assertions_warnings"
            ], ["warning", "warnings", "warn"]),
            Failed = GetCount(report, [
                "failed",
                "failures",
                "errors",
                "error",
                "metrics.failed",
                "metrics.errors",
                "statistics.failed",
                "statistics.errors",
                "summary.failed",
                "summary.errors",
                "totals.failed",
                "totals.errors",
                "assertions_failed",
                "data.failed",
                "data.failures",
                "data.errors",
                "data.error",
                "data.metrics.failed",
                "data.metrics.errors",
                "data.statistics.failed",
                "data.statistics.errors",
                "data.summary.failed",
                "data.summary.errors",
                "data.totals.failed",
                "data.totals.errors",
                "data.assertions_failed",
                "data.data.failed",
                "data.data.failures",
                "data.data.errors",
                "data.data.error",
                "data.data.metrics.failed",
                "data.data.metrics.errors",
                "data.data.statistics.failed",
                "data.data.statistics.errors",
                "data.data.summary.failed",
                "data.data.summary.errors",
                "data.data.totals.failed",
                "data.data.totals.errors",
                "data.data.assertions_failed"
            ], ["fail", "failed", "failure", "error", "errors", "false"]),
            Data = report.TryGetProperty("data", out var data)
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(data.GetRawText())
                : null,
            RawResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(report.GetRawText())
        };
    }

    private static float? GetFloat(JsonElement root, string[] paths)
    {
        foreach (var path in paths)
        {
            if (TryGetPath(root, path, out var value) && TryGetFloat(value, out var number))
                return number;
        }

        return null;
    }

    private static int? GetCount(JsonElement root, string[] paths, string[] resultValues)
    {
        foreach (var path in paths)
        {
            if (!TryGetPath(root, path, out var value))
                continue;

            if (TryGetInt(value, out var number))
                return number;

            if (value.ValueKind == JsonValueKind.Array)
                return value.GetArrayLength();

            if (value.ValueKind == JsonValueKind.Object)
            {
                var count = CountResultObjects(value, resultValues);
                if (count > 0)
                    return count;
            }
        }

        var inferredCount = CountResultObjects(root, resultValues);
        return inferredCount > 0 ? inferredCount : null;
    }

    private static int CountResultObjects(JsonElement element, string[] acceptedValues)
    {
        var count = 0;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (IsResultProperty(prop.Name) && MatchesResultValue(prop.Value, acceptedValues))
                    count++;

                count += CountResultObjects(prop.Value, acceptedValues);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                count += CountResultObjects(item, acceptedValues);
        }

        return count;
    }

    private static bool IsResultProperty(string propertyName)
    {
        return propertyName.Equals("result", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("outcome", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("status", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("verdict", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesResultValue(JsonElement value, string[] acceptedValues)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var normalized = value.GetString()?.Trim().ToLowerInvariant();
            return normalized != null && acceptedValues.Contains(normalized);
        }

        if (value.ValueKind == JsonValueKind.True)
            return acceptedValues.Contains("true");

        if (value.ValueKind == JsonValueKind.False)
            return acceptedValues.Contains("false");

        return false;
    }

    private static bool TryGetPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;

        foreach (var segment in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }

        return true;
    }

    private static bool TryGetFloat(JsonElement element, out float value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String
            && float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        value = default;
        return false;
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        value = default;
        return false;
    }

    private static JsonElement FilterHtmlResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return root;

        var filtered = new JsonObject();

        // Try top-level errors/warnings
        if (root.TryGetProperty("errors", out var errors))
            filtered["errors"] = JsonNode.Parse(errors.GetRawText());

        if (root.TryGetProperty("warnings", out var warnings))
            filtered["warnings"] = JsonNode.Parse(warnings.GetRawText());

        // Also check nested 'data'
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("errors", out var dataErrors))
                filtered["errors"] = JsonNode.Parse(dataErrors.GetRawText());

            if (data.TryGetProperty("warnings", out var dataWarnings))
                filtered["warnings"] = JsonNode.Parse(dataWarnings.GetRawText());
        }

        return filtered.Count > 0
            ? JsonDocument.Parse(filtered.ToJsonString()).RootElement.Clone()
            : root;
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