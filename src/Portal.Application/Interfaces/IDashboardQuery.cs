using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Dashboard;

namespace Portal.Application.Interfaces.Dashboard;

public interface IDashboardQuery
{
    Task<DashboardDto> GetAsync(
        CancellationToken cancellationToken = default);
}