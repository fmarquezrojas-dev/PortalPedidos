using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public IntegrationController(
        IIntegrationMonitorQuery query)
    {
        _query = query;
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

        return View(model);
    }
}