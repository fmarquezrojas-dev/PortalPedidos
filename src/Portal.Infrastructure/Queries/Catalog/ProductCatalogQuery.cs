using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Catalog;
using Portal.Application.Interfaces.Catalog;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Common;

namespace Portal.Infrastructure.Queries.Catalog;

public sealed class ProductCatalogQuery : IProductCatalogQuery
{
    private readonly PortalDbContext _dbContext;

    public ProductCatalogQuery(PortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProductCatalogPageDto> SearchAsync(
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        string normalizedSearch = search?.Trim() ?? string.Empty;

        if (normalizedSearch.Length > 100)
        {
            throw new ArgumentException(
                "La búsqueda no puede superar los 100 caracteres.",
                nameof(search));
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            productsQuery = productsQuery.Matching(normalizedSearch);
        }

        int totalCount = await productsQuery.CountAsync(
            cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)pageSize));

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var itemsQuery =
            from product in productsQuery
            join stock in _dbContext.InventorySnapshots
                .AsNoTracking()
                .Where(stock => stock.WarehouseCode == "01")
                on product.Id equals stock.ProductId into stockGroup
            from stock in stockGroup.DefaultIfEmpty()
            orderby product.Name, product.Id
            select new ProductCatalogItemDto
            {
                Id = product.Id,
                Code = product.ExternalCode,
                Name = product.Name,
                UnitCode = product.UnitCode,
                UnitPrice = product.UnitPrice,
                AvailableStock = stock == null
                    ? (decimal?)null
                    : stock.Available,
                StockObservedAtUtc = stock == null
                    ? (DateTime?)null
                    : stock.ObservedAtUtc
            };

        var items = await itemsQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var syncState = await _dbContext.CatalogSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                state => state.Id == 1,
                cancellationToken);

        DateTime? catalogObservedAtUtc = await _dbContext
            .InventorySnapshots
            .AsNoTracking()
            .Where(stock => stock.WarehouseCode == "01")
            .Select(stock => (DateTime?)stock.ObservedAtUtc)
            .MinAsync(cancellationToken);

        return new ProductCatalogPageDto
        {
            Search = normalizedSearch,
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            CatalogVersion = syncState?.CurrentVersion ?? 0,
            LastSuccessAtUtc = syncState?.LastSuccessAtUtc,
            CatalogObservedAtUtc = catalogObservedAtUtc,
            LastErrorCode = syncState?.LastErrorCode
        };
    }
}