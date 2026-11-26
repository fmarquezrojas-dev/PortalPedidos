using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.DTOs.Customers;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Queries.Customers;

namespace Portal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class CommercialPortfolioControllerBase(CommercialPortfolioQuery portfolio, bool team) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, PortfolioFilter? filter, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || search?.Length > 100 || (filter.HasValue && !Enum.IsDefined(filter.Value)))
            return BadRequest("Revisa la búsqueda y el tipo de responsabilidad.");
        return View("~/Views/CommercialPortfolio/Index.cshtml",
            await portfolio.SearchAsync(team, search, filter, page, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var row = await portfolio.FindAsync(id, team, cancellationToken);
        if (row == null) return NotFound();
        ViewData["TeamView"] = team;
        return View("~/Views/CommercialPortfolio/Details.cshtml", row);
    }
}

[Authorize(Roles = AppRoles.Seller)]
public sealed class MyPortfolioController(CommercialPortfolioQuery portfolio)
    : CommercialPortfolioControllerBase(portfolio, false);

[Authorize(Roles = AppRoles.SalesSupervisor)]
public sealed class TeamPortfolioController(CommercialPortfolioQuery portfolio)
    : CommercialPortfolioControllerBase(portfolio, true);
