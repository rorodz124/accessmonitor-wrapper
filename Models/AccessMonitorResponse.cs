using System.Text.Json.Serialization;

namespace AccessMonitorWrapper.Models;

/// <summary>
/// Structured response from the AccessMonitor service with standardized field mapping.
/// </summary>
public class AccessMonitorResponse
{
    [JsonPropertyName("score")]
    public float? Score { get; set; }

    [JsonPropertyName("passed")]
    public int? Passed { get; set; }

    [JsonPropertyName("warnings")]
    public int? Warnings { get; set; }

    [JsonPropertyName("failed")]
    public int? Failed { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }

    [JsonPropertyName("rawResponse")]
    public Dictionary<string, object>? RawResponse { get; set; }
}
