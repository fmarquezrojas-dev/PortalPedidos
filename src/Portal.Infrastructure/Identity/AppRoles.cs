using System;
using System.Collections.Generic;

namespace Portal.Infrastructure.Identity;

public static class AppRoles
{
    // Identity utiliza estos nombres para las asignaciones de usuarios y las políticas.
    public const string Seller = "Vendedor";

    public const string Operator = "Operador";

    public const string Admin = "Administrador";

    public const string SalesSupervisor = "SupervisorComercial";

    public const string CommercialManagement = "GerenciaComercial";

    public const string Auditor = "Auditor";

    // Catálogo de roles reconocidos por PortalPedidos.
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[]
        {
            Admin,
            Seller,
            SalesSupervisor,
            Operator,
            CommercialManagement,
            Auditor
        });
}
