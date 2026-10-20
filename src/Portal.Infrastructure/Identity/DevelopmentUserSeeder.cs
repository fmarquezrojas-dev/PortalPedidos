using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Identity;

public static class DevelopmentUserSeeder
{
    public static async Task RunAsync(
        PortalDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        string email,
        string password,
        string roleName)
    {
        // El rol debe existir antes de crear el usuario.
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            throw new InvalidOperationException(
                $"El rol '{roleName}' no existe. Ejecuta primero --seed-roles.");
        }

        var existingUser = await userManager.FindByEmailAsync(email);

        if (existingUser is not null)
        {
            bool hasRole = await userManager.IsInRoleAsync(
                existingUser, roleName);

            if (!hasRole)
            {
                throw new InvalidOperationException(
                    $"El usuario '{email}' ya existe con otros permisos. " +
                    "Este proceso no modificará sus roles.");
            }

            Console.WriteLine(
                $"El usuario ya existe: {email}. No se modificó su contraseña.");

            return;
        }

        // La creación del usuario y su rol se guardarán juntas.
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            IsActive = true,
            LockoutEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        IdentityResult createResult =
            await userManager.CreateAsync(user, password);

        EnsureSucceeded(createResult, "No se pudo crear el usuario");

        IdentityResult roleResult =
            await userManager.AddToRoleAsync(user, roleName);

        EnsureSucceeded(roleResult, "No se pudo asignar el rol");

        await transaction.CommitAsync();

        Console.WriteLine(
            $"Usuario creado: {email} | Rol: {roleName}");
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        string errors = string.Join(
            "; ",
            result.Errors.Select(error => error.Description));

        throw new InvalidOperationException($"{message}: {errors}");
    }
}