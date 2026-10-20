using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Orders;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Orders;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Infrastructure.Persistence;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Portal.Application.Interfaces.Customers;


namespace Portal.Infrastructure.Services.Orders;

public sealed class OrderDraftService : IOrderDraftService
{
    private readonly PortalDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerEligibilityService _customerEligibility;

    private static readonly TimeZoneInfo PeruTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "SA Pacific Standard Time"
                : "America/Lima");

    public OrderDraftService(
        PortalDbContext dbContext,
        ICurrentUser currentUser,
        ICustomerEligibilityService customerEligibility)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _customerEligibility = customerEligibility;
    }

    private string RequireCreator()
    {
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.CanCreateOrders ||
            string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            throw new UnauthorizedAccessException(
                "El usuario no tiene permiso para crear pedidos.");
        }

        return _currentUser.UserId!;
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

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "La asignación del equipo debe ejecutarse " +
                "dentro de la transacción de creación del pedido.");
        }

        // SalesTeamService utiliza este mismo recurso con un
        // bloqueo Exclusive al modificar equipos o integrantes.
        //
        // El bloqueo Shared permite crear varios pedidos a la vez,
        // pero evita que cambie la membresía mientras se atribuye
        // y guarda este pedido.
        await AcquireLockAsync(
            "PortalPedidos.SalesTeams",
            "Shared",
            cancellationToken);

        Guid? salesTeamId = await (
            from membership in _dbContext.SalesTeamMembers.AsNoTracking()
            join team in _dbContext.SalesTeams.AsNoTracking()
                on membership.SalesTeamId equals team.Id
            where membership.UserId == ownerUserId
                  && membership.LeftAtUtc == null
                  && team.IsActive
            select (Guid?)team.Id
        ).SingleOrDefaultAsync(cancellationToken);

        return salesTeamId;
    }

    private static DateOnly TodayInPeru()
    {
        DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            PeruTimeZone);

        return DateOnly.FromDateTime(localTime);
    }

    private async Task AcquireLockAsync(
        string resource,
        string mode,
        CancellationToken cancellationToken)
    {
        // Los valores interpolados se envían como parámetros SQL.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @result int;

            EXEC @result = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = {mode},
                @LockOwner = N'Transaction',
                @LockTimeout = 5000;

            IF @result < 0
            BEGIN
                THROW 51002, 'Order operation lock unavailable.', 1;
            END;
            """,
            cancellationToken);
    }

    public async Task<OrderDraftFormDataDto> GetFormDataAsync(
        CancellationToken cancellationToken = default)
    {
        RequireCreator();

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Compartido: permite otras lecturas y evita leer
        // un catálogo mientras la sincronización lo modifica.
        await AcquireLockAsync(
            "PortalPedidos.CatalogSync",
            "Shared",
            cancellationToken);

        var state = await _dbContext.CatalogSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == 1,
                cancellationToken);

        var customers = await _dbContext.Customers
            .AsNoTracking()
            .Where(_customerEligibility.GetEligibleCustomerPredicate())
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .Select(customer => new CustomerOptionDto
            {
                Id = customer.Id,
                Code = customer.ExternalCode,
                Name = customer.Name
            })
            .ToListAsync(cancellationToken);

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => new ProductOptionDto
            {
                Id = product.Id,
                Code = product.ExternalCode,
                Name = product.Name,
                UnitPrice = product.UnitPrice
            })
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new OrderDraftFormDataDto
        {
            CatalogVersion = state?.CurrentVersion ?? 0,
            TodayInPeru = TodayInPeru(),
            Customers = customers,
            Products = products
        };
    }

    public async Task<Guid> CreateAsync(
        CreateOrderDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        string userId = RequireCreator();

        if (request.DraftId == Guid.Empty)
        {
            throw new OrderDraftException(
                "El identificador del formulario no es válido.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Serializar los envíos del mismo formulario.
        await AcquireLockAsync(
            $"PortalPedidos.CreateDraft:{request.DraftId:N}",
            "Exclusive",
            cancellationToken);

        var existingOrder = await _dbContext.SalesOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                order => order.Id == request.DraftId,
                cancellationToken);

        if (existingOrder is not null)
        {
            if (existingOrder.OwnerUserId != userId)
            {
                throw new OrderDraftException(
                    "No se pudo utilizar este formulario. " +
                    "Abre nuevamente la opción Nuevo pedido.");
            }

            // El primer envío ya creó el pedido.
            // No crear otro ni modificar el existente.
            await transaction.CommitAsync(cancellationToken);

            return existingOrder.Id;
        }

        if (request.CustomerId == Guid.Empty)
        {
            throw new OrderDraftException(
                "Selecciona un cliente.");
        }

        if (request.RequestedDate < TodayInPeru())
        {
            throw new OrderDraftException(
                "La fecha solicitada no puede ser anterior a hoy.");
        }

        if (request.Lines is null ||
            request.Lines.Count < 1 ||
            request.Lines.Count > 100)
        {
            throw new OrderDraftException(
                "El pedido debe contener entre 1 y 100 líneas.");
        }

        if (request.Lines.Any(line =>
                line is null ||
                line.ProductId == Guid.Empty ||
                line.Quantity < 1 ||
                line.Quantity > 9999))
        {
            throw new OrderDraftException(
                "Cada línea debe tener un producto y una cantidad " +
                "entera entre 1 y 9999.");
        }

        var productIds = request.Lines
            .Select(line => line.ProductId)
            .ToArray();

        if (productIds.Distinct().Count() != productIds.Length)
        {
            throw new OrderDraftException(
                "No repitas un producto en varias líneas. " +
                "Agrupa su cantidad en una sola línea.");
        }

        await AcquireLockAsync(
            "PortalPedidos.CatalogSync",
            "Shared",
            cancellationToken);

        var state = await _dbContext.CatalogSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == 1,
                cancellationToken);

        if (state is null ||
            state.CurrentVersion <= 0 ||
            state.LastSuccessAtUtc is null)
        {
            throw new OrderDraftException(
                "Primero debe completarse una sincronización del catálogo.");
        }

        if (request.CatalogVersion != state.CurrentVersion)
        {
            throw new OrderDraftException(
                "El catálogo cambió mientras preparabas el pedido. " +
                "Revisa los productos y precios actualizados antes de guardar.");
        }

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.CustomerId,
                cancellationToken);

        var customerEligibility = _customerEligibility.Evaluate(customer);

        if (customer is null || !customerEligibility.IsEligible)
        {
            throw new OrderDraftException(
                customerEligibility.Message
                ?? "El cliente no está disponible para guardar el pedido.");
        }

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                productIds.Contains(product.Id) &&
                product.IsActive)
            .ToDictionaryAsync(
                product => product.Id,
                cancellationToken);

        if (products.Count != productIds.Length)
        {
            throw new OrderDraftException(
                "Uno o más productos ya no están disponibles en el catálogo.");
        }

        // Obtener el equipo desde la base de datos.
        // No se recibe desde el formulario del navegador.
        Guid? salesTeamId = await ResolveSalesTeamForNewOrderAsync(
            userId,
            cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;
        Guid correlationId = Guid.NewGuid();

        var order = new SalesOrder
        {
            Id = request.DraftId,
            OwnerUserId = userId,
            SalesTeamId = salesTeamId,
            CustomerId = customer.Id,
            CustomerCode = customer.ExternalCode,
            CustomerName = customer.Name,
            RequestedDate = request.RequestedDate,
            Currency = "PEN",
            Status = OrderStatus.Draft,
            CatalogVersion = state.CurrentVersion,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Provider = "Simulator",
            Subtotal = 0m
        };

        // PublicNumber lo genera la secuencia de SQL Server.
        _dbContext.SalesOrders.Add(order);

        int lineNumber = 1;

        foreach (var source in request.Lines)
        {
            var product = products[source.ProductId];

            decimal lineAmount = decimal.Round(
                product.UnitPrice * source.Quantity,
                2,
                MidpointRounding.AwayFromZero);

            var line = new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                LineNumber = lineNumber++,
                ProductId = product.Id,
                ItemCode = product.ExternalCode,
                Description = product.Name,
                Quantity = source.Quantity,
                UnitPrice = product.UnitPrice,
                LineAmount = lineAmount,
                WarehouseCode = "01"
            };

            _dbContext.SalesOrderLines.Add(line);

            order.Subtotal += lineAmount;
        }

        _dbContext.OrderStatusHistories.Add(
            new OrderStatusHistory
            {
                OrderId = order.Id,
                PreviousStatus = null,
                NewStatus = OrderStatus.Draft,
                ActorUserId = userId,
                Reason = "Borrador creado desde el portal.",
                OccurredAtUtc = nowUtc,
                CorrelationId = correlationId
            });

        _dbContext.AuditEvents.Add(
            new AuditEvent
            {
                ActorUserId = userId,
                ActorType = "User",
                Action = "Order.DraftCreated",
                EntityType = "SalesOrder",
                EntityId = order.Id.ToString(),
                OccurredAtUtc = nowUtc,
                CorrelationId = correlationId,
                SummaryJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    ownerUserId = order.OwnerUserId,
                    salesTeamId = order.SalesTeamId,
                    lineCount = request.Lines.Count,
                    subtotal = order.Subtotal,
                    currency = order.Currency
                })
            });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return order.Id;
    }

    public async Task<OrderDraftEditDto?> GetEditableAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        string userId = RequireCreator();

        var query = _dbContext.SalesOrders
            .AsNoTracking()
            .Where(order =>
                order.Id == id &&
                order.Status == OrderStatus.Draft);

        // RequireCreator excluye al operador.
        // Entre los roles restantes, solo el administrador puede ver todos.
        if (!_currentUser.CanViewAllOrders)
        {
            query = query.Where(order => order.OwnerUserId == userId);
        }

        var order = await query.SingleOrDefaultAsync(
            cancellationToken);

        if (order is null)
        {
            return null;
        }

        var lines = await _dbContext.SalesOrderLines
            .AsNoTracking()
            .Where(line => line.OrderId == order.Id)
            .OrderBy(line => line.LineNumber)
            .Select(line => new CreateOrderDraftLineRequest
            {
                ProductId = line.ProductId,
                Quantity = (int)line.Quantity
            })
            .ToListAsync(cancellationToken);

        return new OrderDraftEditDto
        {
            Id = order.Id,
            PublicNumber = order.PublicNumber,
            CustomerId = order.CustomerId,
            RequestedDate = order.RequestedDate,
            Subtotal = order.Subtotal,
            RowVersion = order.RowVersion,
            Lines = lines
        };
    }

    public async Task<bool> UpdateAsync(
    UpdateOrderDraftRequest request,
    CancellationToken cancellationToken = default)
    {
        string userId = RequireCreator();

        if (request.Id == Guid.Empty)
        {
            throw new OrderDraftException(
                "El identificador del pedido no es válido.");
        }

        if (request.RowVersion is null || request.RowVersion.Length != 8)
        {
            throw new OrderDraftException(
                "La versión del pedido no es válida. Recarga el formulario.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        await AcquireLockAsync(
            $"PortalPedidos.EditDraft:{request.Id:N}",
            "Exclusive",
            cancellationToken);

        var query = _dbContext.SalesOrders
            .Where(order => order.Id == request.Id);

        if (!_currentUser.CanViewAllOrders)
        {
            query = query.Where(order => order.OwnerUserId == userId);
        }

        var order = await query.SingleOrDefaultAsync(
            cancellationToken);

        if (order is null)
        {
            return false;
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw new OrderDraftException(
                "Este pedido ya no está en estado Borrador y no puede editarse.");
        }

        if (!order.RowVersion.SequenceEqual(request.RowVersion))
        {
            throw new OrderDraftConflictException();
        }

        if (!request.AcceptCurrentPrices)
        {
            throw new OrderDraftException(
                "Confirma que revisaste los precios actuales y el importe.");
        }

        if (request.CustomerId == Guid.Empty)
        {
            throw new OrderDraftException("Selecciona un cliente.");
        }

        if (request.RequestedDate < TodayInPeru())
        {
            throw new OrderDraftException(
                "La fecha solicitada no puede ser anterior a hoy.");
        }

        if (request.Lines is null ||
            request.Lines.Count < 1 ||
            request.Lines.Count > 100)
        {
            throw new OrderDraftException(
                "El pedido debe contener entre 1 y 100 líneas.");
        }

        if (request.Lines.Any(line =>
                line is null ||
                line.ProductId == Guid.Empty ||
                line.Quantity < 1 ||
                line.Quantity > 9999))
        {
            throw new OrderDraftException(
                "Cada línea debe tener un producto y una cantidad " +
                "entera entre 1 y 9999.");
        }

        var productIds = request.Lines
            .Select(line => line.ProductId)
            .ToArray();

        if (productIds.Distinct().Count() != productIds.Length)
        {
            throw new OrderDraftException(
                "No repitas un producto en varias líneas.");
        }

        await AcquireLockAsync(
            "PortalPedidos.CatalogSync",
            "Shared",
            cancellationToken);

        var state = await _dbContext.CatalogSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == 1,
                cancellationToken);

        if (state is null ||
            state.CurrentVersion <= 0 ||
            state.LastSuccessAtUtc is null)
        {
            throw new OrderDraftException(
                "No hay un catálogo sincronizado disponible.");
        }

        if (request.CatalogVersion != state.CurrentVersion)
        {
            throw new OrderDraftException(
                "El catálogo cambió mientras editabas. " +
                "Revisa los precios actualizados antes de guardar.");
        }

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.CustomerId,
                cancellationToken);

        var customerEligibility = _customerEligibility.Evaluate(customer);

        if (customer is null || !customerEligibility.IsEligible)
        {
            throw new OrderDraftException(
                customerEligibility.Message
                ?? "El cliente no está disponible para guardar el pedido.");
        }

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                productIds.Contains(product.Id) &&
                product.IsActive)
            .ToDictionaryAsync(
                product => product.Id,
                cancellationToken);

        if (products.Count != productIds.Length)
        {
            throw new OrderDraftException(
                "Uno o más productos están inactivos o ya no existen.");
        }

        var previousLines = await _dbContext.SalesOrderLines
            .Where(line => line.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        decimal previousSubtotal = order.Subtotal;
        Guid previousCustomerId = order.CustomerId;
        DateOnly previousRequestedDate = order.RequestedDate;
        int previousLineCount = previousLines.Count;

        DateTime nowUtc = DateTime.UtcNow;
        Guid correlationId = Guid.NewGuid();

        order.CustomerId = customer.Id;
        order.CustomerCode = customer.ExternalCode;
        order.CustomerName = customer.Name;
        order.RequestedDate = request.RequestedDate;
        order.CatalogVersion = state.CurrentVersion;
        order.UpdatedAtUtc = nowUtc;
        order.Subtotal = request.Lines.Sum(line =>
            decimal.Round(
                products[line.ProductId].UnitPrice * line.Quantity,
                2,
                MidpointRounding.AwayFromZero));

        // Usar como versión original la que recibió el usuario al abrir.
        _dbContext.Entry(order)
            .Property(item => item.RowVersion)
            .OriginalValue = request.RowVersion;

        // Asegurar una actualización de la cabecera incluso si los datos
        // introducidos coinciden con los que ya estaban guardados.
        _dbContext.Entry(order)
            .Property(item => item.UpdatedAtUtc)
            .IsModified = true;

        _dbContext.SalesOrderLines.RemoveRange(previousLines);

        // Primero actualizar la cabecera y retirar las líneas anteriores.
        // Todo sigue dentro de la misma transacción.
        await _dbContext.SaveChangesAsync(cancellationToken);

        int lineNumber = 1;

        foreach (var source in request.Lines)
        {
            var product = products[source.ProductId];

            _dbContext.SalesOrderLines.Add(new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                LineNumber = lineNumber++,
                ProductId = product.Id,
                ItemCode = product.ExternalCode,
                Description = product.Name,
                Quantity = source.Quantity,
                UnitPrice = product.UnitPrice,
                LineAmount = decimal.Round(
                    product.UnitPrice * source.Quantity,
                    2,
                    MidpointRounding.AwayFromZero),
                WarehouseCode = "01"
            });
        }

        _dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = userId,
            ActorType = "User",
            Action = "Order.DraftUpdated",
            EntityType = "SalesOrder",
            EntityId = order.Id.ToString(),
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            SummaryJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                previousCustomerId,
                customerId = order.CustomerId,
                previousRequestedDate,
                requestedDate = order.RequestedDate,
                previousSubtotal,
                subtotal = order.Subtotal,
                previousLineCount,
                lineCount = request.Lines.Count,
                catalogVersion = order.CatalogVersion,
                acceptedCurrentPrices = true
            })
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(
    CancelOrderDraftRequest request,
    CancellationToken cancellationToken = default)
    {
        string userId = RequireCreator();

        if (request.Id == Guid.Empty)
        {
            throw new OrderDraftException(
                "El identificador del pedido no es válido.");
        }

        if (request.RowVersion is null ||
            request.RowVersion.Length != 8)
        {
            throw new OrderDraftException(
                "La versión del pedido no es válida. " +
                "Abre nuevamente la confirmación de cancelación.");
        }

        string reason = request.Reason?.Trim() ?? string.Empty;

        if (reason.Length < 5 || reason.Length > 250)
        {
            throw new OrderDraftException(
                "El motivo debe tener entre 5 y 250 caracteres.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Usar el mismo bloqueo que la edición para coordinar
        // ambas operaciones sobre el mismo pedido.
        await AcquireLockAsync(
            $"PortalPedidos.EditDraft:{request.Id:N}",
            "Exclusive",
            cancellationToken);

        var query = _dbContext.SalesOrders
            .Where(order => order.Id == request.Id);

        // RequireCreator permite vendedor y administrador.
        // El vendedor solo puede operar sobre sus propios pedidos.
        if (!_currentUser.CanViewAllOrders)
        {
            query = query.Where(order =>
                order.OwnerUserId == userId);
        }

        var order = await query.SingleOrDefaultAsync(
            cancellationToken);

        if (order is null)
        {
            return false;
        }

        // Repetir una cancelación ya completada no genera otro evento.
        if (order.Status == OrderStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw new OrderDraftException(
                "Solo se pueden cancelar pedidos en estado Borrador.");
        }

        if (!order.RowVersion.SequenceEqual(request.RowVersion))
        {
            throw new OrderDraftConflictException();
        }

        DateTime nowUtc = DateTime.UtcNow;
        Guid correlationId = Guid.NewGuid();

        _dbContext.Entry(order)
            .Property(item => item.RowVersion)
            .OriginalValue = request.RowVersion;

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAtUtc = nowUtc;

        _dbContext.OrderStatusHistories.Add(
            new OrderStatusHistory
            {
                OrderId = order.Id,
                PreviousStatus = OrderStatus.Draft,
                NewStatus = OrderStatus.Cancelled,
                ActorUserId = userId,
                Reason = reason,
                OccurredAtUtc = nowUtc,
                CorrelationId = correlationId
            });

        _dbContext.AuditEvents.Add(
            new AuditEvent
            {
                ActorUserId = userId,
                ActorType = "User",
                Action = "Order.DraftCancelled",
                EntityType = "SalesOrder",
                EntityId = order.Id.ToString(),
                OccurredAtUtc = nowUtc,
                CorrelationId = correlationId,
                SummaryJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    previousStatus = OrderStatus.Draft.ToString(),
                    newStatus = OrderStatus.Cancelled.ToString(),
                    reason
                })
            });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<bool> QueueAsync(
    SubmitOrderDraftRequest request,
    CancellationToken cancellationToken = default)
    {
        string userId = RequireCreator();

        if (request.Id == Guid.Empty ||
            request.RowVersion is null ||
            request.RowVersion.Length != 8)
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "La solicitud no es válida. Abre nuevamente la confirmación.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Coordinar envío, edición y cancelación del mismo pedido.
        await AcquireLockAsync(
            $"PortalPedidos.EditDraft:{request.Id:N}",
            "Exclusive",
            cancellationToken);

        var query = _dbContext.SalesOrders
            .Where(order => order.Id == request.Id);

        if (!_currentUser.CanViewAllOrders)
        {
            query = query.Where(order => order.OwnerUserId == userId);
        }

        var order = await query.SingleOrDefaultAsync(
            cancellationToken);

        if (order is null)
        {
            return false;
        }

        var existingMessage = await _dbContext.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.OrderId == order.Id,
                cancellationToken);

        // Una repetición del mismo envío no crea otro mensaje.
        if ((order.Status == OrderStatus.Queued ||
             order.Status == OrderStatus.Confirmed) &&
            existingMessage is not null)
        {
            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw new OrderSubmissionException(
                "ORDER_NOT_DRAFT",
                "Solo se pueden enviar pedidos en estado Borrador.");
        }

        if (existingMessage is not null)
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "El pedido tiene un mensaje de integración incompatible " +
                "con su estado. Se requiere revisar la operación.");
        }

        if (!order.RowVersion.SequenceEqual(request.RowVersion))
        {
            throw new OrderDraftConflictException();
        }

        if (order.RequestedDate < TodayInPeru())
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "La fecha solicitada quedó en el pasado. " +
                "Edita el borrador antes de enviarlo.");
        }

        if (order.Currency != "PEN" || order.Provider != "Simulator")
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "La moneda o el proveedor del pedido no son válidos.");
        }

        await AcquireLockAsync(
            "PortalPedidos.CatalogSync",
            "Shared",
            cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        var state = await _dbContext.CatalogSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == 1,
                cancellationToken);

        if (state is null ||
            state.CurrentVersion <= 0 ||
            !state.LastSuccessAtUtc.HasValue ||
            state.LastSuccessAtUtc.Value < nowUtc.AddMinutes(-5) ||
            state.LastSuccessAtUtc.Value > nowUtc.AddMinutes(1))
        {
            throw new OrderSubmissionException(
                "CATALOG_STALE",
                "El catálogo no tiene una sincronización reciente. " +
                "Espera una actualización antes de enviar.");
        }

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == order.CustomerId,
                cancellationToken);

        var customerEligibility = _customerEligibility.Evaluate(customer);

        if (customer is null || !customerEligibility.IsEligible)
        {
            throw new OrderSubmissionException(
                "CATALOG_CHANGED",
                customerEligibility.Message
                ?? "El cliente ya no está disponible. Revisa el borrador.");
        }

        if (customer.ExternalCode != order.CustomerCode)
        {
            throw new OrderSubmissionException(
                "CATALOG_CHANGED",
                "El código ERP del cliente cambió. "
                + "Edita y revisa el borrador antes de enviarlo.");
        }

        var lines = await _dbContext.SalesOrderLines
            .AsNoTracking()
            .Where(line => line.OrderId == order.Id)
            .OrderBy(line => line.LineNumber)
            .ToListAsync(cancellationToken);

        if (lines.Count < 1 || lines.Count > 100)
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "El pedido debe contener entre 1 y 100 líneas.");
        }

        if (lines.Select(line => line.ProductId).Distinct().Count()
            != lines.Count)
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "El pedido contiene productos duplicados.");
        }

        var productIds = lines
            .Select(line => line.ProductId)
            .ToArray();

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(
                product => product.Id,
                cancellationToken);

        var stocks = await _dbContext.InventorySnapshots
            .AsNoTracking()
            .Where(stock =>
                productIds.Contains(stock.ProductId) &&
                stock.WarehouseCode == "01")
            .ToDictionaryAsync(
                stock => stock.ProductId,
                cancellationToken);

        decimal calculatedSubtotal = 0m;

        foreach (var line in lines)
        {
            if (line.Quantity < 1m ||
                line.Quantity > 9999m ||
                decimal.Truncate(line.Quantity) != line.Quantity ||
                line.WarehouseCode != "01" ||
                line.UnitPrice < 0m ||
                line.UnitPrice > 999999.9999m ||
                decimal.Round(line.UnitPrice, 4) != line.UnitPrice)
            {
                throw new OrderSubmissionException(
                    "ORDER_INVALID",
                    "El pedido contiene una línea inválida.");
            }

            if (!products.TryGetValue(line.ProductId, out var product) ||
                !product.IsActive ||
                product.ExternalCode != line.ItemCode)
            {
                throw new OrderSubmissionException(
                    "CATALOG_CHANGED",
                    "Uno de los productos ya no está disponible. " +
                    "Edita el borrador antes de enviarlo.");
            }

            if (product.UnitPrice != line.UnitPrice)
            {
                throw new OrderSubmissionException(
                    "CATALOG_CHANGED",
                    "Cambió el precio de uno o más productos. " +
                    "Edita el borrador, revisa los precios actuales " +
                    "y guarda antes de enviarlo.");
            }

            if (!stocks.TryGetValue(line.ProductId, out var stock) ||
                stock.ObservedAtUtc < nowUtc.AddMinutes(-5) ||
                stock.ObservedAtUtc > nowUtc.AddMinutes(1) ||
                stock.CatalogVersion != state.CurrentVersion)
            {
                throw new OrderSubmissionException(
                    "CATALOG_STALE",
                    "Falta una lectura reciente de existencias. " +
                    "Espera una actualización del catálogo.");
            }

            if (stock.Available < line.Quantity)
            {
                throw new OrderSubmissionException(
                    "INSUFFICIENT_STOCK",
                    $"La última lectura indica existencias insuficientes " +
                    $"para el producto {line.ItemCode}.");
            }

            decimal expectedAmount = decimal.Round(
                line.UnitPrice * line.Quantity,
                2,
                MidpointRounding.AwayFromZero);

            if (line.LineAmount != expectedAmount)
            {
                throw new OrderSubmissionException(
                    "ORDER_INVALID",
                    "El importe de una línea es inconsistente. " +
                    "Revisa el borrador.");
            }

            calculatedSubtotal += expectedAmount;
        }

        if (calculatedSubtotal != order.Subtotal)
        {
            throw new OrderSubmissionException(
                "ORDER_INVALID",
                "El importe total del pedido es inconsistente.");
        }

        // Normalizar la escala decimal para construir el mensaje.
        static decimal FixedDecimal(decimal value, string format)
        {
            return decimal.Parse(
                value.ToString(format, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
        }

        var payload = new
        {
            schemaVersion = 1,
            externalReference = order.Id.ToString("D"),
            customerCode = order.CustomerCode,
            requestedDate = order.RequestedDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            currency = order.Currency,
            warehouseCode = "01",
            subtotal = FixedDecimal(order.Subtotal, "0.00"),
            lines = lines.Select(line => new
            {
                lineNumber = line.LineNumber,
                itemCode = line.ItemCode,
                quantity = (int)line.Quantity,
                unitPrice = FixedDecimal(line.UnitPrice, "0.0000"),
                lineAmount = FixedDecimal(line.LineAmount, "0.00")
            }).ToArray()
        };

        string payloadJson = JsonSerializer.Serialize(payload);

        string payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)))
            .ToLowerInvariant();

        Guid messageId = Guid.NewGuid();
        Guid correlationId = Guid.NewGuid();

        _dbContext.Entry(order)
            .Property(item => item.RowVersion)
            .OriginalValue = request.RowVersion;

        order.Status = OrderStatus.Queued;
        order.SubmittedAtUtc = nowUtc;
        order.UpdatedAtUtc = nowUtc;
        order.CatalogVersion = state.CurrentVersion;

        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = messageId,
            OrderId = order.Id,
            IdempotencyKey = order.Id,
            Provider = "Simulator",
            SchemaVersion = 1,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Status = IntegrationStatus.Pending,
            AttemptCount = 0,
            RecoveryCycle = 0,
            NextAttemptAtUtc = nowUtc,
            CreatedAtUtc = nowUtc
        });

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            PreviousStatus = OrderStatus.Draft,
            NewStatus = OrderStatus.Queued,
            ActorUserId = userId,
            Reason = "Pedido confirmado por el usuario y puesto en cola.",
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId
        });

        _dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = userId,
            ActorType = "User",
            Action = "Order.SubmissionQueued",
            EntityType = "SalesOrder",
            EntityId = order.Id.ToString(),
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            SummaryJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                messageId,
                idempotencyKey = order.Id,
                payloadHash,
                catalogVersion = state.CurrentVersion
            })
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

}