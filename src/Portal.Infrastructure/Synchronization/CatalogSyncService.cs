using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Catalog;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Catalog;
using Portal.Application.Interfaces.Erp;
using Portal.Domain.Entities;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Synchronization;

public sealed class CatalogSyncService : ICatalogSyncService
{
    private readonly PortalDbContext _dbContext;
    private readonly IErpClient _erpClient;

    public CatalogSyncService(
        PortalDbContext dbContext,
        IErpClient erpClient)
    {
        _dbContext = dbContext;
        _erpClient = erpClient;
    }

    public async Task<CatalogSyncResultDto> SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        // La llamada HTTP se realiza antes de abrir la transacción SQL.
        var snapshot = await _erpClient.GetCatalogAsync(
            cancellationToken);

        ErpCatalogValidator.Validate(snapshot);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Impedir que dos sincronizaciones escriban simultáneamente.
        const string lockSql = """
            DECLARE @result int;

            EXEC @result = sys.sp_getapplock
                @Resource = N'PortalPedidos.CatalogSync',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 0;

            IF @result < 0
            BEGIN
                THROW 51001, 'Catalog synchronization lock unavailable.', 1;
            END;
            """;

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                lockSql,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 51001)
        {
            throw new ErpClientException(
                "CATALOG_SYNC_BUSY",
                "Otra sincronización está guardando el catálogo. " +
                "Inténtalo nuevamente cuando termine.");
        }

        var syncState = await _dbContext.CatalogSyncStates
            .SingleOrDefaultAsync(
                state => state.Id == 1,
                cancellationToken);

        if (syncState is not null &&
            snapshot.CatalogVersion < syncState.CurrentVersion)
        {
            throw new ErpClientException(
                "CATALOG_OLDER_VERSION",
                "El ERP devolvió una versión anterior " +
                "a la que ya tiene el portal.");
        }

        var customers = await _dbContext.Customers
            .ToListAsync(cancellationToken);

        var products = await _dbContext.Products
            .ToListAsync(cancellationToken);

        var inventories = await _dbContext.InventorySnapshots
            .ToListAsync(cancellationToken);

        // Evitar que una respuesta que llegó tarde sobrescriba
        // una lectura más reciente de la misma versión.
        DateTime? latestObservation = inventories
            .Select(stock => (DateTime?)stock.ObservedAtUtc)
            .Max();

        if (latestObservation.HasValue &&
            snapshot.ObservedAtUtc < latestObservation.Value)
        {
            throw new ErpClientException(
                "CATALOG_OLDER_SNAPSHOT",
                "La lectura recibida es anterior " +
                "a las existencias ya sincronizadas.");
        }

        var customersByCode = customers.ToDictionary(
            customer => customer.ExternalCode,
            StringComparer.OrdinalIgnoreCase);

        var productsByCode = products.ToDictionary(
            product => product.ExternalCode,
            StringComparer.OrdinalIgnoreCase);

        var inventoriesByProduct = inventories.ToDictionary(
            stock => stock.ProductId);

        var receivedCustomerCodes = snapshot.Customers
            .Select(customer => customer.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var receivedProductCodes = snapshot.Items
            .Select(product => product.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Crear o actualizar clientes.
        foreach (var source in snapshot.Customers)
        {
            if (!customersByCode.TryGetValue(
                    source.Code,
                    out var customer))
            {
                customer = new Customer
                {
                    Id = Guid.NewGuid(),
                    ExternalCode = source.Code
                };

                _dbContext.Customers.Add(customer);
                customersByCode.Add(source.Code, customer);
            }

            customer.Name = source.Name;
            customer.IsActive = source.Active;
            customer.UpdatedAtUtc = snapshot.ObservedAtUtc;
            customer.CatalogVersion = snapshot.CatalogVersion;
        }

        // El endpoint entrega el catálogo completo:
        // los clientes ausentes se conservan, pero se desactivan.
        foreach (var customer in customers)
        {
            if (!receivedCustomerCodes.Contains(customer.ExternalCode))
            {
                customer.IsActive = false;
                customer.UpdatedAtUtc = snapshot.ObservedAtUtc;
                customer.CatalogVersion = snapshot.CatalogVersion;
            }
        }

        // Crear o actualizar productos.
        foreach (var source in snapshot.Items)
        {
            if (!productsByCode.TryGetValue(
                    source.Code,
                    out var product))
            {
                product = new Product
                {
                    Id = Guid.NewGuid(),
                    ExternalCode = source.Code
                };

                _dbContext.Products.Add(product);
                productsByCode.Add(source.Code, product);
            }

            product.Name = source.Name;
            product.UnitCode = source.UnitCode;
            product.UnitPrice = source.UnitPrice;
            product.IsActive = source.Active;
            product.UpdatedAtUtc = snapshot.ObservedAtUtc;
            product.CatalogVersion = snapshot.CatalogVersion;
        }

        // Conservar productos ausentes para no romper referencias
        // de pedidos que puedan existir posteriormente.
        foreach (var product in products)
        {
            if (!receivedProductCodes.Contains(product.ExternalCode))
            {
                product.IsActive = false;
                product.UpdatedAtUtc = snapshot.ObservedAtUtc;
                product.CatalogVersion = snapshot.CatalogVersion;

                // Ya no contamos con una lectura válida de su stock.
                if (inventoriesByProduct.TryGetValue(
                        product.Id,
                        out var oldInventory))
                {
                    _dbContext.InventorySnapshots.Remove(oldInventory);
                }
            }
        }

        // Crear o actualizar existencias.
        foreach (var source in snapshot.Stocks)
        {
            var product = productsByCode[source.ItemCode];

            if (!inventoriesByProduct.TryGetValue(
                    product.Id,
                    out var inventory))
            {
                inventory = new InventorySnapshot
                {
                    ProductId = product.Id
                };

                _dbContext.InventorySnapshots.Add(inventory);
                inventoriesByProduct.Add(product.Id, inventory);
            }

            inventory.WarehouseCode = source.WarehouseCode;
            inventory.Available = source.Available;
            inventory.ObservedAtUtc = snapshot.ObservedAtUtc;
            inventory.CatalogVersion = snapshot.CatalogVersion;
        }

        // El estado se guarda dentro de la misma transacción.
        if (syncState is null)
        {
            syncState = new CatalogSyncState
            {
                Id = 1
            };

            _dbContext.CatalogSyncStates.Add(syncState);
        }

        DateTime completedAtUtc = DateTime.UtcNow;

        syncState.CurrentVersion = snapshot.CatalogVersion;
        syncState.LastSuccessAtUtc = completedAtUtc;
        syncState.LastErrorCode = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new CatalogSyncResultDto
        {
            CatalogVersion = snapshot.CatalogVersion,
            CompletedAtUtc = completedAtUtc,
            CustomersReceived = snapshot.Customers.Count,
            ProductsReceived = snapshot.Items.Count,
            StocksReceived = snapshot.Stocks.Count
        };
    }
}