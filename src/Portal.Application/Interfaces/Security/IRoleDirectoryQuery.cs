using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Security;

namespace Portal.Application.Interfaces.Security;

public interface IRoleDirectoryQuery
{
    Task<IReadOnlyList<RoleDirectoryItemDto>> GetAsync(
        CancellationToken cancellationToken = default);
}