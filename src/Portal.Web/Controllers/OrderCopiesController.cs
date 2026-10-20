using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Orders;
using Portal.Web.Models.Orders;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.CreateOrders)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class OrderCopiesController : Controller
{
    private readonly ICopyRejectedOrderService _service;
    private readonly ILogger<OrderCopiesController> _logger;

    public OrderCopiesController(
        ICopyRejectedOrderService service,
        ILogger<OrderCopiesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid id,
        Guid? draftId,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid ||
            id == Guid.Empty ||
            draftId == Guid.Empty ||
            draftId == id)
        {
            return BadRequest("Los identificadores no son válidos.");
        }

        try
        {
            var preview = await _service.GetPreviewAsync(
                id,
                cancellationToken);

            if (preview is null)
            {
                return NotFound();
            }

            var model = new CopyRejectedOrderViewModel
            {
                SourceOrderId = id,
                DraftId = draftId ?? Guid.NewGuid(),
                CatalogVersion = preview.CatalogVersion,
                RequestedDate = preview.TodayInPeru.AddDays(1),
                Preview = preview
            };

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid id,
        CopyRejectedOrderViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty ||
            id != model.SourceOrderId ||
            model.DraftId == Guid.Empty ||
            model.DraftId == id)
        {
            return BadRequest("Los identificadores no son válidos.");
        }

        try
        {
            var preview = await _service.GetPreviewAsync(
                id,
                cancellationToken);

            if (preview is null)
            {
                return NotFound();
            }

            model.Preview = preview;

            if (!model.ConfirmCopy)
            {
                ModelState.AddModelError(
                    nameof(model.ConfirmCopy),
                    "Debes confirmar que revisaste los datos actuales.");
            }

            if (model.RequestedDate.HasValue &&
                model.RequestedDate.Value < preview.TodayInPeru)
            {
                ModelState.AddModelError(
                    nameof(model.RequestedDate),
                    "La fecha no puede ser anterior a hoy en Perú.");
            }

            if (!ModelState.IsValid)
            {
                ResetConfirmation(model);
                return View("Index", model);
            }

            Guid? newOrderId = await _service.CreateAsync(
                model.SourceOrderId,
                model.DraftId,
                model.CatalogVersion,
                model.RequestedDate!.Value,
                cancellationToken);

            if (!newOrderId.HasValue)
            {
                return NotFound();
            }

            TempData["SuccessMessage"] =
                "La copia está disponible. Revisa su estado y sus datos " +
                "antes de continuar. Crear la copia no la envía al ERP.";

            return RedirectToAction(
                "Details",
                "Orders",
                new { id = newOrderId.Value });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (CopyRejectedOrderException exception)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;

            ModelState.AddModelError(
                string.Empty,
                exception.Message);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "No se pudo guardar la copia del pedido {SourceOrderId}.",
                id);

            Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;

            ModelState.AddModelError(
                string.Empty,
                "No se pudo completar la operación. " +
                "Recarga esta revisión y vuelve a intentarlo.");
        }
        catch (SqlException exception)
        {
            _logger.LogError(
                exception,
                "Error SQL al copiar el pedido {SourceOrderId}.",
                id);

            Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;

            ModelState.AddModelError(
                string.Empty,
                "La base de datos no pudo completar la operación. " +
                "Recarga esta revisión y vuelve a intentarlo.");
        }

        model.RequiresReload = true;
        ResetConfirmation(model);

        return View("Index", model);
    }

    private void ResetConfirmation(
        CopyRejectedOrderViewModel model)
    {
        model.ConfirmCopy = false;

        ModelState.Remove(
            nameof(CopyRejectedOrderViewModel.ConfirmCopy));
    }
}