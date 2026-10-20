using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Interfaces.Dashboard;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewOrders)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class DashboardController : Controller
{
    private readonly IDashboardQuery _query;

    public DashboardController(IDashboardQuery query)
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