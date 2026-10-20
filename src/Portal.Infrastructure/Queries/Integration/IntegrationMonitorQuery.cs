using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Integration;
using Portal.Application.Interfaces.Integration;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Enums;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Queries.Integration;

public sealed class IntegrationMonitorQuery
    : IIntegrationMonitorQuery
{
    private const int PageSize = 20;

    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;

    public IntegrationMonitorQuery(
        PortalDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IntegrationMonitorPageDto> SearchAsync(
        long? orderNumber,
        IntegrationStatus? status,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        RequireAccess();

        if (orderNumber.HasValue && orderNumber.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orderNumber));
        }

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var query = BuildQuery();

        if (orderNumber.HasValue)
        {
            query = query.Where(
                item => item.PublicNumber == orderNumber.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(
                item => item.Status == status.Value);
        }

        int totalCount = await query.CountAsync(
            cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalCount / PageSize));

        pageNumber = Math.Clamp(
            pageNumber,
            1,
            totalPages);

        var items = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.MessageId)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        return new IntegrationMonitorPageDto
        {
            OrderNumber = orderNumber,
            Status = status,
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = PageSize
        };
    }

    public async Task<IntegrationMonitorDetailDto?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        RequireAccess();

        var message = await BuildQuery()
            .SingleOrDefaultAsync(
                item => item.MessageId == messageId,
                cancellationToken);

        if (message is null)
        {
            return null;
        }

        var attempts = await _db.IntegrationAttempts
            .AsNoTracking()
            .Where(item => item.MessageId == messageId)
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .Take(50)
            .Select(item => new IntegrationAttemptItemDto
            {
                AttemptNumber = item.AttemptNumber,
                Operation = item.Operation,
                StartedAtUtc = item.StartedAtUtc,
                FinishedAtUtc = item.FinishedAtUtc,
                HttpStatus = item.HttpStatus,
                ResultCode = item.ResultCode,
                ErrorSummary = item.ErrorSummary,
                DurationMs = item.DurationMs,
                CorrelationId = item.CorrelationId
            })
            .ToListAsync(cancellationToken);

        return new IntegrationMonitorDetailDto
        {
            Message = message,
            Attempts = attempts
        };
    }

    private IQueryable<IntegrationMonitorItemDto> BuildQuery()
    {
        return
            from message in _db.OutboxMessages.AsNoTracking()
            join order in _db.SalesOrders.AsNoTracking()
                on message.OrderId equals order.Id
            select new IntegrationMonitorItemDto
            {
                MessageId = message.Id,
                OrderId = order.Id,
                PublicNumber = order.PublicNumber,
                CustomerName = order.CustomerName,
                Status = message.Status,
                AttemptCount = message.AttemptCount,
                RecordedHttpAttempts = _db.IntegrationAttempts.Count(attempt =>
                    attempt.MessageId == message.Id &&
                    (attempt.Operation == "Submit" || attempt.Operation == "Lookup")),
                RecoveryCycle = message.RecoveryCycle,
                CreatedAtUtc = message.CreatedAtUtc,
                NextAttemptAtUtc = message.NextAttemptAtUtc,
                LeaseUntilUtc = message.LeaseUntilUtc,
                CompletedAtUtc = message.CompletedAtUtc,
                LastErrorCode = message.LastErrorCode,
                ExternalOrderId = order.ExternalOrderId
            };
    }

    private void RequireAccess()
    {
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.CanViewAllOrders)
        {
            throw new UnauthorizedAccessException(
                "No tienes permiso para consultar las integraciones.");
        }
    }
}