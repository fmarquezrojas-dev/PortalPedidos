using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Portal.Infrastructure.Identity;

namespace Portal.Web.Security;

public static class ApplicationCookieValidator
{
    public static async Task ValidateAsync(
        CookieValidatePrincipalContext context)
    {
        // Conservar la validación de seguridad propia de Identity.
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        var principal = context.Principal;

        // Identity puede haber rechazado ya la sesión.
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.GetUserAsync(principal);

        if (user is null || !user.IsActive)
        {
            context.RejectPrincipal();
            context.ShouldRenew = false;

            await context.HttpContext.SignOutAsync(
                IdentityConstants.ApplicationScheme);
        }
    }
}