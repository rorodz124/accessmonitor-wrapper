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

        var errors = new JsonArray();
        var warnings = new JsonArray();

        AddNamedIssues(root, "errors", errors);
        AddNamedIssues(root, "warnings", warnings);

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            AddNamedIssues(data, "errors", errors);
            AddNamedIssues(data, "warnings", warnings);

            if (data.TryGetProperty("data", out var nestedData) && nestedData.ValueKind == JsonValueKind.Object)
            {
                AddNamedIssues(nestedData, "errors", errors);
                AddNamedIssues(nestedData, "warnings", warnings);
            }
        }

        ExtractNodeIssues(root, errors, warnings);

        var filtered = new JsonObject
        {
            ["errors"] = errors,
            ["warnings"] = warnings,
            ["summary"] = new JsonObject
            {
                ["errors"] = errors.Count,
                ["warnings"] = warnings.Count
            }
        };

        return JsonDocument.Parse(filtered.ToJsonString()).RootElement.Clone();
    }

    private static void AddNamedIssues(JsonElement source, string propertyName, JsonArray target)
    {
        if (!source.TryGetProperty(propertyName, out var value))
            return;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                target.Add(JsonNode.Parse(item.GetRawText()));
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            target.Add(JsonNode.Parse(value.GetRawText()));
        }
    }

    private static void ExtractNodeIssues(JsonElement root, JsonArray errors, JsonArray warnings)
    {
        foreach (var nodes in FindNodesObjects(root))
        {
            foreach (var criterion in nodes.EnumerateObject())
            {
                if (criterion.Value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var check in criterion.Value.EnumerateArray())
                {
                    if (check.ValueKind != JsonValueKind.Object)
                        continue;

                    var verdict = GetStringProperty(check, "verdict")
                        ?? GetStringProperty(check, "result")
                        ?? GetStringProperty(check, "status")
                        ?? GetStringProperty(check, "outcome");

                    var issueType = NormalizeIssueType(verdict);
                    if (issueType == null)
                        continue;

                    var issue = new JsonObject
                    {
                        ["criterion"] = criterion.Name,
                        ["verdict"] = verdict,
                        ["resultCode"] = GetStringProperty(check, "resultCode") ?? GetStringProperty(check, "code"),
                        ["description"] = GetStringProperty(check, "description") ?? GetStringProperty(check, "message"),
                        ["elements"] = check.TryGetProperty("elements", out var elements)
                            ? JsonNode.Parse(elements.GetRawText())
                            : new JsonArray(),
                        ["attributes"] = check.TryGetProperty("attributes", out var attributes)
                            ? JsonNode.Parse(attributes.GetRawText())
                            : new JsonArray()
                    };

                    if (issueType == "error")
                        errors.Add(issue);
                    else
                        warnings.Add(issue);
                }
            }
        }
    }

    private static IEnumerable<JsonElement> FindNodesObjects(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        if (element.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
            yield return nodes;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name == "pagecode")
                continue;

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var found in FindNodesObjects(prop.Value))
                    yield return found;
            }
        }
    }

    private static string? NormalizeIssueType(string? verdict)
    {
        var normalized = verdict?.Trim().ToLowerInvariant();

        if (normalized is "failed" or "fail" or "failure" or "error" or "errors" or "false")
            return "error";

        if (normalized is "warning" or "warnings" or "warn")
            return "warning";

        return null;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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