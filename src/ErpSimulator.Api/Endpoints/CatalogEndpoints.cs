using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ErpSimulator.Api.Contracts;
using ErpSimulator.Api.Persistence;

namespace ErpSimulator.Api.Endpoints;

public static class CatalogEndpoints
{
    public static async Task<IResult> GetAsync(
        ErpSimulatorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Las consultas forman parte de una misma lectura consistente.
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var state = await dbContext.CatalogStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                catalog => catalog.Id == 1,
                cancellationToken);

        if (state is null || state.CurrentVersion <= 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Catálogo no disponible",
                detail: "El catálogo del simulador no está inicializado.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "CATALOG_NOT_INITIALIZED"
                });
        }

        var customers = await dbContext.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Code)
            .Select(customer => new ErpCustomerDto
            {
                Code = customer.Code,
                Name = customer.Name,
                Active = customer.Active
            })
            .ToListAsync(cancellationToken);

        var items = await dbContext.Items
            .AsNoTracking()
            .OrderBy(item => item.Code)
            .Select(item => new ErpItemDto
            {
                Code = item.Code,
                Name = item.Name,
                UnitCode = item.UnitCode,
                UnitPrice = item.UnitPrice,
                Active = item.Active
            })
            .ToListAsync(cancellationToken);

        var stocks = await dbContext.Stocks
            .AsNoTracking()
            .Where(stock => stock.WarehouseCode == "01")
            .OrderBy(stock => stock.ItemCode)
            .Select(stock => new ErpStockDto
            {
                ItemCode = stock.ItemCode,
                WarehouseCode = stock.WarehouseCode,
                Available = stock.OnHand - stock.Reserved
            })
            .ToListAsync(cancellationToken);

        var snapshot = new ErpCatalogSnapshotDto
        {
            CatalogVersion = state.CurrentVersion,
            ObservedAtUtc = DateTime.UtcNow,
            Currency = "PEN",
            WarehouseCode = "01",
            Customers = customers,
            Items = items,
            Stocks = stocks
        };

        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(snapshot);
    }
}