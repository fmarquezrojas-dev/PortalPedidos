using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.Common;
using Portal.Application.DTOs.Integration;
using Portal.Application.Interfaces.Integration;
using Portal.Domain.Enums;
using Portal.Infrastructure.Identity;

namespace Portal.Web.Controllers;

[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Operator)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class IntegrationController : Controller
{
    private readonly IIntegrationMonitorQuery _query;
    private readonly IIntegrationRecoveryService _recovery;
    private readonly ILogger<IntegrationController> _logger;

    public IntegrationController(
        IIntegrationMonitorQuery query, IIntegrationRecoveryService recovery, ILogger<IntegrationController> logger)
    {
        _query = query;
        _recovery = recovery;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Attention(long? orderNumber, AttentionCategory? category,
        int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || orderNumber < 1 || (category.HasValue && !Enum.IsDefined(category.Value)))
        {
            return BadRequest("Los filtros son inválidos.");
        }

        try
        {
            return View(await _query.GetAttentionAsync(orderNumber, category, pageNumber, cancellationToken));
        }
        catch (Exception ex) when (ex.GetBaseException() is DbException or TimeoutException || ex is DbException)
        {
            _logger.LogError(ex, "Error de consulta de la bandeja de integración.");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        long? orderNumber,
        IntegrationStatus? status,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid ||
            (orderNumber.HasValue && orderNumber.Value < 1) ||
            (status.HasValue && !Enum.IsDefined(status.Value)))
        {
            return BadRequest("Los filtros son inválidos.");
        }

        var model = await _query.SearchAsync(
            orderNumber,
            status,
            pageNumber,
            cancellationToken);

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        Guid id,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return NotFound();
        }

        var model = await _query.GetByIdAsync(
            id,
            cancellationToken);

        if (model is null)
        {
            return NotFound();
        }

        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action(nameof(Index));
        if (model.Message.Status == IntegrationStatus.DeadLetter)
        {
            try
            {
                var preview = await _recovery.GetPreviewAsync(id, cancellationToken);
                ViewData["CanRecover"] = preview?.CanRecover == true;
                ViewData["BlockReason"] = preview?.BlockReason;
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
        return View(model);
    }
}
