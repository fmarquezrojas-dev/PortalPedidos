using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Teams;
using Portal.Web.Models.Teams;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewTeams)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class TeamsController : Controller
{
    private readonly ISalesTeamService _service;
    private readonly ILogger<TeamsController> _logger;

    public TeamsController(
        ISalesTeamService service,
        ILogger<TeamsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("La página solicitada no es válida.");
        }

        try
        {
            var model = await _service.GetPageAsync(
                pageNumber,
                cancellationToken);

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.CreateTeams)]
    public IActionResult Create()
    {
        return View(new CreateSalesTeamViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.CreateTeams)]
    public async Task<IActionResult> Create(
        CreateSalesTeamViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!model.ConfirmSupervisorAssignment)
        {
            ModelState.AddModelError(
                nameof(model.ConfirmSupervisorAssignment),
                "Confirma la asignación del supervisor.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                Guid id = await _service.CreateAsync(
                    model.Code,
                    model.Name,
                    model.SupervisorEmail,
                    cancellationToken);

                TempData["SuccessMessage"] =
                    "El equipo fue creado correctamente. " +
                    "Si se asignó un nuevo rol al supervisor, " +
                    "deberá iniciar sesión nuevamente.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (SalesTeamException exception)
            {
                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(
                    exception,
                    "No se pudo crear el equipo comercial.");

                Response.StatusCode = 409;

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo guardar el equipo. " +
                    "Revisa el listado antes de repetir la operación.");
            }
            catch (SqlException exception)
            {
                _logger.LogError(
                    exception,
                    "Error SQL al crear el equipo comercial.");

                Response.StatusCode = 503;

                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo completar la operación. " +
                    "Revisa el listado antes de volver a intentarlo.");
            }
        }

        model.ConfirmSupervisorAssignment = false;

        ModelState.Remove(
            nameof(model.ConfirmSupervisorAssignment));

        return View(model);
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
            var model = await _service.GetDetailsAsync(
                id,
                cancellationToken);

            if (model is null)
            {
                return NotFound();
            }

            return View(model);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTeams)]
    public async Task<IActionResult> AddMember(
        Guid id,
        AddSalesTeamMemberViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid ||
            id == Guid.Empty ||
            id != model.TeamId)
        {
            return BadRequest(
                "Los datos de incorporación no son válidos.");
        }

        byte[]? version = DecodeVersion(model.RowVersion);

        if (version is null)
        {
            return BadRequest(
                "La versión del equipo no es válida.");
        }

        return await ExecuteMemberChangeAsync(
            id,
            () => _service.AddMemberAsync(
                id,
                model.SellerEmail,
                version,
                cancellationToken),
            "El vendedor fue incorporado al equipo.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTeams)]
    public async Task<IActionResult> RemoveMember(
        Guid id,
        RemoveSalesTeamMemberViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid ||
            id == Guid.Empty ||
            id != model.TeamId ||
            model.MembershipId == Guid.Empty ||
            !model.ConfirmRemoval)
        {
            return BadRequest(
                "Debes confirmar una asignación válida para retirarla.");
        }

        byte[]? version = DecodeVersion(model.RowVersion);

        if (version is null)
        {
            return BadRequest(
                "La versión del equipo no es válida.");
        }

        return await ExecuteMemberChangeAsync(
            id,
            () => _service.RemoveMemberAsync(
                id,
                model.MembershipId,
                version,
                cancellationToken),
            "La asignación finalizó. Su historial se conservó.");
    }

    private async Task<IActionResult> ExecuteMemberChangeAsync(
        Guid teamId,
        Func<Task> operation,
        string successMessage)
    {
        try
        {
            await operation();

            TempData["SuccessMessage"] = successMessage;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (SalesTeamException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] =
                "El equipo fue modificado por otra operación. " +
                "Revisa la ficha actual antes de repetir la acción.";
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "No se pudo cambiar una asignación del equipo {TeamId}.",
                teamId);

            TempData["ErrorMessage"] =
                "No se pudo guardar la asignación. " +
                "Revisa los integrantes actuales antes de continuar.";
        }
        catch (SqlException exception)
        {
            _logger.LogError(
                exception,
                "Error SQL al gestionar el equipo {TeamId}.",
                teamId);

            TempData["ErrorMessage"] =
                "No se pudo completar la operación. " +
                "Revisa la ficha antes de volver a intentarlo.";
        }

        return RedirectToAction(
            nameof(Details),
            new { id = teamId });
    }

    private static byte[]? DecodeVersion(string value)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(value);

            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}