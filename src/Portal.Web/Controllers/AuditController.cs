using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Interfaces.Audit;
using Portal.Application.DTOs.Audit;
using Portal.Infrastructure.Identity;

namespace Portal.Web.Controllers;

[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Operator)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class AuditController : Controller
{
    private readonly IAuditQuery _query;

    public AuditController(IAuditQuery query)
    {
        _query = query;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AuditSearchDto filters,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return View(new AuditPageDto { Filters = filters, Search = filters.Search });
        }
        try { return View(await _query.SearchAsync(filters, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Response.StatusCode = 400;
            return View(new AuditPageDto { Filters = filters, Search = filters.Search });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return NotFound();
        }

        try
        {
            var model = await _query.GetByIdAsync(
                id,
                cancellationToken);

            if (model is null)
            {
                return NotFound();
            }

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
