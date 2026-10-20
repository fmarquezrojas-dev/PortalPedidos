using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Portal.Infrastructure.Identity;

public static class DevelopmentRoleSeeder
{
    public static async Task RunAsync(
        RoleManager<IdentityRole> roleManager)
    {
        ArgumentNullException.ThrowIfNull(roleManager);

        foreach (string roleName in AppRoles.All)
        {
            bool exists = await roleManager.RoleExistsAsync(roleName);

            if (exists)
            {
                Console.WriteLine(
                    $"El rol ya existe: {roleName}");

                continue;
            }

            IdentityResult result = await roleManager.CreateAsync(
                new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                string errors = string.Join(
                    "; ",
                    result.Errors.Select(error => error.Description));

                throw new InvalidOperationException(
                    $"No se pudo crear el rol '{roleName}': {errors}");
            }

            Console.WriteLine(
                $"Rol creado: {roleName}");
        }
    }
}