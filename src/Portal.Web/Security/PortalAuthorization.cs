using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Portal.Infrastructure.Identity;

namespace Portal.Web.Security;

public static class PortalAuthorization
{
    private static readonly Dictionary<string, string[]> PolicyRoles =
        new(StringComparer.Ordinal)
        {
            // Administración.
            [AppPolicies.ManageUsers] = new[] { AppRoles.Admin },
            [AppPolicies.ViewUsers] =
                new[]
                {
                    AppRoles.Admin
                },

            [AppPolicies.ViewRoles] =
                new[]
                {
                    AppRoles.Admin
                },

            // Consulta de pedidos.
            [AppPolicies.ViewOrders] =
    new[]
    {
        AppRoles.Seller,
        AppRoles.SalesSupervisor,
        AppRoles.Operator,
        AppRoles.Admin
    },

            [AppPolicies.ViewTeamOrders] =
    new[]
    {
        AppRoles.SalesSupervisor
    },

            [AppPolicies.ViewAllOrders] =
                new[]
                {
                    AppRoles.Operator,
                    AppRoles.Admin
                },

            // Operaciones sobre pedidos.
            [AppPolicies.CreateOrders] =
                new[]
                {
                    AppRoles.Seller,
                    AppRoles.Admin
                },

            [AppPolicies.EditOrders] =
                new[]
                {
                    AppRoles.Seller,
                    AppRoles.Admin
                },

            [AppPolicies.CancelOrders] =
                new[]
                {
                    AppRoles.Seller,
                    AppRoles.Admin
                },

            [AppPolicies.SubmitOrders] =
                new[]
                {
                    AppRoles.Seller,
                    AppRoles.Admin
                },

            // Equipos comerciales.
            [AppPolicies.ViewTeams] =
                new[]
                {
                    AppRoles.Admin,
                    AppRoles.SalesSupervisor,
                    AppRoles.Seller
                },

            [AppPolicies.CreateTeams] =
                new[]
                {
                    AppRoles.Admin
                },

            [AppPolicies.ManageTeams] =
                new[]
                {
                    AppRoles.Admin,
                    AppRoles.SalesSupervisor
                }
        };

    public static void Configure(
        AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var entry in PolicyRoles)
        {
            string policyName = entry.Key;
            string[] allowedRoles = entry.Value;

            options.AddPolicy(
                policyName,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(allowedRoles);
                });
        }
    }

    public static bool HasPermission(
        ClaimsPrincipal? principal,
        string policyName)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(policyName))
        {
            return false;
        }

        if (!PolicyRoles.TryGetValue(
                policyName,
                out string[]? allowedRoles))
        {
            return false;
        }

        return allowedRoles.Any(
            role => principal.IsInRole(role));
    }
}