using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Portal.Application.Interfaces.Security;

namespace Portal.Web.Security;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal =>
        _httpContextAccessor.HttpContext?.User;

    public string? UserId =>
        Principal?.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    public bool CanViewOrders =>
        PortalAuthorization.HasPermission(
            Principal,
            AppPolicies.ViewOrders);

    public bool CanViewAllOrders =>
        PortalAuthorization.HasPermission(
            Principal,
            AppPolicies.ViewAllOrders);

    public bool CanViewTeamOrders =>
        PortalAuthorization.HasPermission(
            Principal,
            AppPolicies.ViewTeamOrders);

    public bool CanCreateOrders =>
        PortalAuthorization.HasPermission(
            Principal,
            AppPolicies.CreateOrders);
}