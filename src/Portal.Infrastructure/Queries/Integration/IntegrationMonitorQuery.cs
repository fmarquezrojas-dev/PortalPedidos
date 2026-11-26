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

    public async Task<OperationalAttention> GetAttentionAsync(
        long? orderNumber, AttentionCategory? category, int pageNumber,
        CancellationToken cancellationToken = default)
    {
        RequireAccess();
        DateTime now = DateTime.UtcNow;
        DateTime cutoff = now - OperationalThresholds.PendingWarningAge;
        var open = BuildQuery().Where(item =>
            item.Status != IntegrationStatus.Rejected && item.Status != IntegrationStatus.Succeeded);
        if (orderNumber.HasValue)
        {
            open = open.Where(item => item.PublicNumber == orderNumber.Value);
        }

        var classified = open.Select(item => new
        {
            Item = item,
            Category = item.Status == IntegrationStatus.DeadLetter ? AttentionCategory.Review
                : item.Status == IntegrationStatus.Unknown ? AttentionCategory.Uncertain
                : item.Status == IntegrationStatus.Processing &&
                    (item.LeaseUntilUtc == null || item.LeaseUntilUtc <= now) ? AttentionCategory.ExpiredLease
                : item.CreatedAtUtc < cutoff ? AttentionCategory.Delayed
                : AttentionCategory.Monitoring
        });

        var counts = await classified.GroupBy(row => row.Category)
            .Select(group => new { Category = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Category, row => row.Count, cancellationToken);
        int total = category.HasValue ? counts.GetValueOrDefault(category.Value) : counts.Values.Sum();
        int pages = Math.Max(1, (int)Math.Ceiling((double)total / PageSize));
        pageNumber = Math.Clamp(pageNumber, 1, pages);
        if (category.HasValue)
        {
            classified = classified.Where(row => row.Category == category.Value);
        }

        var items = await classified.OrderBy(row => row.Category)
            .ThenBy(row => row.Item.CreatedAtUtc).ThenBy(row => row.Item.MessageId)
            .Skip((pageNumber - 1) * PageSize).Take(PageSize)
            .Select(row => new OperationalItem(
                row.Item.MessageId, row.Item.PublicNumber, row.Item.CustomerName, row.Item.Status,
                row.Category, row.Item.CreatedAtUtc,
                _db.IntegrationAttempts.Where(a => a.MessageId == row.Item.MessageId &&
                        (a.Operation == "Submit" || a.Operation == "Lookup"))
                    .OrderByDescending(a => a.AttemptNumber)
                    .Select(a => (DateTime?)a.StartedAtUtc).FirstOrDefault(),
                row.Item.NextAttemptAtUtc, row.Item.LeaseUntilUtc, row.Item.CompletedAtUtc,
                row.Item.LastErrorCode, row.Item.ExternalOrderId))
            .ToListAsync(cancellationToken);
        var catalog = await _db.CatalogSyncStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.Id == 1, cancellationToken);

        return new OperationalAttention(now, orderNumber, category, counts, total, pageNumber, pages, items, catalog);
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
