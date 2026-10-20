using System;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.DTOs.Erp;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Erp;
using Portal.Application.Interfaces.Integration;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Integration;

public sealed class OrderOutboxProcessor : IOrderOutboxProcessor
{
    private const int MaxSubmissionAttempts = 5;
    private const int MaxConsecutiveLookups = 5;

    private const string SubmitOperation = "Submit";
    private const string LookupOperation = "Lookup";

    private static readonly TimeSpan LeaseDuration =
        TimeSpan.FromSeconds(120);

    private readonly PortalDbContext _db;
    private readonly IErpOrderClient _erpClient;
    private readonly ILogger<OrderOutboxProcessor> _logger;

    public OrderOutboxProcessor(
        PortalDbContext db,
        IErpOrderClient erpClient,
        ILogger<OrderOutboxProcessor> logger)
    {
        _db = db;
        _erpClient = erpClient;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(
     CancellationToken cancellationToken = default)
    {
        var work = await ClaimNextAsync(cancellationToken);

        if (work is null)
        {
            return false;
        }

        ErpOrderResponseDto? result = null;
        ErpOrderCommunicationException? communicationError = null;

        try
        {
            if (!HasValidStoredPayload(work))
            {
                throw new ErpOrderCommunicationException(
                    code: "OUTBOX_PAYLOAD_INVALID",
                    message: "El mensaje almacenado no coincide con su referencia y hash. Requiere revisión técnica.",
                    isTransient: false, outcomeUncertain: work.Operation == LookupOperation);
            }
            // La transacción de adquisición ya terminó.
            // No mantenemos una transacción SQL durante la llamada HTTP.
            if (work.Operation == LookupOperation)
            {
                // null significa que la consulta no encontró
                // una decisión remota, según el contrato actual.
                result = await _erpClient.GetResultAsync(
                    work.OrderId,
                    work.PayloadHash,
                    work.CorrelationId,
                    cancellationToken);
            }
            else if (work.Operation == SubmitOperation)
            {
                result = await _erpClient.SubmitAsync(
                    work.OrderId,
                    work.IdempotencyKey,
                    work.PayloadJson,
                    work.PayloadHash,
                    work.CorrelationId,
                    cancellationToken);

                if (result is null)
                {
                    // Un envío sin respuesta válida no demuestra que
                    // el ERP no haya guardado el pedido.
                    //
                    // Propagamos el fallo al worker. El mensaje permanece
                    // en Processing hasta vencer su concesión y después
                    // será recuperado mediante Lookup.
                    throw new InvalidOperationException(
                        "El cliente ERP devolvió un resultado nulo " +
                        "para una operación de envío. " +
                        "Debe verificarse el resultado remoto.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"La operación de integración '{work.Operation}' " +
                    "no está soportada.");
            }
        }
        catch (ErpOrderCommunicationException exception)
        {
            communicationError = exception;
        }

        // La cancelación y los fallos inesperados se propagan al worker.
        // No se registra una confirmación ni un rechazo por suposición.
        await FinishAsync(
            work,
            result,
            communicationError,
            cancellationToken);

        return true;
    }

    private async Task<ClaimedWork?> ClaimNextAsync(
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        await AcquireQueueLockAsync(cancellationToken);

        DateTime now = await GetDatabaseUtcNowAsync(
            cancellationToken);

        var message = await _db.OutboxMessages
            .Where(item =>
                (
                    (
                        item.Status == IntegrationStatus.Pending ||
                        item.Status == IntegrationStatus.RetryWaiting ||
                        item.Status == IntegrationStatus.Unknown
                    ) &&
                    item.NextAttemptAtUtc <= now
                ) ||
                (
                    item.Status == IntegrationStatus.Processing &&
                    (
                        item.LeaseUntilUtc == null ||
                        item.LeaseUntilUtc <= now
                    )
                ))
            .OrderBy(item => item.NextAttemptAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var order = await _db.SalesOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == message.OrderId,
                cancellationToken);

        // No enviar mensajes incompatibles con el pedido actual.
        if (order is null ||
            order.Status != OrderStatus.Queued ||
            message.Provider != "Simulator" ||
            order.Provider != "Simulator" ||
            message.SchemaVersion != 1 ||
            message.IdempotencyKey != message.OrderId)
        {
            MarkDeadLetter(
                message,
                "OUTBOX_INVALID_STATE",
                now);

            AddAudit(
                message,
                Guid.NewGuid(),
                "Integration.Stopped",
                now,
                "OUTBOX_INVALID_STATE");

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogWarning(
                "El mensaje {MessageId} requiere revisión por estado inválido.",
                message.Id);

            return null;
        }

        bool recoveringInterruptedWork =
            message.Status == IntegrationStatus.Processing;

        bool mustLookup =
            recoveringInterruptedWork ||
            message.Status == IntegrationStatus.Unknown ||
            message.AttemptCount >= MaxSubmissionAttempts;

        string operation = mustLookup
            ? LookupOperation
            : SubmitOperation;

        // Cerrar registros incompletos de una ejecución anterior.
        // "Interrupted" no significa que el ERP rechazó la operación.
        var unfinishedAttempts = await _db.IntegrationAttempts
            .Where(attempt =>
                attempt.MessageId == message.Id &&
                attempt.FinishedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var unfinished in unfinishedAttempts)
        {
            unfinished.FinishedAtUtc = now;
            unfinished.ResultCode = "INTERRUPTED";

            unfinished.ErrorSummary =
                "La ejecución anterior terminó sin registrar un resultado.";

            unfinished.DurationMs = ElapsedMilliseconds(
                unfinished.StartedAtUtc,
                now);
        }

        int previousAttemptNumber =
            await _db.IntegrationAttempts
                .Where(attempt => attempt.MessageId == message.Id)
                .Select(attempt => (int?)attempt.AttemptNumber)
                .MaxAsync(cancellationToken)
            ?? 0;

        var leaseOwner = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        message.Status = IntegrationStatus.Processing;
        message.LeaseOwner = leaseOwner;
        message.LeaseUntilUtc = now.Add(LeaseDuration);
        message.CompletedAtUtc = null;

        if (operation == SubmitOperation)
        {
            message.AttemptCount =
                checked(message.AttemptCount + 1);
        }

        var attempt = new IntegrationAttempt
        {
            MessageId = message.Id,
            AttemptNumber = checked(previousAttemptNumber + 1),
            Operation = operation,
            StartedAtUtc = now,
            ResultCode = "STARTED",
            CorrelationId = correlationId
        };

        _db.IntegrationAttempts.Add(attempt);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ClaimedWork(
            message.Id,
            message.OrderId,
            message.IdempotencyKey,
            leaseOwner,
            attempt.Id,
            correlationId,
            operation,
            message.PayloadJson,
            message.PayloadHash);
    }

    private async Task FinishAsync(
        ClaimedWork work,
        ErpOrderResponseDto? result,
        ErpOrderCommunicationException? error,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        await AcquireQueueLockAsync(cancellationToken);

        DateTime now = await GetDatabaseUtcNowAsync(
            cancellationToken);

        var message = await _db.OutboxMessages
            .SingleAsync(
                item => item.Id == work.MessageId,
                cancellationToken);

        // Una ejecución antigua no puede sobrescribir el resultado
        // de otra que ya recuperó el mensaje.
        if (message.Status != IntegrationStatus.Processing ||
            message.LeaseOwner != work.LeaseOwner ||
            !message.LeaseUntilUtc.HasValue ||
            message.LeaseUntilUtc.Value <= now)
        {
            await transaction.CommitAsync(cancellationToken);

            _logger.LogWarning(
                "Se descartó una finalización con concesión vencida " +
                "o reemplazada. Mensaje: {MessageId}.",
                work.MessageId);

            return;
        }

        var attempt = await _db.IntegrationAttempts
            .SingleAsync(
                item => item.Id == work.AttemptId,
                cancellationToken);

        attempt.FinishedAtUtc = now;
        attempt.DurationMs = ElapsedMilliseconds(
            attempt.StartedAtUtc,
            now);

        if (error is not null)
        {
            attempt.HttpStatus = error.HttpStatusCode;
            attempt.ResultCode = Clip(error.Code, 60);
            attempt.ErrorSummary = Clip(error.Message, 500);

            await HandleCommunicationErrorAsync(
                message,
                work.Operation,
                error,
                now,
                cancellationToken);

            AddAudit(
                message,
                work.CorrelationId,
                "Integration.Failed",
                now,
                error.Code);
        }
        else if (result is not null)
        {
            attempt.HttpStatus = 200;
            attempt.ResultCode = Clip(result.Code, 60);

            await ApplyErpResultAsync(
                message,
                result,
                work.CorrelationId,
                now,
                cancellationToken);
        }
        else
        {
            // Solo GetResultAsync puede devolver null:
            // el ERP respondió ORDER_RESULT_NOT_FOUND.
            attempt.HttpStatus = 404;
            attempt.ResultCode = "ORDER_RESULT_NOT_FOUND";

            if (message.AttemptCount >= MaxSubmissionAttempts)
            {
                MarkDeadLetter(
                    message,
                    "ERP_RESULT_UNRESOLVED",
                    now);
            }
            else
            {
                Schedule(
                    message,
                    IntegrationStatus.RetryWaiting,
                    "ORDER_RESULT_NOT_FOUND",
                    now,
                    GetDelaySeconds(message.AttemptCount));
            }

            AddAudit(
                message,
                work.CorrelationId,
                "Integration.LookupNotFound",
                now,
                message.LastErrorCode ?? "ORDER_RESULT_NOT_FOUND");
        }

        message.LeaseOwner = null;
        message.LeaseUntilUtc = null;

        // Mensaje, intento, pedido, historial y auditoría
        // se guardan dentro de esta misma transacción.
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Mensaje {MessageId}: operación {Operation}, " +
            "estado {Status}, código {Code}.",
            message.Id,
            work.Operation,
            message.Status,
            attempt.ResultCode);
    }

    private async Task ApplyErpResultAsync(
        OutboxMessage message,
        ErpOrderResponseDto result,
        Guid correlationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var order = await _db.SalesOrders
            .SingleOrDefaultAsync(
                item => item.Id == message.OrderId,
                cancellationToken);

        if (order is null ||
            order.Status != OrderStatus.Queued ||
            result.ExternalReference != message.OrderId ||
            (
                result.Status != "Accepted" &&
                result.Status != "Rejected"
            ))
        {
            MarkDeadLetter(
                message,
                "ORDER_STATE_CONFLICT",
                now);

            AddAudit(
                message,
                correlationId,
                "Integration.Stopped",
                now,
                "ORDER_STATE_CONFLICT");

            return;
        }

        bool accepted = result.Status == "Accepted";

        OrderStatus previousStatus = order.Status;

        order.Status = accepted
            ? OrderStatus.Confirmed
            : OrderStatus.Rejected;

        order.ExternalOrderId = accepted
            ? result.ExternalOrderId
            : null;

        order.UpdatedAtUtc = now;

        message.Status = accepted
            ? IntegrationStatus.Succeeded
            : IntegrationStatus.Rejected;

        message.CompletedAtUtc = now;

        message.LastErrorCode = accepted
            ? null
            : Clip(result.Code, 60);

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            PreviousStatus = previousStatus,
            NewStatus = order.Status,
            ActorUserId = null,
            Reason = Clip(
                $"{result.Code}: {result.Message}",
                200),
            OccurredAtUtc = now,
            CorrelationId = correlationId
        });

        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = null,
            ActorType = "System",
            Action = accepted
                ? "Order.ErpConfirmed"
                : "Order.ErpRejected",
            EntityType = "SalesOrder",
            EntityId = order.Id.ToString("D"),
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            SummaryJson = JsonSerializer.Serialize(new
            {
                messageId = message.Id,
                orderId = order.Id,
                result.Status,
                result.Code,
                result.ExternalOrderId,
                result.ProcessedAtUtc,
                submissionAttempts = message.AttemptCount
            })
        });
    }

    private async Task HandleCommunicationErrorAsync(
        OutboxMessage message,
        string operation,
        ErpOrderCommunicationException error,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Credenciales, conflicto de idempotencia, payload inválido,
        // etc. requieren corrección o revisión, no un bucle automático.
        if (!error.IsTransient)
        {
            MarkDeadLetter(
                message,
                error.Code,
                now);

            return;
        }

        if (operation == LookupOperation)
        {
            int lookupCount = await CountConsecutiveLookupsAsync(
                message.Id,
                cancellationToken);

            if (lookupCount >= MaxConsecutiveLookups)
            {
                MarkDeadLetter(
                    message,
                    "ERP_LOOKUP_EXHAUSTED",
                    now);

                return;
            }

            int delay = Math.Max(
                GetDelaySeconds(lookupCount),
                error.RetryAfterSeconds ?? 0);

            Schedule(
                message,
                IntegrationStatus.Unknown,
                error.Code,
                now,
                delay);

            return;
        }

        int submissionDelay = Math.Max(
            GetDelaySeconds(message.AttemptCount),
            error.RetryAfterSeconds ?? 0);

        // Después del último envío también se consulta al ERP.
        // Nunca convertir una respuesta incierta en rechazo.
        bool mustLookup =
            error.OutcomeUncertain ||
            message.AttemptCount >= MaxSubmissionAttempts;

        Schedule(
            message,
            mustLookup
                ? IntegrationStatus.Unknown
                : IntegrationStatus.RetryWaiting,
            error.Code,
            now,
            submissionDelay);
    }

    private async Task<int> CountConsecutiveLookupsAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        int lastBoundaryNumber =
            await _db.IntegrationAttempts
                .Where(attempt =>
                    attempt.MessageId == messageId &&
                    (
                        attempt.Operation == SubmitOperation ||
                        attempt.Operation == "Recovery"
                    ))
                .Select(attempt => (int?)attempt.AttemptNumber)
                .MaxAsync(cancellationToken)
            ?? 0;

        return await _db.IntegrationAttempts
            .CountAsync(
                attempt =>
                    attempt.MessageId == messageId &&
                    attempt.Operation == LookupOperation &&
                    attempt.AttemptNumber > lastBoundaryNumber,
                cancellationToken);
    }
    private static void Schedule(
        OutboxMessage message,
        IntegrationStatus status,
        string code,
        DateTime now,
        int delaySeconds)
    {
        message.Status = status;
        message.LastErrorCode = Clip(code, 60);
        message.NextAttemptAtUtc = now.AddSeconds(delaySeconds);
        message.CompletedAtUtc = null;
    }

    private static void MarkDeadLetter(
        OutboxMessage message,
        string code,
        DateTime now)
    {
        message.Status = IntegrationStatus.DeadLetter;
        message.LastErrorCode = Clip(code, 60);
        message.CompletedAtUtc = now;
        message.LeaseOwner = null;
        message.LeaseUntilUtc = null;
    }

    private void AddAudit(
        OutboxMessage message,
        Guid correlationId,
        string action,
        DateTime now,
        string code)
    {
        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = null,
            ActorType = "System",
            Action = action,
            EntityType = "OutboxMessage",
            EntityId = message.Id.ToString("D"),
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            SummaryJson = JsonSerializer.Serialize(new
            {
                messageId = message.Id,
                orderId = message.OrderId,
                status = message.Status.ToString(),
                code,
                submissionAttempts = message.AttemptCount,
                message.RecoveryCycle
            })
        });
    }

    private Task AcquireQueueLockAsync(
        CancellationToken cancellationToken)
    {
        return _db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @result int;

            EXEC @result = sys.sp_getapplock
                @Resource = N'PortalPedidos.Outbox',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 5000;

            IF @result < 0
            BEGIN
                THROW 51020, 'No se pudo bloquear la cola de pedidos.', 1;
            END;
            """,
            cancellationToken);
    }

    private async Task<DateTime> GetDatabaseUtcNowAsync(
        CancellationToken cancellationToken)
    {
        DateTime value = await _db.Database
            .SqlQuery<DateTime>(
                $"""
                SELECT CAST(SYSUTCDATETIME() AS datetime2(3)) AS [Value]
                """)
            .SingleAsync(cancellationToken);

        return DateTime.SpecifyKind(
            value,
            DateTimeKind.Utc);
    }

    private static int GetDelaySeconds(int attemptCount)
    {
        int baseSeconds = attemptCount switch
        {
            <= 1 => 10,
            2 => 30,
            3 => 120,
            _ => 300
        };

        // Variación entre -20 % y +20 %.
        double factor = 0.8 + Random.Shared.NextDouble() * 0.4;

        return Math.Max(
            1,
            (int)Math.Ceiling(baseSeconds * factor));
    }

    private static long ElapsedMilliseconds(
        DateTime startedAtUtc,
        DateTime finishedAtUtc)
    {
        return Math.Max(
            0L,
            (long)(finishedAtUtc - startedAtUtc).TotalMilliseconds);
    }

    private static bool HasValidStoredPayload(ClaimedWork work)
    {
        if (string.IsNullOrWhiteSpace(work.PayloadJson) || work.PayloadJson.Length > 64 * 1024)
            return false;
        byte[] bytes = Encoding.UTF8.GetBytes(work.PayloadJson);
        if (bytes.Length > 64 * 1024) return false;
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, work.PayloadHash, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var document = JsonDocument.Parse(work.PayloadJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("externalReference", out var reference) &&
                reference.ValueKind == JsonValueKind.String && reference.TryGetGuid(out var id) &&
                id == work.OrderId;
        }
        catch (JsonException) { return false; }
    }

    private static string Clip(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private sealed record ClaimedWork(
        Guid MessageId,
        Guid OrderId,
        Guid IdempotencyKey,
        Guid LeaseOwner,
        long AttemptId,
        Guid CorrelationId,
        string Operation,
        string PayloadJson,
        string PayloadHash);
}