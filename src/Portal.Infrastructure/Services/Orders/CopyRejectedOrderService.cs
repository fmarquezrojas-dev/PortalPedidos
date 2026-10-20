using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Orders;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Orders;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Services.Orders;

public class CopyRejectedOrderService : ICopyRejectedOrderService
{
    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerEligibilityService _customerEligibility;

    public CopyRejectedOrderService(
        PortalDbContext db,
        ICurrentUser currentUser,
        ICustomerEligibilityService customerEligibility)
    {
        _db = db;
        _currentUser = currentUser;
        _customerEligibility = customerEligibility;
    }

    public async Task<CopyRejectedOrderPreviewDto?> GetPreviewAsync(
        Guid sourceOrderId,
        CancellationToken cancellationToken = default)
    {
        RequireCreator();

        var source = await FindVisibleSourceAsync(
            sourceOrderId,
            cancellationToken);

        if (source is null)
        {
            return null;
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        await LockCatalogAsync(cancellationToken);

        var preview = await BuildPreviewAsync(
            source,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return preview;
    }

    public async Task<Guid?> CreateAsync(
        Guid sourceOrderId,
        Guid draftId,
        long expectedCatalogVersion,
        DateOnly requestedDate,
        CancellationToken cancellationToken = default)
    {
        string userId = RequireCreator();

        if (draftId == Guid.Empty || draftId == sourceOrderId)
        {
            throw new CopyRejectedOrderException(
                "El identificador del nuevo borrador no es válido.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        // Serializar la creación de una copia con el mismo identificador.
        // Este recurso también coordina las operaciones de edición.
        string resource =
            $"PortalPedidos.EditDraft:{draftId:N}";

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @LockResult < 0
                THROW 51030, 'No se pudo bloquear el borrador.', 1;
            """,
            cancellationToken);

        var source = await FindVisibleSourceAsync(
            sourceOrderId,
            cancellationToken);

        if (source is null)
        {
            return null;
        }

        if (source.Status != OrderStatus.Rejected)
        {
            throw new CopyRejectedOrderException(
                "Solo se puede copiar un pedido rechazado.");
        }

        // Si este formulario ya creó el pedido, devolverlo
        // sin duplicar líneas, historial ni auditoría.
        var existing = await _db.SalesOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                order => order.Id == draftId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.ReplacesOrderId != sourceOrderId ||
                existing.OwnerUserId != userId)
            {
                throw new CopyRejectedOrderException(
                    "El identificador del borrador ya está en uso.");
            }

            await transaction.CommitAsync(cancellationToken);

            return existing.Id;
        }

        await LockCatalogAsync(cancellationToken);

        // Volver a validar con los datos actuales del catálogo.
        // BuildPreviewAsync utiliza la regla común de elegibilidad.
        var preview = await BuildPreviewAsync(
            source,
            cancellationToken);

        if (!preview.CanCopy)
        {
            throw new CopyRejectedOrderException(
                string.Join(" ", preview.Problems));
        }

        if (expectedCatalogVersion != preview.CatalogVersion)
        {
            throw new CopyRejectedOrderException(
                "El catálogo cambió desde que abriste esta pantalla. " +
                "Recarga la revisión y verifica los precios actuales.");
        }

        if (requestedDate < preview.TodayInPeru)
        {
            throw new CopyRejectedOrderException(
                "La fecha solicitada no puede ser anterior a hoy en Perú.");
        }

        // El nuevo pedido pertenece al usuario que realiza la copia.
        // Su equipo se obtiene de la membresía vigente en la base de datos.
        Guid? salesTeamId =
            await ResolveSalesTeamForNewOrderAsync(
                userId,
                cancellationToken);

        DateTime now = DateTime.UtcNow;
        Guid correlationId = Guid.NewGuid();

        var draft = new SalesOrder
        {
            Id = draftId,
            OwnerUserId = userId,
            SalesTeamId = salesTeamId,

            CustomerId = source.CustomerId,
            CustomerCode = preview.CustomerCode,
            CustomerName = preview.CustomerName,

            RequestedDate = requestedDate,
            Currency = "PEN",
            Status = OrderStatus.Draft,

            Subtotal = preview.NewSubtotal,
            CatalogVersion = preview.CatalogVersion,

            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            SubmittedAtUtc = null,

            Provider = source.Provider,
            ExternalOrderId = null,

            ReplacesOrderId = source.Id
        };

        // PublicNumber lo genera la secuencia de SQL Server.
        _db.SalesOrders.Add(draft);

        int lineNumber = 1;

        foreach (var line in preview.Lines)
        {
            if (!line.CurrentUnitPrice.HasValue ||
                !line.CurrentLineAmount.HasValue)
            {
                throw new CopyRejectedOrderException(
                    "Uno de los productos no tiene un precio " +
                    "o importe válido. Recarga la revisión.");
            }

            _db.SalesOrderLines.Add(new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = draft.Id,
                LineNumber = lineNumber++,

                ProductId = line.ProductId,
                ItemCode = line.ItemCode,
                Description = line.Description,

                Quantity = line.Quantity,
                UnitPrice = line.CurrentUnitPrice.Value,
                LineAmount = line.CurrentLineAmount.Value,

                WarehouseCode = "01"
            });
        }

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = draft.Id,
            PreviousStatus = null,
            NewStatus = OrderStatus.Draft,

            ActorUserId = userId,

            Reason =
                $"Borrador creado desde el pedido rechazado " +
                $"#{source.PublicNumber}.",

            OccurredAtUtc = now,
            CorrelationId = correlationId
        });

        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = userId,
            ActorType = "User",
            Action = "Order.CopiedFromRejected",

            EntityType = "SalesOrder",
            EntityId = draft.Id.ToString("D"),

            OccurredAtUtc = now,
            CorrelationId = correlationId,

            SummaryJson = JsonSerializer.Serialize(new
            {
                sourceOrderId = source.Id,
                sourcePublicNumber = source.PublicNumber,
                sourceSalesTeamId = source.SalesTeamId,

                newOrderId = draft.Id,
                ownerUserId = draft.OwnerUserId,
                salesTeamId = draft.SalesTeamId,

                catalogVersion = preview.CatalogVersion,
                pricesChanged = preview.PricesChanged,
                originalSubtotal = source.Subtotal,
                newSubtotal = draft.Subtotal
            })
        });

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return draft.Id;
    }

    private string RequireCreator()
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            !_currentUser.CanCreateOrders ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException(
                "No tienes permisos para crear pedidos.");
        }

        return userId;
    }

    private async Task<SalesOrder?> FindVisibleSourceAsync(
        Guid sourceOrderId,
        CancellationToken cancellationToken)
    {
        var query = _db.SalesOrders
            .AsNoTracking()
            .Where(order => order.Id == sourceOrderId);

        // Consultar pedidos de un equipo no concede permiso
        // para copiar pedidos ajenos.
        if (!_currentUser.CanViewAllOrders)
        {
            string? userId = _currentUser.UserId;

            query = query.Where(order =>
                order.OwnerUserId == userId);
        }

        return await query.SingleOrDefaultAsync(
            cancellationToken);
    }

    private async Task<Guid?> ResolveSalesTeamForNewOrderAsync(
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException(
                "El propietario del pedido no es válido.",
                nameof(ownerUserId));
        }

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "La asignación del equipo debe ejecutarse " +
                "dentro de la transacción de creación del pedido.");
        }

        // Coordinar la lectura con las modificaciones realizadas
        // por SalesTeamService, que usa este recurso en modo Exclusive.
        await _db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'PortalPedidos.SalesTeams',
                @LockMode = 'Shared',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @LockResult < 0
                THROW 51032, 'No se pudo consultar el equipo comercial.', 1;
            """,
            cancellationToken);

        Guid? salesTeamId = await (
            from membership in _db.SalesTeamMembers.AsNoTracking()
            join team in _db.SalesTeams.AsNoTracking()
                on membership.SalesTeamId equals team.Id
            where membership.UserId == ownerUserId
                  && membership.LeftAtUtc == null
                  && team.IsActive
            select (Guid?)team.Id
        ).SingleOrDefaultAsync(cancellationToken);

        return salesTeamId;
    }

    private async Task LockCatalogAsync(
        CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'PortalPedidos.CatalogSync',
                @LockMode = 'Shared',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @LockResult < 0
                THROW 51031, 'No se pudo consultar el catálogo.', 1;
            """,
            cancellationToken);
    }

    private async Task<CopyRejectedOrderPreviewDto> BuildPreviewAsync(
        SalesOrder source,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;

        // Para el alcance actual, Perú utiliza UTC-05.
        DateOnly todayInPeru = DateOnly.FromDateTime(
            now.AddHours(-5));

        var preview = new CopyRejectedOrderPreviewDto
        {
            SourceOrderId = source.Id,
            SourcePublicNumber = source.PublicNumber,

            CustomerCode = source.CustomerCode,
            CustomerName = source.CustomerName,
            Currency = source.Currency,

            OriginalSubtotal = source.Subtotal,
            TodayInPeru = todayInPeru
        };

        if (source.Status != OrderStatus.Rejected)
        {
            preview.Problems.Add(
                "El pedido original no está rechazado.");
        }

        if (source.Currency != "PEN")
        {
            preview.Problems.Add(
                "Esta versión del portal solo permite pedidos en PEN.");
        }

        var catalog = await _db.CatalogSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                state => state.Id == 1,
                cancellationToken);

        preview.CatalogVersion =
            catalog?.CurrentVersion ?? 0;

        if (catalog is null ||
            catalog.CurrentVersion <= 0 ||
            !catalog.LastSuccessAtUtc.HasValue)
        {
            preview.Problems.Add(
                "Se necesita una sincronización correcta del catálogo.");
        }
        else
        {
            DateTime lastSuccess = DateTime.SpecifyKind(
                catalog.LastSuccessAtUtc.Value,
                DateTimeKind.Utc);

            if (lastSuccess < now.AddMinutes(-5) ||
                lastSuccess > now.AddMinutes(1))
            {
                preview.Problems.Add(
                    "El catálogo no está vigente. " +
                    "Espera una sincronización correcta y recarga.");
            }
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == source.CustomerId,
                cancellationToken);

        // Aplicar la misma regla utilizada por el selector
        // y las operaciones de creación, edición y envío.
        var customerEligibility =
            _customerEligibility.Evaluate(customer);

        if (customer is null || !customerEligibility.IsEligible)
        {
            preview.Problems.Add(
                customerEligibility.Message
                ?? "El cliente no está disponible para copiar el pedido.");
        }
        else
        {
            preview.CustomerCode = customer.ExternalCode;
            preview.CustomerName = customer.Name;

            // La elegibilidad no reemplaza la comprobación
            // de integridad de los datos del catálogo.
            if (customer.CatalogVersion != preview.CatalogVersion ||
                string.IsNullOrWhiteSpace(customer.ExternalCode))
            {
                preview.Problems.Add(
                    "El cliente no tiene datos válidos " +
                    "en la versión actual del catálogo.");
            }
        }

        var originalLines = await _db.SalesOrderLines
            .AsNoTracking()
            .Where(line => line.OrderId == source.Id)
            .OrderBy(line => line.LineNumber)
            .ToListAsync(cancellationToken);

        if (originalLines.Count == 0 ||
            originalLines.Count > 100)
        {
            preview.Problems.Add(
                "El pedido debe contener entre 1 y 100 productos.");
        }

        var productIds = originalLines
            .Select(line => line.ProductId)
            .Distinct()
            .ToArray();

        if (productIds.Length != originalLines.Count)
        {
            preview.Problems.Add(
                "El pedido original contiene productos repetidos.");
        }

        var products = await _db.Products
            .AsNoTracking()
            .Where(product =>
                productIds.Contains(product.Id))
            .ToDictionaryAsync(
                product => product.Id,
                cancellationToken);

        foreach (var original in originalLines)
        {
            var line = new CopyRejectedOrderLineDto
            {
                ProductId = original.ProductId,
                ItemCode = original.ItemCode,
                Description = original.Description,
                Quantity = original.Quantity,
                OriginalUnitPrice = original.UnitPrice
            };

            if (original.Quantity < 1 ||
                original.Quantity > 9999 ||
                decimal.Truncate(original.Quantity) != original.Quantity)
            {
                preview.Problems.Add(
                    $"El producto {original.ItemCode} " +
                    "tiene una cantidad inválida.");
            }

            if (original.WarehouseCode != "01")
            {
                preview.Problems.Add(
                    $"El producto {original.ItemCode} " +
                    "no pertenece al almacén 01.");
            }

            if (!products.TryGetValue(
                    original.ProductId,
                    out var product) ||
                !product.IsActive)
            {
                preview.Problems.Add(
                    $"El producto {original.ItemCode} " +
                    "ya no está activo.");
            }
            else
            {
                bool validPrice =
                    product.UnitPrice >= 0 &&
                    product.UnitPrice <= 999999.9999m &&
                    decimal.Round(product.UnitPrice, 4) ==
                        product.UnitPrice;

                bool validCatalogEntry =
                    product.CatalogVersion == preview.CatalogVersion &&
                    !string.IsNullOrWhiteSpace(product.ExternalCode);

                if (!validPrice || !validCatalogEntry)
                {
                    preview.Problems.Add(
                        $"El producto {original.ItemCode} " +
                        "no tiene precio o versión de catálogo válidos.");
                }
                else
                {
                    line.ItemCode = product.ExternalCode;
                    line.Description = product.Name;
                    line.CurrentUnitPrice = product.UnitPrice;

                    line.CurrentLineAmount = decimal.Round(
                        original.Quantity * product.UnitPrice,
                        2,
                        MidpointRounding.AwayFromZero);
                }
            }

            preview.Lines.Add(line);
        }

        preview.NewSubtotal = preview.Lines.Sum(
            line => line.CurrentLineAmount ?? 0m);

        return preview;
    }
}