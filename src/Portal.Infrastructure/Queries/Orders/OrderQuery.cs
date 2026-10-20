using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Portal.Infrastructure.Queries.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Orders;
using Portal.Application.Interfaces.Orders;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Security;

namespace Portal.Infrastructure.Queries.Orders;

public sealed class OrderQuery : IOrderQuery
{
    private readonly PortalDbContext _dbContext;
    private readonly ICurrentUser _currentUser;

    public OrderQuery(
        PortalDbContext dbContext,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    private IQueryable<SalesOrder> VisibleOrders()
    {
        // Compartir el mismo alcance de acceso con el Panel.
        return OrderVisibility.ForUser(
            _dbContext,
            _currentUser);
    }

    public Task<OrderListPageDto> SearchAsync(string? search, OrderStatus? status,
        int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        => SearchAsync(new OrderSearchFilterDto { Search = search, Status = status },
            pageNumber, pageSize, cancellationToken);

    private static readonly Expression<Func<SalesOrder, OrderListItemDto>> ListProjection = order => new OrderListItemDto
    {
        Id = order.Id, PublicNumber = order.PublicNumber, CustomerCode = order.CustomerCode,
        CustomerName = order.CustomerName, Status = order.Status, Subtotal = order.Subtotal,
        Currency = order.Currency, CreatedAtUtc = order.CreatedAtUtc
    };

    private IQueryable<SalesOrder> FilteredOrders(OrderSearchFilterDto filters)
    {
        // El alcance se aplica antes de cualquier búsqueda, exportación o paginación.
        var query = VisibleOrders();
        ArgumentNullException.ThrowIfNull(filters);
        string search = filters.Search?.Trim() ?? string.Empty;
        if (search.Length > 100) throw new ArgumentException("La búsqueda no puede superar los 100 caracteres.");
        if (filters.Status.HasValue && !Enum.IsDefined(typeof(OrderStatus), filters.Status.Value))
            throw new ArgumentException("El estado del pedido no es válido.");
        var range = PeruDateRange.Bounds(filters.From, filters.To);
        if (range.FromUtc.HasValue) query = query.Where(order => order.CreatedAtUtc >= range.FromUtc.Value);
        if (range.UntilUtc.HasValue) query = query.Where(order => order.CreatedAtUtc < range.UntilUtc.Value);
        if (search.Length > 0)
        {
            bool hasNumber = long.TryParse(search.TrimStart('#'), NumberStyles.None,
                CultureInfo.InvariantCulture, out long number);
            query = query.Where(order => order.CustomerCode.Contains(search) ||
                order.CustomerName.Contains(search) || (hasNumber && order.PublicNumber == number));
        }
        if (filters.Status.HasValue) query = query.Where(order => order.Status == filters.Status.Value);
        return query;
    }

    public async Task<OrderExportDto> ExportAsync(OrderSearchFilterDto filters,
        CancellationToken cancellationToken = default)
    {
        var rows = await FilteredOrders(filters).OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id).Select(ListProjection)
            .Take(OrderExportDto.MaximumRows + 1).ToListAsync(cancellationToken);
        return rows.Count > OrderExportDto.MaximumRows
            ? new OrderExportDto { LimitExceeded = true }
            : new OrderExportDto { Items = rows };
    }

    public async Task<OrderListPageDto> SearchAsync(OrderSearchFilterDto filters,
        int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = FilteredOrders(filters);
        string normalizedSearch = filters.Search?.Trim() ?? string.Empty;
        OrderStatus? status = filters.Status;

        pageSize = Math.Clamp(pageSize, 1, 100);

        int totalCount = await query.CountAsync(
            cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                totalCount / (double)pageSize));

        pageNumber = Math.Clamp(
            pageNumber,
            1,
            totalPages);

        var items = await query
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ListProjection)
            .ToListAsync(cancellationToken);

        bool showsAllOrders =
            _currentUser.CanViewAllOrders;

        bool showsTeamOrders =
            !showsAllOrders &&
            _currentUser.CanViewTeamOrders;

        return new OrderListPageDto
        {
            Search = normalizedSearch,
            Status = status,
            From = filters.From,
            To = filters.To,
            Items = items,

            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,

            ShowsAllOrders = showsAllOrders,
            ShowsTeamOrders = showsTeamOrders
        };
    }

    public async Task<OrderDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Aplicar la misma autorización que en el listado,
        // también cuando se accede mediante una URL directa.
        var query = VisibleOrders();

        if (id == Guid.Empty)
        {
            return null;
        }

        var order = await query
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        // No devolver información de un pedido fuera del alcance.
        if (order is null)
        {
            return null;
        }

        string? salesTeamCode = null;
        string? salesTeamName = null;

        if (order.SalesTeamId is Guid salesTeamId)
        {
            var team = await _dbContext.SalesTeams
                .AsNoTracking()
                .Where(item => item.Id == salesTeamId)
                .Select(item => new
                {
                    item.Code,
                    item.Name
                })
                .SingleOrDefaultAsync(cancellationToken);

            salesTeamCode = team?.Code;
            salesTeamName = team?.Name;
        }

        var lines = await _dbContext.SalesOrderLines
            .AsNoTracking()
            .Where(line => line.OrderId == order.Id)
            .OrderBy(line => line.LineNumber)
            .Select(line => new OrderLineDto
            {
                LineNumber = line.LineNumber,
                ItemCode = line.ItemCode,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineAmount = line.LineAmount,
                WarehouseCode = line.WarehouseCode
            })
            .ToListAsync(cancellationToken);

        var history = await _dbContext.OrderStatusHistories
            .AsNoTracking()
            .Where(entry => entry.OrderId == order.Id)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new OrderHistoryItemDto
            {
                PreviousStatus = entry.PreviousStatus,
                NewStatus = entry.NewStatus,
                Reason = entry.Reason,
                OccurredAtUtc = entry.OccurredAtUtc
            })
            .ToListAsync(cancellationToken);

        return new OrderDetailDto
        {
            Order = new OrderListItemDto
            {
                Id = order.Id,
                PublicNumber = order.PublicNumber,
                CustomerCode = order.CustomerCode,
                CustomerName = order.CustomerName,
                Status = order.Status,
                Subtotal = order.Subtotal,
                Currency = order.Currency,
                CreatedAtUtc = order.CreatedAtUtc
            },

            SalesTeamId = order.SalesTeamId,
            SalesTeamCode = salesTeamCode,
            SalesTeamName = salesTeamName,

            RequestedDate = order.RequestedDate,
            UpdatedAtUtc = order.UpdatedAtUtc,
            SubmittedAtUtc = order.SubmittedAtUtc,

            Provider = order.Provider,
            ExternalOrderId = order.ExternalOrderId,

            Lines = lines,
            History = history
        };
    }
}
