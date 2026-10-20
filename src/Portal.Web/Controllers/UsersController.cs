using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.DTOs.Security;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Security;
using Portal.Web.Models.Users;
using Portal.Web.Security;
namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.ViewUsers)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class UsersController : Controller
{
    private readonly IUserAdministrationService _service;
    private readonly ILogger<UsersController> _logger;
    public UsersController(IUserAdministrationService service, ILogger<UsersController> logger)
    { _service = service; _logger = logger; }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool? isActive, int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Los filtros no son válidos.");
        try { var result = await _service.SearchAsync(search, isActive, pageNumber, cancellationToken); ModelState.Clear(); return View(result); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (UserAdministrationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet, Authorize(Policy = AppPolicies.ManageUsers)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        try { return View(new CreateManagedUserViewModel { AvailableRoles = await _service.GetAssignableRolesAsync(cancellationToken) }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageUsers)]
    public async Task<IActionResult> Create(CreateManagedUserViewModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            if (ModelState.IsValid)
            {
                string id = await _service.CreateAsync(new CreateManagedUserRequest { Email = model.Email, Password = model.Password,
                    IsActive = model.IsActive, Roles = model.Roles, Reason = model.Reason }, cancellationToken);
                TempData["SuccessMessage"] = "La cuenta quedó registrada.";
                return RedirectToAction(nameof(Edit), new { id });
            }
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (UserAdministrationException ex) { AddFailure(ex); }
        catch (DbUpdateException ex) { StorageFailure(ex); }
        catch (SqlException ex) { StorageFailure(ex); }
        // No volver a enviar contraseñas en HTML tras un error.
        model.Password = model.ConfirmPassword = "";
        ModelState.Remove(nameof(model.Password)); ModelState.Remove(nameof(model.ConfirmPassword));
        ModelState.AddModelError("", "Revisa los datos y vuelve a introducir la contraseña inicial y su confirmación.");
        try { model.AvailableRoles = await _service.GetAssignableRolesAsync(cancellationToken); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return View(model);
    }

    [HttpGet, Authorize(Policy = AppPolicies.ManageUsers)]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 450) return NotFound();
        try
        {
            var user = await _service.GetAsync(id, cancellationToken);
            if (user is null) return NotFound();
            return View(new EditManagedUserViewModel { Id = user.Id, Email = user.Email, IsActive = user.IsActive,
                ExpectedConcurrencyStamp = user.ConcurrencyStamp, Roles = user.Roles.ToList(),
                AvailableRoles = await _service.GetAssignableRolesAsync(cancellationToken) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageUsers)]
    public async Task<IActionResult> Edit(string id, EditManagedUserViewModel model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id != model.Id) return BadRequest("La cuenta no es válida.");
        try
        {
            if (ModelState.IsValid)
            {
                if (!await _service.UpdateAsync(new UpdateManagedUserRequest { Id = id,
                    ExpectedConcurrencyStamp = model.ExpectedConcurrencyStamp, IsActive = model.IsActive,
                    Roles = model.Roles, Reason = model.Reason }, cancellationToken)) return NotFound();
                TempData["SuccessMessage"] = "Los accesos se actualizaron. La cuenta deberá iniciar sesión nuevamente si hubo cambios.";
                return RedirectToAction(nameof(Edit), new { id });
            }
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (UserAdministrationException ex) { AddFailure(ex); model.RequiresReload = ex.IsConflict; }
        catch (DbUpdateConcurrencyException ex) { StorageFailure(ex, true); model.RequiresReload = true; }
        catch (DbUpdateException ex) { StorageFailure(ex); model.RequiresReload = true; }
        catch (SqlException ex) { StorageFailure(ex); model.RequiresReload = true; }
        try
        {
            var current = await _service.GetAsync(id, cancellationToken);
            if (current is null) return NotFound();
            model.Email = current.Email;
            model.AvailableRoles = await _service.GetAssignableRolesAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return View(model);
    }
    private void AddFailure(UserAdministrationException ex)
    { Response.StatusCode = ex.IsConflict ? 409 : 400; ModelState.AddModelError("", ex.Message); }
    private void StorageFailure(Exception ex, bool conflict = false)
    {
        _logger.LogWarning("Administración de usuarios: operación no completada. Tipo {ErrorType}.", ex.GetType().Name);
        Response.StatusCode = conflict ? 409 : 503;
        ModelState.AddModelError("", "No se pudo guardar la operación. Recarga la cuenta y revisa su estado antes de repetirla.");
    }
}
