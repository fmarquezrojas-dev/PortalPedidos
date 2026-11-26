namespace Portal.Application.Interfaces.Security;

public interface ICurrentUser
{
    string? UserId { get; }

    bool IsAuthenticated { get; }
    bool IsAdministrator { get; }

    bool CanViewOrders { get; }

    bool CanViewAllOrders { get; }

    bool CanViewTeamOrders { get; }

    bool CanCreateOrders { get; }
}
