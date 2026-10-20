using System.Collections.Generic;
namespace Portal.Web.Formatting;

public static class AuditDisplay
{
    public static IReadOnlyDictionary<string, string> Actions { get; } = new Dictionary<string, string>
    {
        ["Order.DraftCreated"] = "Borrador creado",
        ["Order.DraftUpdated"] = "Borrador actualizado",
        ["Order.DraftCancelled"] = "Borrador cancelado",
        ["Order.SubmissionQueued"] = "Pedido enviado a integración",
        ["Order.CopiedFromRejected"] = "Copia de pedido rechazado creada",
        ["Order.ErpConfirmed"] = "Pedido confirmado por el ERP",
        ["Order.ErpRejected"] = "Pedido rechazado por el ERP",
        ["Integration.LookupNotFound"] = "Resultado aún no encontrado en el ERP",
        ["Integration.RecoveryRequested"] = "Recuperación de integración solicitada",
        ["Integration.Failed"] = "Fallo de comunicación registrado",
        ["Integration.Stopped"] = "Procesamiento automático detenido",
        ["CustomerPortfolio.Assigned"] = "Responsable comercial asignado",
        ["CustomerPortfolio.Reassigned"] = "Responsable comercial cambiado",
        ["CustomerPortfolio.Released"] = "Asignación comercial retirada",
        ["SalesTeam.Created"] = "Equipo comercial creado",
        ["SalesTeam.MemberAdded"] = "Vendedor incorporado al equipo",
        ["SalesTeam.MemberRemoved"] = "Vendedor retirado del equipo",
        ["User.Created"] = "Cuenta de usuario creada",
        ["User.AccessUpdated"] = "Accesos del usuario actualizados"
    };
    public static string Action(string code) => Actions.TryGetValue(code, out var label) ? label : code;
    public static string Actor(string type) => type switch
    {
        "System" => "Proceso automático", "User" => "Usuario", _ => type
    };
    public static string Entity(string type) => type switch
    {
        "SalesOrder" => "Pedido", "OutboxMessage" => "Integración", "ApplicationUser" => "Cuenta de usuario",
        "Customer" => "Cliente", "CustomerAssignment" => "Asignación comercial",
        "SalesTeam" => "Equipo comercial", "SalesTeamMember" => "Miembro de equipo", _ => type
    };
    public static string Badge(string action) => action switch
    {
        "Order.ErpRejected" or "Integration.Stopped" or "Integration.Failed" => "bg-warning text-dark",
        "Order.ErpConfirmed" => "bg-success",
        "Order.DraftCancelled" or "CustomerPortfolio.Released" or "SalesTeam.MemberRemoved" => "bg-secondary",
        _ => "bg-primary"
    };
}
