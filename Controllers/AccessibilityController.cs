using AccessMonitorWrapper.Models;
using AccessMonitorWrapper.Services;
using Microsoft.AspNetCore.Mvc;

namespace AccessMonitorWrapper.Controllers;

[ApiController]
[Route("api")]
public class AccessibilityController : ControllerBase
{
    private readonly AccessMonitorService _accessMonitorService;
    private readonly ILogger<AccessibilityController> _logger;

    public AccessibilityController(
        AccessMonitorService accessMonitorService,
        ILogger<AccessibilityController> logger)
    {
        _accessMonitorService = accessMonitorService;
        _logger = logger;
    }

    /// <summary>
    /// Validates the accessibility of a web page.
    /// </summary>
    /// <param name="request">The request containing the URL to validate.</param>
    /// <returns>The accessibility report from AccessMonitor.</returns>
    [HttpPost("validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Validate([FromBody] ValidateRequest request)
    {
        // Validate that a URL was provided
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "The 'url' field is required." });
        }

        // Validate that the URL is well-formed
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { error = "The provided URL is not valid. It must be an absolute HTTP or HTTPS URL." });
        }

        _logger.LogInformation("Received validation request for URL: {Url}", request.Url);

        try
        {
            var report = await _accessMonitorService.EvaluateAsync(request.Url);
            return Ok(report);
        }
        catch (AccessMonitorException ex)
        {
            _logger.LogWarning(ex, "AccessMonitor error for URL: {Url}", request.Url);

            return StatusCode((int)ex.StatusCode, new
            {
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Validates the accessibility of raw HTML through the AccessMonitor API.
    /// </summary>
    /// <param name="request">The request containing the HTML to validate.</param>
    /// <returns>A filtered response with only errors and warnings.</returns>
    [HttpPost("validate/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> ValidateHtml([FromBody] ValidateHtmlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Html))
        {
            return BadRequest(new { error = "The 'html' field is required." });
        }

        _logger.LogInformation("Received HTML validation request. Html length: {Length}", request.Html.Length);

        try
        {
            var report = await _accessMonitorService.EvaluateHtmlAsync(request.Html);
            return Ok(report);
        }
        catch (AccessMonitorException ex)
        {
            _logger.LogWarning(ex, "AccessMonitor error for HTML validation request.");
            return StatusCode((int)ex.StatusCode, new
            {
                error = ex.Message
            });
        }
    }
}
