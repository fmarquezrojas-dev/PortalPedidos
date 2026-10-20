namespace Portal.Web.Security;

public static class AppPolicies
{
    // Administración.
    public const string ViewUsers = "Users.View";
    public const string ManageUsers = "Users.Manage";

    public const string ViewRoles = "Roles.View";

    // Consulta de pedidos.
    public const string ViewOrders = "Orders.View";

    public const string ViewAllOrders = "Orders.ViewAll";

    // Operaciones comerciales.
    public const string CreateOrders = "Orders.Create";

    public const string EditOrders = "Orders.Edit";

    public const string CancelOrders = "Orders.Cancel";

    public const string SubmitOrders = "Orders.Submit";

    public const string ViewTeams = "Teams.View";

    public const string CreateTeams = "Teams.Create";

    public const string ManageTeams = "Teams.Manage";

    public const string ViewTeamOrders = "Orders.ViewTeam";
}