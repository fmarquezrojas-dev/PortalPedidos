using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Interfaces.Catalog;

namespace Portal.Web.Controllers;

[Authorize]
[ResponseCache(
    Location = ResponseCacheLocation.None,
    NoStore = true)]
public class CatalogController : Controller
{
    private readonly IProductCatalogQuery _productCatalogQuery;

    public CatalogController(
        IProductCatalogQuery productCatalogQuery)
    {
        _productCatalogQuery = productCatalogQuery;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Los parámetros de búsqueda no son válidos.");
        }

        search = search?.Trim();

        if (search is { Length: > 100 })
        {
            return BadRequest(
                "La búsqueda no puede superar los 100 caracteres.");
        }

        var model = await _productCatalogQuery.SearchAsync(
            search,
            pageNumber,
            pageSize: 20,
            cancellationToken: cancellationToken);

        return View(model);
    }
}