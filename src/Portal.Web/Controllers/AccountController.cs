using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Portal.Infrastructure.Identity;
using Portal.Web.Models.Account;

namespace Portal.Web.Controllers;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    // Mostrar el formulario de inicio de sesión.
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterLogin(returnUrl);
        }

        var model = new LoginViewModel
        {
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
        };

        return View(model);
    }

    // Procesar el formulario de inicio de sesión.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return ShowLogin(model);
        }

        var user = await _userManager.FindByEmailAsync(
            model.Email.Trim());

        // Usamos el mismo mensaje para no revelar si la cuenta existe.
        if (user is null || !user.IsActive)
        {
            AddLoginError();
            return ShowLogin(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            model.Password,
            isPersistent: model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectAfterLogin(model.ReturnUrl);
        }

        AddLoginError();
        return ShowLogin(model);
    }

    // Cerrar sesión mediante un formulario POST.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();

        return RedirectToAction(nameof(Login));
    }

    // Mostrar la pantalla de acceso restringido.
    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        return View();
    }

    private IActionResult RedirectAfterLogin(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }

        return RedirectToAction("Index", "Home");
    }

    private IActionResult ShowLogin(LoginViewModel model)
    {
        // No devolver la contraseña introducida al formulario.
        model.Password = string.Empty;

        if (ModelState.TryGetValue(
            nameof(LoginViewModel.Password), out var passwordEntry))
        {
            passwordEntry.RawValue = null;
            passwordEntry.AttemptedValue = null;
        }

        return View("Login", model);
    }

    private void AddLoginError()
    {
        ModelState.AddModelError(
            string.Empty,
            "No se pudo iniciar sesión. Revisa tus credenciales. " +
            "Si el problema continúa, espera unos minutos " +
            "o contacta al administrador.");
    }
}