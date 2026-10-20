using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Integration;
using Portal.Infrastructure.Identity;
using Portal.Web.Models.Integration;

namespace Portal.Web.Controllers;

[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Operator)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class IntegrationRecoveryController : Controller
{
    private readonly IIntegrationRecoveryService _service;
    private readonly ILogger<IntegrationRecoveryController> _logger;

    public IntegrationRecoveryController(
        IIntegrationRecoveryService service,
        ILogger<IntegrationRecoveryController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return NotFound();
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

            return View(new RecoverIntegrationViewModel
            {
                MessageId = id,
                RowVersion = preview.RowVersion,
                Preview = preview
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        Guid id,
        RecoverIntegrationViewModel model,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || id != model.MessageId)
        {
            return BadRequest();
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

            if (!model.ConfirmRecovery)
            {
                ModelState.AddModelError(
                    nameof(model.ConfirmRecovery),
                    "Debes confirmar la recuperación.");
            }

            byte[] rowVersion = Array.Empty<byte>();

            try
            {
                rowVersion = Convert.FromBase64String(
                    model.RowVersion ?? string.Empty);

                if (rowVersion.Length != 8)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "La versión del mensaje es inválida. Recarga la página.");

                model.RequiresReload = true;
            }

            if (!ModelState.IsValid)
            {
                model.ConfirmRecovery = false;
                ModelState.Remove(nameof(model.ConfirmRecovery));

                return View("Index", model);
            }

            bool recovered = await _service.RecoverAsync(
                id,
                rowVersion,
                model.Reason,
                cancellationToken);

            if (!recovered)
            {
                return NotFound();
            }

            TempData["SuccessMessage"] =
                "Se programó la recuperación. " +
                "El procesador consultará primero el resultado en el ERP.";

            return RedirectToAction(
                "Details",
                "Integration",
                new { id });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (IntegrationRecoveryException exception)
        {
            Response.StatusCode = 409;
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            Response.StatusCode = 409;

            ModelState.AddModelError(
                string.Empty,
                "El mensaje cambió durante la operación. " +
                "Consulta su estado y recarga la confirmación.");
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "No se pudo guardar la recuperación de {MessageId}.",
                id);

            Response.StatusCode = 503;

            ModelState.AddModelError(
                string.Empty,
                "No se pudo completar la operación. " +
                "Consulta el seguimiento antes de volver a intentarlo.");
        }
        catch (SqlException exception)
        {
            _logger.LogError(
                exception,
                "Error SQL al recuperar {MessageId}.",
                id);

            Response.StatusCode = 503;

            ModelState.AddModelError(
                string.Empty,
                "No se pudo completar la operación. " +
                "Consulta el seguimiento antes de volver a intentarlo.");
        }

        model.RequiresReload = true;
        model.ConfirmRecovery = false;

        ModelState.Remove(nameof(model.ConfirmRecovery));

        return View("Index", model);
    }
}