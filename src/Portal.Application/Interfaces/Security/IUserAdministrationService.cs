using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Security;
namespace Portal.Application.Interfaces.Security;
public interface IUserAdministrationService
{
    Task<ManagedUserPageDto> SearchAsync(string? search, bool? isActive, int pageNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);
    Task<ManagedUserEditDto?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<string> CreateAsync(CreateManagedUserRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(UpdateManagedUserRequest request, CancellationToken cancellationToken = default);
}
