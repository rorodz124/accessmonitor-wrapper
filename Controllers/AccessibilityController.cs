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
    /// Valida a acessibilidade de uma página web por URL.
    /// Devolve o relatório completo do AccessMonitor (sem o campo pagecode).
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Validate([FromBody] ValidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { error = "The 'url' field is required." });

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
            return StatusCode((int)ex.StatusCode, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Valida a acessibilidade de HTML em bruto.
    /// Devolve a resposta do AccessMonitor tal como está.
    /// </summary>
    [HttpPost("validate/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> ValidateHtml([FromBody] ValidateHtmlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Html))
            return BadRequest(new { error = "The 'html' field is required." });

        _logger.LogInformation("Received HTML validation request. Html length: {Length}", request.Html.Length);

        try
        {
            var report = await _accessMonitorService.EvaluateHtmlAsync(request.Html);
            return Ok(report);
        }
        catch (AccessMonitorException ex)
        {
            _logger.LogWarning(ex, "AccessMonitor error for HTML validation request.");
            return StatusCode((int)ex.StatusCode, new { error = ex.Message });
        }
    }
}
