using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErpSimulator.Api.Contracts.Orders;
using ErpSimulator.Api.Entities;
using ErpSimulator.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpSimulator.Api.Services.Orders;

public sealed class ErpOrderService
{
    private readonly ErpSimulatorDbContext _db;

    private static readonly TimeZoneInfo PeruTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "SA Pacific Standard Time"
                : "America/Lima");

    public ErpOrderService(ErpSimulatorDbContext db)
    {
        _db = db;
    }

    public async Task<ErpOrderResultDto> SubmitAsync(
        Guid idempotencyKey,
        SubmitErpOrderRequest request,
        string requestJson,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        ValidateStructure(idempotencyKey, request);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        // Coordina las recepciones entre instancias del simulador.
        await _db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @result int;

            EXEC @result = sys.sp_getapplock
                @Resource = N'ErpSimulator.ReceiveOrder',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 5000;

            IF @result < 0
            BEGIN
                THROW 51010, 'No se pudo bloquear la recepción.', 1;
            END;
            """,
            cancellationToken);

        // Consultar el resultado previo ANTES de validar fecha,
        // precios o stock actuales.
        var existing = await _db.OrderReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                receipt =>
                    receipt.IdempotencyKey == idempotencyKey ||
                    receipt.ExternalReference == request.ExternalReference,
                cancellationToken);

        if (existing is not null)
        {
            bool sameOperation =
                existing.IdempotencyKey == idempotencyKey &&
                existing.ExternalReference == request.ExternalReference;

            bool samePayload = string.Equals(
                existing.PayloadHash,
                payloadHash,
                StringComparison.Ordinal);

            if (!sameOperation || !samePayload)
            {
                throw new ErpOrderApiException(
                    409,
                    "IDEMPOTENCY_CONFLICT",
                    "La operación ya existe con otro contenido. " +
                    "No reutilices su clave para modificar el pedido.");
            }

            await transaction.CommitAsync(cancellationToken);

            return ToDto(existing);
        }

        var catalogState = await _db.CatalogStates
            .SingleOrDefaultAsync(
                state => state.Id == 1,
                cancellationToken);

        if (catalogState is null ||
            catalogState.CurrentVersion <= 0)
        {
            throw new ErpOrderApiException(
                503,
                "CATALOG_NOT_INITIALIZED",
                "El catálogo del simulador todavía no está preparado.");
        }

        // Truncamos a milisegundos para conservar el mismo valor
        // al leer posteriormente desde datetime2(3).
        var utcNow = DateTime.UtcNow;

        var processedAtUtc = new DateTime(
            utcNow.Ticks -
                utcNow.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);

        // Todos los rechazos de negocio pasan por esta función.
        // Todavía no se habrá modificado ninguna existencia.
        async Task<ErpOrderResultDto> RejectAsync(
            string code,
            string message)
        {
            var rejectedReceipt = new ErpOrderReceipt
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey,
                ExternalReference = request.ExternalReference,
                PayloadHash = payloadHash,
                RequestJson = requestJson,
                Status = ErpOrderReceiptStatuses.Rejected,
                ExternalOrderId = null,
                ResultCode = code,
                ResultMessage = message,
                ProcessedAtUtc = processedAtUtc
            };

            _db.OrderReceipts.Add(rejectedReceipt);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToDto(rejectedReceipt);
        }

        var todayInPeru = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                processedAtUtc,
                PeruTimeZone));

        if (request.RequestedDate < todayInPeru)
        {
            return await RejectAsync(
                "REQUESTED_DATE_EXPIRED",
                "La fecha solicitada ya pasó.");
        }

        if (request.Currency != "PEN")
        {
            return await RejectAsync(
                "CURRENCY_NOT_SUPPORTED",
                "El simulador solo admite pedidos en PEN.");
        }

        if (request.WarehouseCode != "01")
        {
            return await RejectAsync(
                "WAREHOUSE_NOT_SUPPORTED",
                "El simulador solo admite el almacén 01.");
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == request.CustomerCode,
                cancellationToken);

        if (customer is null || !customer.Active)
        {
            return await RejectAsync(
                "CUSTOMER_UNAVAILABLE",
                "El cliente no existe o está inactivo.");
        }

        var itemCodes = request.Lines
            .Select(line => line.ItemCode)
            .ToArray();

        var items = await _db.Items
            .AsNoTracking()
            .Where(item => itemCodes.Contains(item.Code))
            .ToListAsync(cancellationToken);

        var itemsByCode = items.ToDictionary(
            item => item.Code,
            StringComparer.OrdinalIgnoreCase);

        // Estas entidades quedan rastreadas para actualizar Reserved.
        var stocks = await _db.Stocks
            .Where(stock =>
                stock.WarehouseCode == "01" &&
                itemCodes.Contains(stock.ItemCode))
            .ToListAsync(cancellationToken);

        var stocksByCode = stocks.ToDictionary(
            stock => stock.ItemCode,
            StringComparer.OrdinalIgnoreCase);

        // Validar TODAS las líneas antes de modificar existencias.
        foreach (var line in request.Lines.OrderBy(
                     line => line.LineNumber))
        {
            if (!itemsByCode.TryGetValue(
                    line.ItemCode,
                    out var item) ||
                !item.Active)
            {
                return await RejectAsync(
                    "ITEM_UNAVAILABLE",
                    $"El producto {line.ItemCode} no existe o está inactivo.");
            }

            if (item.UnitPrice != line.UnitPrice)
            {
                return await RejectAsync(
                    "PRICE_CHANGED",
                    $"El precio del producto {line.ItemCode} cambió.");
            }

            if (!stocksByCode.TryGetValue(
                    line.ItemCode,
                    out var stock))
            {
                return await RejectAsync(
                    "STOCK_UNAVAILABLE",
                    $"No hay registro de existencias para {line.ItemCode}.");
            }

            decimal available = stock.OnHand - stock.Reserved;

            if (available < line.Quantity)
            {
                return await RejectAsync(
                    "INSUFFICIENT_STOCK",
                    $"Existencias insuficientes para {line.ItemCode}.");
            }
        }

        // Evitar desbordar la versión antes de cambiar las existencias.
        long nextCatalogVersion =
            checked(catalogState.CurrentVersion + 1);

        // Todas las validaciones de negocio fueron satisfactorias.
        foreach (var line in request.Lines)
        {
            var stock = stocksByCode[line.ItemCode];

            stock.Reserved += line.Quantity;
        }

        // La disponibilidad cambió: debe cambiar la versión global.
        catalogState.CurrentVersion = nextCatalogVersion;
        catalogState.UpdatedAtUtc = processedAtUtc;

        var receiptId = Guid.NewGuid();

        var acceptedReceipt = new ErpOrderReceipt
        {
            Id = receiptId,
            IdempotencyKey = idempotencyKey,
            ExternalReference = request.ExternalReference,
            PayloadHash = payloadHash,
            RequestJson = requestJson,
            Status = ErpOrderReceiptStatuses.Accepted,
            ExternalOrderId = $"SIM-{receiptId:N}",
            ResultCode = "ORDER_ACCEPTED",
            ResultMessage =
                "Pedido aceptado y existencias reservadas.",
            ProcessedAtUtc = processedAtUtc
        };

        _db.OrderReceipts.Add(acceptedReceipt);

        // Reserva, versión y resultado se guardan juntos.
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(acceptedReceipt);
    }

    public async Task<ErpOrderResultDto?> GetByExternalReferenceAsync(
        Guid externalReference,
        CancellationToken cancellationToken)
    {
        var receipt = await _db.OrderReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ExternalReference == externalReference,
                cancellationToken);

        return receipt is null ? null : ToDto(receipt);
    }

    private static ErpOrderResultDto ToDto(
        ErpOrderReceipt receipt)
    {
        return new ErpOrderResultDto
        {
            ExternalReference = receipt.ExternalReference,
            PayloadHash = receipt.PayloadHash,
            Status = receipt.Status,
            ExternalOrderId = receipt.ExternalOrderId,
            Code = receipt.ResultCode,
            Message = receipt.ResultMessage,
            ProcessedAtUtc = DateTime.SpecifyKind(
                receipt.ProcessedAtUtc,
                DateTimeKind.Utc)
        };
    }

    private static void ValidateStructure(
        Guid idempotencyKey,
        SubmitErpOrderRequest request)
    {
        static void Invalid(string message)
        {
            throw new ErpOrderApiException(
                400,
                "INVALID_ORDER",
                message);
        }

        static bool ValidCode(string? value, int maxLength)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.Length <= maxLength &&
                value == value.Trim();
        }

        if (idempotencyKey == Guid.Empty ||
            request.ExternalReference == Guid.Empty ||
            idempotencyKey != request.ExternalReference)
        {
            Invalid(
                "Idempotency-Key debe coincidir con externalReference " +
                "y ambos deben ser identificadores válidos.");
        }

        if (request.SchemaVersion != 1)
        {
            Invalid("La versión admitida del contrato es 1.");
        }

        if (!ValidCode(request.CustomerCode, 50))
        {
            Invalid("El código del cliente es obligatorio y admite 50 caracteres.");
        }

        if (!ValidCode(request.Currency, 3) ||
            !ValidCode(request.WarehouseCode, 8))
        {
            Invalid("La moneda o el almacén tienen un formato inválido.");
        }

        if (request.RequestedDate == default)
        {
            Invalid("La fecha solicitada es obligatoria.");
        }

        if (request.Lines is null ||
            request.Lines.Count < 1 ||
            request.Lines.Count > 100)
        {
            throw new ErpOrderApiException(
                400,
                "INVALID_ORDER",
                "El pedido debe contener entre 1 y 100 líneas.");
        }

        var codes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var numbers = new HashSet<int>();

        decimal calculatedSubtotal = 0m;

        foreach (var line in request.Lines)
        {
            if (line is null)
            {
                throw new ErpOrderApiException(
                    400,
                    "INVALID_ORDER",
                    "El pedido contiene una línea nula.");
            }

            if (line.LineNumber < 1 ||
                line.LineNumber > request.Lines.Count ||
                !numbers.Add(line.LineNumber))
            {
                Invalid(
                    "Las líneas deben tener números únicos y consecutivos desde 1.");
            }

            if (!ValidCode(line.ItemCode, 50) ||
                !codes.Add(line.ItemCode))
            {
                Invalid(
                    "Los códigos de producto deben ser válidos y no repetirse.");
            }

            if (line.Quantity < 1 || line.Quantity > 9999)
            {
                Invalid("Las cantidades deben estar entre 1 y 9999.");
            }

            if (line.UnitPrice < 0m ||
                line.UnitPrice > 999999.9999m ||
                decimal.Round(line.UnitPrice, 4) != line.UnitPrice)
            {
                Invalid(
                    "El precio admite hasta cuatro decimales " +
                    "y debe estar entre 0 y 999999.9999.");
            }

            decimal expectedAmount = decimal.Round(
                line.UnitPrice * line.Quantity,
                2,
                MidpointRounding.AwayFromZero);

            if (line.LineAmount != expectedAmount)
            {
                Invalid(
                    $"El importe de la línea {line.LineNumber} es incorrecto.");
            }

            calculatedSubtotal += expectedAmount;
        }

        if (request.Subtotal != calculatedSubtotal)
        {
            Invalid("El subtotal no coincide con la suma de las líneas.");
        }
    }
}