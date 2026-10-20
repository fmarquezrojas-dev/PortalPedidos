using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.DTOs.Search;
using Portal.Application.Interfaces.Search;
using Portal.Web.Models.Search;
using Portal.Web.Security;
namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewOrders)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SearchAssistantController : Controller
{
    private readonly ISearchAssistant _assistant;
    public SearchAssistantController(ISearchAssistant assistant) => _assistant = assistant;

    [HttpGet]
    public IActionResult Index() => View(new SearchAssistantViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Index(SearchAssistantViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var intent = _assistant.Interpret(model.Question);
        model.Intent = intent;
        // Destinos fijos: no SQL, redirecciones externas ni acciones de escritura.
        // Cada controlador de destino vuelve a autorizar y aplicar su alcance.
        model.Destination = intent.Area switch
        {
            SearchArea.Orders => Url.Action("Index", "Orders", new { search = intent.Term, status = intent.OrderStatus,
                from = intent.From?.ToString("yyyy-MM-dd"), to = intent.To?.ToString("yyyy-MM-dd") }),
            SearchArea.Customers => Url.Action("Index", "Customers", new { search = intent.Term, isActive = intent.CustomerIsActive }),
            SearchArea.Catalog => Url.Action("Index", "Catalog", new { search = intent.Term }),
            _ => null
        };
        return View(model);
    }
}
