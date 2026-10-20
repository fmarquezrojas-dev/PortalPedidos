using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ErpSimulator.Api.Entities;
using ErpSimulator.Api.Persistence;

namespace ErpSimulator.Api.Development;

public static class ErpCatalogSeeder
{
    public static async Task RunAsync(
        ErpSimulatorDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // Toda la preparación se confirma o revierte conjuntamente.
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var catalogState = await dbContext.CatalogStates
            .SingleOrDefaultAsync(
                state => state.Id == 1,
                cancellationToken);

        // Un catálogo inicializado no se sobrescribe.
        if (catalogState is not null &&
            catalogState.CurrentVersion > 0)
        {
            Console.WriteLine(
                "El catálogo ya está inicializado. " +
                "No se modificaron clientes, precios ni existencias.");

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // Evitar mezclar la semilla con datos cargados por otro medio.
        bool hasCustomers = await dbContext.Customers
            .AnyAsync(cancellationToken);

        bool hasItems = await dbContext.Items
            .AnyAsync(cancellationToken);

        bool hasStocks = await dbContext.Stocks
            .AnyAsync(cancellationToken);

        if (hasCustomers || hasItems || hasStocks)
        {
            throw new InvalidOperationException(
                "Existen datos, pero el catálogo no está inicializado. " +
                "Revisa la base antes de continuar. " +
                "No se eliminaron ni sobrescribieron registros.");
        }

        // Clientes ficticios de demostración.
        (string Code, string Name, bool Active)[] customers =
        {
            ("C001", "Ferretería Central", true),
            ("C002", "Suministros Sur", true),
            ("C003", "Comercial Norte", true),
            ("C004", "Ferretería El Constructor", true),
            ("C005", "Distribuidora Callao", true),
            ("C006", "Materiales Los Olivos", true),
            ("C007", "Ferretería San Miguel", true),
            ("C008", "Comercial La Unión", true),
            ("C009", "Suministros del Pacífico", true),
            ("C010", "Cliente de prueba inactivo", false)
        };

        foreach (var customer in customers)
        {
            dbContext.Customers.Add(new ErpCustomer
            {
                Code = customer.Code,
                Name = customer.Name,
                Active = customer.Active
            });
        }

        // Precios ficticios antes de impuestos.
        // Los seis primeros productos corresponden al prototipo.
        (
            string Code,
            string Name,
            decimal Price,
            decimal Stock,
            bool Active
        )[] products =
        {
            ("P001", "Cemento Portland 42.5 kg",
                32.00m, 120m, true),

            ("P002", "Taladro percutor 650 W",
                180.00m, 8m, true),

            ("P003", "Tubo PVC 1/2 pulgada",
                12.00m, 64m, true),

            ("P004", "Pintura látex blanca",
                48.00m, 22m, true),

            ("P005", "Martillo de carpintero",
                25.00m, 12m, true),

            ("P006", "Disco de corte 4.5 pulgadas",
                8.00m, 30m, true),

            ("P007", "Destornillador plano",
                9.50m, 35m, true),

            ("P008", "Destornillador estrella",
                10.00m, 32m, true),

            ("P009", "Alicate universal",
                28.00m, 18m, true),

            ("P010", "Llave ajustable 10 pulgadas",
                35.00m, 16m, true),

            ("P011", "Cinta métrica 5 metros",
                18.00m, 40m, true),

            ("P012", "Nivel de aluminio",
                42.00m, 14m, true),

            ("P013", "Brocha 2 pulgadas",
                7.50m, 50m, true),

            ("P014", "Rodillo para pintura",
                16.00m, 28m, true),

            ("P015", "Bandeja para pintura",
                11.00m, 24m, true),

            ("P016", "Espátula metálica",
                8.50m, 36m, true),

            ("P017", "Lija para madera",
                2.00m, 100m, true),

            ("P018", "Cinta aislante",
                4.50m, 80m, true),

            ("P019", "Cinta de teflón",
                2.50m, 90m, true),

            ("P020", "Pegamento para PVC",
                14.00m, 25m, true),

            ("P021", "Codo PVC 1/2 pulgada",
                1.80m, 120m, true),

            ("P022", "Unión PVC 1/2 pulgada",
                1.50m, 110m, true),

            ("P023", "Llave de paso 1/2 pulgada",
                22.00m, 20m, true),

            ("P024", "Guantes de trabajo",
                12.00m, 45m, true),

            ("P025", "Lentes de seguridad",
                9.00m, 38m, true),

            ("P026", "Casco de seguridad",
                19.00m, 26m, true),

            ("P027", "Artículo de prueba de redondeo",
                12.3450m, 50m, true),

            ("TEST-LAST", "Prueba de última unidad",
                10.00m, 1m, true),

            ("TEST-ZERO", "Prueba sin existencias",
                15.00m, 0m, true),

            ("TEST-INACTIVE", "Producto de prueba inactivo",
                20.00m, 10m, false)
        };

        foreach (var product in products)
        {
            dbContext.Items.Add(new ErpItem
            {
                Code = product.Code,
                Name = product.Name,
                UnitCode = "UND",
                UnitPrice = product.Price,
                Active = product.Active
            });

            dbContext.Stocks.Add(new ErpStock
            {
                ItemCode = product.Code,
                WarehouseCode = "01",
                OnHand = product.Stock,
                Reserved = 0m
            });
        }

        if (catalogState is null)
        {
            catalogState = new ErpCatalogState
            {
                Id = 1
            };

            dbContext.CatalogStates.Add(catalogState);
        }

        catalogState.CurrentVersion = 1;
        catalogState.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        Console.WriteLine("Catálogo inicializado correctamente.");
        Console.WriteLine($"Clientes creados: {customers.Length}");
        Console.WriteLine($"Productos creados: {products.Length}");
        Console.WriteLine($"Registros de stock creados: {products.Length}");
        Console.WriteLine("Almacén: 01");
        Console.WriteLine("Versión del catálogo: 1");
    }
}