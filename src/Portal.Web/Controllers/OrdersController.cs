using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Portal.Web.Formatting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.DTOs.Orders;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Orders;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Enums;
using Portal.Web.Models.Orders;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewOrders)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class OrdersController : Controller
{
    private readonly IOrderQuery _orderQuery;
    private readonly IOrderDraftService _draftService;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderQuery orderQuery,
        IOrderDraftService draftService,
        ICurrentUser currentUser,
        ILogger<OrdersController> logger)
    {
        _orderQuery = orderQuery;
        _draftService = draftService;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, OrderStatus? status,
        int pageNumber = 1, CancellationToken cancellationToken = default,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Challenge();
        if (!ModelState.IsValid) return InvalidSearch(search, status, from, to);
        try
        {
            return View(await _orderQuery.SearchAsync(new OrderSearchFilterDto
                { Search = search, Status = status, From = from, To = to },
                pageNumber, 20, cancellationToken));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return InvalidSearch(search, status, from, to);
        }
    }

    private IActionResult InvalidSearch(string? search, OrderStatus? status,
        DateOnly? from, DateOnly? to)
    {
        Response.StatusCode = 400;
        return View("Index", new OrderListPageDto
        {
            Search = search ?? string.Empty, Status = status, From = from, To = to,
            ShowsAllOrders = _currentUser.CanViewAllOrders,
            ShowsTeamOrders = !_currentUser.CanViewAllOrders && _currentUser.CanViewTeamOrders
        });
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] OrderSearchFilterDto filters,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Los filtros enviados no son válidos.");
        try
        {
            var result = await _orderQuery.ExportAsync(filters, cancellationToken);
            if (result.LimitExceeded)
                return BadRequest("La exportación admite hasta 2000 pedidos. Reduce el rango de fechas o utiliza más filtros.");
            var rows = new List<IEnumerable<string?>>
            {
                new string?[] { "Pedido", "Código de cliente", "Cliente", "Estado", "Creación (Perú)", "Moneda", "Subtotal referencial" }
            };
            rows.AddRange(result.Items.Select(item => new string?[] {
                item.PublicNumber.ToString(CultureInfo.InvariantCulture), item.CustomerCode, item.CustomerName,
                OrderStatusDisplay.Label(item.Status), PortalDateTime.Format(item.CreatedAtUtc),
                item.Currency, item.Subtotal.ToString("0.00", CultureInfo.InvariantCulture) }));
            return File(CsvDocument.Build(rows), "text/csv; charset=utf-8", "pedidos.csv");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return NotFound();
        try
        {
            var model = await _orderQuery.GetByIdAsync(id, cancellationToken);
            return model is null ? NotFound() : View(model);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound();
        }

        var model = await _orderQuery.GetByIdAsync(
            id,
            cancellationToken);

        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.CreateOrders)]
    public async Task<IActionResult> SearchCustomers(string? term, CancellationToken cancellationToken, int offset = 0)
    {
        term = term?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(term) || term.Length > 100 || offset < 0) return BadRequest();
        return Json(await _draftService.SearchCustomersAsync(term, offset, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.CreateOrders)]
    public async Task<IActionResult> SearchProducts(string? term, CancellationToken cancellationToken, int offset = 0)
    {
        term = term?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(term) || term.Length > 100 || offset < 0) return BadRequest();
        return Json(await _draftService.SearchProductsAsync(term, offset, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.CreateOrders)]
    public async Task<IActionResult> Create(
        CancellationToken cancellationToken = default,
        [FromQuery] Guid? customerId = null)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid || customerId == Guid.Empty)
        {
            return BadRequest("El identificador del cliente no es válido.");
        }

        var formData = await _draftService.GetFormDataAsync(
            customerId, Array.Empty<Guid>(), cancellationToken);

        var model = new CreateOrderViewModel
        {
            DraftId = Guid.NewGuid(),
            CatalogVersion = formData.CatalogVersion,
            RequestedDate = formData.TodayInPeru,
            FormData = formData
        };

        if (customerId.HasValue)
        {
            // GetFormDataAsync ya aplica la regla compartida de elegibilidad.
            // La preselección no sustituye la validación al guardar.
            if (formData.CustomerIsEligible)
            {
                model.CustomerId = customerId.Value;
            }
            else
            {
                // Evitar que Razor reutilice el valor de la URL como selección.
                ModelState.Remove(nameof(CreateOrderViewModel.CustomerId));
                ModelState.AddModelError(
                    nameof(CreateOrderViewModel.CustomerId),
                    "El cliente solicitado no está disponible para pedidos. " +
                    "Selecciona un cliente habilitado.");
            }
        }

        model.Lines.Add(new CreateOrderLineViewModel());

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.CreateOrders)]
    public async Task<IActionResult> Create(
        CreateOrderViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (model.DraftId == Guid.Empty)
        {
            return BadRequest(
                "El formulario no es válido. Abre Nuevo pedido nuevamente.");
        }

        if (model.Lines is null ||
            model.Lines.Count < 1 ||
            model.Lines.Count > 100)
        {
            return BadRequest(
                "El pedido debe contener entre 1 y 100 líneas.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var request = new CreateOrderDraftRequest
                {
                    DraftId = model.DraftId,
                    CustomerId = model.CustomerId!.Value,
                    RequestedDate = model.RequestedDate!.Value,
                    CatalogVersion = model.CatalogVersion,
                    Lines = model.Lines
                        .Select(line => new CreateOrderDraftLineRequest
                        {
                            ProductId = line.ProductId!.Value,
                            Quantity = line.Quantity
                        })
                        .ToArray()
                };

                Guid orderId = await _draftService.CreateAsync(
                    request,
                    cancellationToken);

                TempData["SuccessMessage"] =
                    "El borrador se guardó correctamente.";

                return RedirectToAction(
                    nameof(Details),
                    new { id = orderId });
            }
            catch (OrderDraftException exception)
            {
                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(
                    exception,
                    "No se pudo guardar el borrador {DraftId}.",
                    model.DraftId);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo guardar el borrador. " +
                    "Revisa los datos e inténtalo nuevamente.");
            }
            catch (SqlException exception)
            {
                _logger.LogError(
                    exception,
                    "Error SQL al guardar el borrador {DraftId}.",
                    model.DraftId);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo completar la operación. " +
                    "Inténtalo nuevamente en unos segundos.");
            }
        }

        model.FormData = await _draftService.GetFormDataAsync(
            model.CustomerId, SelectedProductIds(model), cancellationToken);

        if (model.CatalogVersion != model.FormData.CatalogVersion)
        {
            ModelState.AddModelError(
                string.Empty,
                "Se cargó una versión más reciente del catálogo. " +
                "Revisa los precios antes de volver a guardar.");
        }

        model.CatalogVersion = model.FormData.CatalogVersion;

        // Permitir que el campo oculto muestre la versión recargada.
        ModelState.Remove(nameof(CreateOrderViewModel.CatalogVersion));

        AddCustomerAvailabilityError(model);

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.EditOrders)]
    public async Task<IActionResult> Edit(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound();
        }

        var draft = await _draftService.GetEditableAsync(
            id,
            cancellationToken);

        if (draft is null)
        {
            return NotFound();
        }

        var formData = await _draftService.GetFormDataAsync(
            draft.CustomerId, draft.Lines.Select(line => line.ProductId).ToArray(), cancellationToken);

        var model = new CreateOrderViewModel
        {
            IsEdit = true,
            DraftId = draft.Id,
            PublicNumber = draft.PublicNumber,
            SavedSubtotal = draft.Subtotal,
            SavedPrices = draft.Lines.ToDictionary(l => l.ProductId, l => l.UnitPrice),
            CustomerId = draft.CustomerId,
            RequestedDate = draft.RequestedDate,
            CatalogVersion = formData.CatalogVersion,
            RowVersion = Convert.ToBase64String(draft.RowVersion),
            FormData = formData,
            AcceptCurrentPrices = false,
            Lines = draft.Lines
                .Select(line => new CreateOrderLineViewModel
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity
                })
                .ToList()
        };

        AddCustomerAvailabilityError(model);

        return View("Create", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.EditOrders)]
    public async Task<IActionResult> Edit(
        Guid id,
        CreateOrderViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (id == Guid.Empty || id != model.DraftId)
        {
            return BadRequest("El identificador del pedido no es válido.");
        }

        if (model.Lines is null ||
            model.Lines.Count < 1 ||
            model.Lines.Count > 100)
        {
            return BadRequest(
                "El pedido debe contener entre 1 y 100 líneas.");
        }

        // Verificar acceso antes de mostrar información del pedido.
        var draft = await _draftService.GetEditableAsync(
            id,
            cancellationToken);

        if (draft is null)
        {
            return NotFound();
        }

        byte[] rowVersion;

        try
        {
            rowVersion = Convert.FromBase64String(
                model.RowVersion ?? string.Empty);

            if (rowVersion.Length != 8)
            {
                return BadRequest(
                    "La versión del pedido no es válida. Recarga el formulario.");
            }
        }
        catch (FormatException)
        {
            return BadRequest(
                "La versión del pedido no es válida. Recarga el formulario.");
        }

        if (!model.AcceptCurrentPrices)
        {
            ModelState.AddModelError(
                nameof(model.AcceptCurrentPrices),
                "Confirma que revisaste los precios actuales y el importe.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var request = new UpdateOrderDraftRequest
                {
                    Id = id,
                    CustomerId = model.CustomerId!.Value,
                    RequestedDate = model.RequestedDate!.Value,
                    CatalogVersion = model.CatalogVersion,
                    RowVersion = rowVersion,
                    AcceptCurrentPrices = model.AcceptCurrentPrices,
                    Lines = model.Lines
                        .Select(line => new CreateOrderDraftLineRequest
                        {
                            ProductId = line.ProductId!.Value,
                            Quantity = line.Quantity
                        })
                        .ToArray()
                };

                bool updated = await _draftService.UpdateAsync(
                    request,
                    cancellationToken);

                if (!updated)
                {
                    return NotFound();
                }

                TempData["SuccessMessage"] =
                    "Los cambios del borrador se guardaron correctamente.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch (OrderDraftConflictException exception)
            {
                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    "El pedido fue modificado por otra operación. " +
                    "Tus cambios no se guardaron. Recarga el pedido.");
            }
            catch (OrderDraftException exception)
            {
                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(
                    exception,
                    "No se pudo actualizar el borrador {OrderId}.",
                    id);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudieron guardar los cambios del borrador.");
            }
            catch (SqlException exception)
            {
                _logger.LogError(
                    exception,
                    "Error SQL al editar el borrador {OrderId}.",
                    id);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo completar la operación. " +
                    "Inténtalo nuevamente en unos segundos.");
            }
        }

        model.IsEdit = true;
        model.PublicNumber = draft.PublicNumber;
        model.SavedSubtotal = draft.Subtotal;
        model.SavedPrices = draft.Lines.ToDictionary(l => l.ProductId, l => l.UnitPrice);

        model.FormData = await _draftService.GetFormDataAsync(
            model.CustomerId, SelectedProductIds(model), cancellationToken);

        if (model.CatalogVersion != model.FormData.CatalogVersion)
        {
            ModelState.AddModelError(
                string.Empty,
                "Se cargaron los precios actuales del catálogo. " +
                "Revísalos antes de volver a guardar.");
        }

        model.CatalogVersion = model.FormData.CatalogVersion;

        ModelState.Remove(nameof(CreateOrderViewModel.CatalogVersion));

        // Solicitar una nueva confirmación después de presentar errores.
        model.AcceptCurrentPrices = false;

        ModelState.SetModelValue(nameof(CreateOrderViewModel.AcceptCurrentPrices), false, "false");

        AddCustomerAvailabilityError(model);

        // Conservar RowVersion: no reemplazarla por una versión nueva
        // después de un conflicto, porque permitiría sobrescribir cambios.
        return View("Create", model);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.CancelOrders)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound();
        }

        var draft = await _draftService.GetEditableAsync(
            id,
            cancellationToken);

        if (draft is null)
        {
            return NotFound();
        }

        var details = await _orderQuery.GetByIdAsync(
            id,
            cancellationToken);

        if (details is null)
        {
            return NotFound();
        }

        var model = new CancelOrderViewModel
        {
            Id = draft.Id,
            RowVersion = Convert.ToBase64String(draft.RowVersion),
            PublicNumber = details.Order.PublicNumber,
            CustomerName = details.Order.CustomerName,
            Currency = details.Order.Currency,
            Subtotal = details.Order.Subtotal,
            CanCancel = details.Order.Status == OrderStatus.Draft
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.CancelOrders)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelOrderViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (id == Guid.Empty || id != model.Id)
        {
            return BadRequest(
                "El identificador del pedido no es válido.");
        }

        byte[] rowVersion;

        try
        {
            rowVersion = Convert.FromBase64String(
                model.RowVersion ?? string.Empty);

            if (rowVersion.Length != 8)
            {
                return BadRequest(
                    "La versión del pedido no es válida. " +
                    "Abre nuevamente la confirmación.");
            }
        }
        catch (FormatException)
        {
            return BadRequest(
                "La versión del pedido no es válida. " +
                "Abre nuevamente la confirmación.");
        }

        if (!model.ConfirmCancellation)
        {
            ModelState.AddModelError(
                nameof(model.ConfirmCancellation),
                "Debes confirmar que deseas cancelar el borrador.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                bool cancelled = await _draftService.CancelAsync(
                    new CancelOrderDraftRequest
                    {
                        Id = id,
                        RowVersion = rowVersion,
                        Reason = model.Reason
                    },
                    cancellationToken);

                if (!cancelled)
                {
                    return NotFound();
                }

                TempData["SuccessMessage"] =
                    "El pedido quedó cancelado.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch (OrderDraftConflictException)
            {
                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    "El pedido cambió desde que abriste esta pantalla. " +
                    "No se canceló. Recarga la confirmación " +
                    "y revisa la información actual.");
            }
            catch (DbUpdateConcurrencyException)
            {
                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    "Otra operación modificó el pedido. " +
                    "No se canceló. Recarga la confirmación.");
            }
            catch (OrderDraftException exception)
            {
                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(
                    exception,
                    "No se pudo cancelar el borrador {OrderId}.",
                    id);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo guardar la cancelación. " +
                    "Revisa el pedido antes de intentarlo nuevamente.");
            }
            catch (SqlException exception)
            {
                _logger.LogError(
                    exception,
                    "Error SQL al cancelar el borrador {OrderId}.",
                    id);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo completar la operación. " +
                    "Revisa el estado del pedido antes de reintentar.");
            }
        }

        var details = await _orderQuery.GetByIdAsync(
            id,
            cancellationToken);

        if (details is null)
        {
            return NotFound();
        }

        model.PublicNumber = details.Order.PublicNumber;
        model.CustomerName = details.Order.CustomerName;
        model.Currency = details.Order.Currency;
        model.Subtotal = details.Order.Subtotal;
        model.CanCancel = details.Order.Status == OrderStatus.Draft;

        model.ConfirmCancellation = false;

        ModelState.Remove(
            nameof(CancelOrderViewModel.ConfirmCancellation));

        // Conservar la versión original después de un conflicto.
        // El usuario debe recargar para revisar y confirmar la nueva versión.
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.SubmitOrders)]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound();
        }

        var draft = await _draftService.GetEditableAsync(
            id,
            cancellationToken);

        if (draft is null)
        {
            return NotFound();
        }

        var details = await _orderQuery.GetByIdAsync(
            id,
            cancellationToken);

        if (details is null)
        {
            return NotFound();
        }

        return View(new SubmitOrderViewModel
        {
            Id = id,
            RowVersion = Convert.ToBase64String(draft.RowVersion),
            Details = details,
            CanSubmit = details.Order.Status == OrderStatus.Draft
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.SubmitOrders)]
    public async Task<IActionResult> Submit(
        Guid id,
        SubmitOrderViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Challenge();
        }

        if (id == Guid.Empty || id != model.Id)
        {
            return BadRequest("El identificador del pedido no es válido.");
        }

        byte[] rowVersion;

        try
        {
            rowVersion = Convert.FromBase64String(
                model.RowVersion ?? string.Empty);

            if (rowVersion.Length != 8)
            {
                return BadRequest(
                    "La versión no es válida. Abre nuevamente la confirmación.");
            }
        }
        catch (FormatException)
        {
            return BadRequest(
                "La versión no es válida. Abre nuevamente la confirmación.");
        }

        if (!model.ConfirmSubmission)
        {
            ModelState.AddModelError(
                nameof(model.ConfirmSubmission),
                "Confirma que revisaste el pedido y deseas enviarlo.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                bool queued = await _draftService.QueueAsync(
                    new SubmitOrderDraftRequest
                    {
                        Id = id,
                        RowVersion = rowVersion
                    },
                    cancellationToken);

                if (!queued)
                {
                    return NotFound();
                }

                TempData["SuccessMessage"] =
                    "La solicitud de envío quedó registrada. " +
                    "Consulta el estado del pedido en este detalle.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch (OrderSubmissionException exception)
            {
                Response.StatusCode = 409;
                model.CustomerOutsidePortfolio = exception.Code == "OUTSIDE_PORTFOLIO";

                _logger.LogWarning(
                    "Envío bloqueado para {OrderId}. Código: {Code}.",
                    id,
                    exception.Code);

                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
            catch (OrderDraftConflictException)
            {
                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    "El pedido cambió después de abrir esta pantalla. " +
                    "Recarga la confirmación y revisa sus datos.");
            }
            catch (DbUpdateConcurrencyException)
            {
                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    "Otra operación modificó el pedido. " +
                    "Recarga la confirmación antes de enviarlo.");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(
                    exception,
                    "Error al preparar el envío de {OrderId}.",
                    id);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo guardar la solicitud de envío. " +
                    "Revisa el estado del pedido antes de reintentar.");
            }
            catch (SqlException exception)
            {
                _logger.LogError(
                    exception,
                    "Error SQL al preparar el envío de {OrderId}.",
                    id);

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo completar la operación. " +
                    "Revisa el estado del pedido antes de reintentar.");
            }
        }

        var details = await _orderQuery.GetByIdAsync(
            id,
            cancellationToken);

        if (details is null)
        {
            return NotFound();
        }

        model.Details = details;
        model.CanSubmit = details.Order.Status == OrderStatus.Draft;

        model.ConfirmSubmission = false;

        ModelState.Remove(nameof(SubmitOrderViewModel.ConfirmSubmission));

        // Mantener la versión revisada originalmente.
        // Una recarga explícita obtendrá la nueva RowVersion.
        return View(model);
    }

    private static Guid[] SelectedProductIds(CreateOrderViewModel model) =>
        model.Lines.Where(l => l.ProductId.HasValue)
            .Select(l => l.ProductId!.Value).Distinct().ToArray();

    private void AddCustomerAvailabilityError(CreateOrderViewModel model)
    {
        if (!model.CustomerId.HasValue || model.CustomerId.Value == Guid.Empty)
        {
            return;
        }

        if (model.FormData.CustomerIsEligible)
        {
            return;
        }

        const string fieldName = nameof(CreateOrderViewModel.CustomerId);

        if (ModelState.TryGetValue(fieldName, out var entry) &&
            entry.Errors.Count > 0)
        {
            return;
        }

        ModelState.AddModelError(
            fieldName,
            "El cliente seleccionado ya no está disponible para pedidos. " +
            "Selecciona un cliente habilitado antes de guardar. " +
            "Este aviso no modifica ni elimina el borrador guardado.");
    }

}
