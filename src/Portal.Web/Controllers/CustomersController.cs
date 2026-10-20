using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Interfaces.Customers;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewOrders)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class CustomersController : Controller
{
    private readonly ICustomerDirectoryQuery _query;

    public CustomersController(
        ICustomerDirectoryQuery query)
    {
        _query = query;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        bool? isActive,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(
                "Los parámetros de búsqueda no son válidos.");
        }

        if (search is { Length: > 100 })
        {
            return BadRequest(
                "La búsqueda no puede superar los 100 caracteres.");
        }

        try
        {
            var model = await _query.SearchAsync(
                search,
                isActive,
                pageNumber,
                cancellationToken);

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}