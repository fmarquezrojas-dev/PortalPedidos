using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Interfaces.Security;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewRoles)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class RolesController : Controller
{
    private readonly IRoleDirectoryQuery _query;

    public RolesController(
        IRoleDirectoryQuery query)
    {
        _query = query;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var model = await _query.GetAsync(
                cancellationToken);

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}