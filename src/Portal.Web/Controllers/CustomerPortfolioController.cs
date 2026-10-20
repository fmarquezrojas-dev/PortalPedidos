using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Customers;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Queries.Customers;
using Portal.Web.Models.Customers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Customers;


namespace Portal.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class CustomerPortfolioController : Controller
{
    private readonly ICustomerPortfolioQuery _query;
    private readonly ICustomerPortfolioService _service;
    private readonly ILogger<CustomerPortfolioController> _logger;

    private readonly ICustomerEligibilityQuery _customerEligibilityQuery;
    private readonly IAuthorizationService _authorizationService;
    private readonly ICustomerPortfolioListQuery _listQuery;

    public CustomerPortfolioController(
      ICustomerPortfolioQuery query,
      ICustomerPortfolioService service,
      ILogger<CustomerPortfolioController> logger,
      ICustomerEligibilityQuery customerEligibilityQuery,
      IAuthorizationService authorizationService,
      ICustomerPortfolioListQuery listQuery)
    {
        _query = query;
        _service = service;
        _logger = logger;
        _customerEligibilityQuery = customerEligibilityQuery;
        _authorizationService = authorizationService;
        _listQuery = listQuery;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
    [Bind(Prefix = "Filters")] CustomerPortfolioListFilterDto filters,
    CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(
                "Los filtros enviados no son válidos. Revisa la búsqueda.");
        }

        try
        {
            var model = await _listQuery.SearchAsync(
                filters,
                cancellationToken);

            // La consulta devuelve textos normalizados y la página ajustada.
            // Los Tag Helpers deben utilizar esos valores, no los anteriores.
            ModelState.Clear();

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }



    [HttpGet]
    public async Task<IActionResult> Details(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound();
        }

        try
        {
            var model = await _query.GetAsync(
                id,
                cancellationToken);

            if (model is null)
            {
                return NotFound();
            }

            bool canCreate = (await _authorizationService.AuthorizeAsync(
                User, null, Portal.Web.Security.AppPolicies.CreateOrders)).Succeeded;
            bool canView = (await _authorizationService.AuthorizeAsync(
                User, null, Portal.Web.Security.AppPolicies.ViewOrders)).Succeeded;
            var eligibility = canCreate && canView
                ? await _customerEligibilityQuery.GetAsync(id, cancellationToken) : null;
            return View(new CustomerPortfolioDetailsViewModel
            {
                Portfolio = model, Eligibility = eligibility, CanCreateOrders = canCreate && canView
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        AssignCustomerPortfolioViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (model.CustomerId == Guid.Empty)
        {
            return BadRequest("El cliente no es válido.");
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = GetValidationMessage();

            return RedirectToAction(
                nameof(Details),
                new { id = model.CustomerId });
        }

        byte[]? rowVersion = null;

        if (model.ExpectedAssignmentId.HasValue)
        {
            if (model.ExpectedAssignmentId.Value == Guid.Empty ||
                !TryDecodeRowVersion(
                    model.ExpectedRowVersion,
                    out rowVersion))
            {
                TempData["ErrorMessage"] =
                    "La versión de la asignación no es válida. " +
                    "Revisa la ficha actualizada.";

                return RedirectToAction(
                    nameof(Details),
                    new { id = model.CustomerId });
            }
        }
        else if (!string.IsNullOrWhiteSpace(
                     model.ExpectedRowVersion))
        {
            TempData["ErrorMessage"] =
                "La información de la asignación no es válida. " +
                "Revisa la ficha actualizada.";

            return RedirectToAction(
                nameof(Details),
                new { id = model.CustomerId });
        }

        try
        {
            await _service.AssignAsync(
                model.CustomerId,
                model.SellerEmail,
                model.Reason,
                model.ExpectedAssignmentId,
                rowVersion,
                cancellationToken);

            TempData["SuccessMessage"] =
                "La asignación comercial quedó registrada.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (CustomerPortfolioException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Conflicto al asignar cartera al cliente {CustomerId}.",
                model.CustomerId);

            TempData["ErrorMessage"] =
                "Otra operación modificó la asignación. " +
                "Revisa la ficha actualizada antes de guardar.";
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Error al guardar cartera del cliente {CustomerId}.",
                model.CustomerId);

            TempData["ErrorMessage"] =
                "No se pudo guardar la asignación. " +
                "Revisa el responsable actual antes de intentarlo otra vez.";
        }
        catch (SqlException exception)
        {
            _logger.LogError(
                exception,
                "Error SQL al asignar cartera al cliente {CustomerId}.",
                model.CustomerId);

            TempData["ErrorMessage"] =
                "No se pudo completar la operación. " +
                "Revisa el responsable actual antes de volver a intentarlo.";
        }

        return RedirectToAction(
            nameof(Details),
            new { id = model.CustomerId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(
        ReleaseCustomerPortfolioViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (model.CustomerId == Guid.Empty)
        {
            return BadRequest("El cliente no es válido.");
        }

        if (!model.ConfirmRelease)
        {
            ModelState.AddModelError(
                nameof(model.ConfirmRelease),
                "Debes confirmar el retiro de la asignación.");
        }

        if (model.ExpectedAssignmentId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(model.ExpectedAssignmentId),
                "La asignación no es válida.");
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = GetValidationMessage();

            return RedirectToAction(
                nameof(Details),
                new { id = model.CustomerId });
        }

        if (!TryDecodeRowVersion(
                model.ExpectedRowVersion,
                out byte[]? rowVersion))
        {
            TempData["ErrorMessage"] =
                "La versión de la asignación no es válida. " +
                "Revisa la ficha actualizada.";

            return RedirectToAction(
                nameof(Details),
                new { id = model.CustomerId });
        }

        try
        {
            await _service.ReleaseAsync(
                model.CustomerId,
                model.ExpectedAssignmentId,
                rowVersion!,
                model.Reason,
                cancellationToken);

            TempData["SuccessMessage"] =
                "La asignación se retiró. " +
                "El cliente quedó sin responsable comercial.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (CustomerPortfolioException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Conflicto al retirar cartera del cliente {CustomerId}.",
                model.CustomerId);

            TempData["ErrorMessage"] =
                "Otra operación modificó la asignación. " +
                "Revisa la ficha actualizada.";
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Error al retirar cartera del cliente {CustomerId}.",
                model.CustomerId);

            TempData["ErrorMessage"] =
                "No se pudo completar el retiro. " +
                "Revisa el responsable actual.";
        }
        catch (SqlException exception)
        {
            _logger.LogError(
                exception,
                "Error SQL al retirar cartera del cliente {CustomerId}.",
                model.CustomerId);

            TempData["ErrorMessage"] =
                "No se pudo completar la operación. " +
                "Revisa el responsable actual antes de volver a intentarlo.";
        }

        return RedirectToAction(
            nameof(Details),
            new { id = model.CustomerId });
    }

    private string GetValidationMessage()
    {
        string[] messages = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .Take(5)
            .ToArray();

        return messages.Length > 0
            ? string.Join(" ", messages)
            : "Los datos enviados no son válidos. Revisa el formulario.";
    }

    private static bool TryDecodeRowVersion(
        string? encoded,
        out byte[]? rowVersion)
    {
        rowVersion = null;

        if (string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > 16)
        {
            return false;
        }
        
        try
        {
            byte[] decoded = Convert.FromBase64String(encoded);

            if (decoded.Length != 8)
            {
                return false;
            }

            rowVersion = decoded;

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}