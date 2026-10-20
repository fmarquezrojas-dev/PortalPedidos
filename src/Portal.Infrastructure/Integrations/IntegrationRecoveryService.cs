using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Integration;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Integration;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Integration;

public sealed class IntegrationRecoveryService
    : IIntegrationRecoveryService
{
    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public IntegrationRecoveryService(
        PortalDbContext db,
        ICurrentUser currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    public async Task<IntegrationRecoveryPreviewDto?> GetPreviewAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await RequireOperatorAsync();

        var message = await _db.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == messageId,
                cancellationToken);

        if (message is null)
        {
            return null;
        }

        var order = await _db.SalesOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == message.OrderId,
                cancellationToken);

        string? blockReason = GetBlockReason(
            message,
            order,
            DateTime.UtcNow);

        return new IntegrationRecoveryPreviewDto
        {
            MessageId = message.Id,
            PublicNumber = order?.PublicNumber ?? 0,
            CustomerName = order?.CustomerName ?? "Pedido no encontrado",
            Status = message.Status,
            AttemptCount = message.AttemptCount,
            RecoveryCycle = message.RecoveryCycle,
            LastErrorCode = message.LastErrorCode,
            RowVersion = Convert.ToBase64String(message.RowVersion),
            BlockReason = blockReason
        };
    }

    public async Task<bool> RecoverAsync(
        Guid messageId,
        byte[] rowVersion,
        string reason,
        CancellationToken cancellationToken = default)
    {
        string actorUserId = await RequireOperatorAsync();

        reason = reason?.Trim() ?? string.Empty;

        if (reason.Length < 5 || reason.Length > 300)
        {
            throw new IntegrationRecoveryException(
                "Escribe un motivo de entre 5 y 300 caracteres.");
        }

        if (rowVersion is null || rowVersion.Length != 8)
        {
            throw new IntegrationRecoveryException(
                "La versión del mensaje es inválida. Recarga la página.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        await _db.Database.ExecuteSqlRawAsync(
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

        DateTime databaseTime = await _db.Database
            .SqlQuery<DateTime>(
                $"""
                SELECT CAST(SYSUTCDATETIME() AS datetime2(3)) AS [Value]
                """)
            .SingleAsync(cancellationToken);

        DateTime now = DateTime.SpecifyKind(
            databaseTime,
            DateTimeKind.Utc);

        var message = await _db.OutboxMessages
            .SingleOrDefaultAsync(
                item => item.Id == messageId,
                cancellationToken);

        if (message is null)
        {
            return false;
        }

        if (!message.RowVersion.SequenceEqual(rowVersion))
        {
            throw new IntegrationRecoveryException(
                "El mensaje cambió desde que abriste esta pantalla. " +
                "Recarga la confirmación antes de continuar.");
        }

        var order = await _db.SalesOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == message.OrderId,
                cancellationToken);

        string? blockReason = GetBlockReason(
            message,
            order,
            now);

        if (blockReason is not null)
        {
            throw new IntegrationRecoveryException(blockReason);
        }

        int previousAttempts = message.AttemptCount;
        int previousCycle = message.RecoveryCycle;
        string? previousError = message.LastErrorCode;

        int previousAttemptNumber =
            await _db.IntegrationAttempts
                .Where(item => item.MessageId == message.Id)
                .Select(item => (int?)item.AttemptNumber)
                .MaxAsync(cancellationToken)
            ?? 0;

        var correlationId = Guid.NewGuid();

        _db.Entry(message)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;

        message.RecoveryCycle = checked(previousCycle + 1);
        message.AttemptCount = 0;

        // Siempre consultar al ERP antes de un posible reenvío.
        message.Status = IntegrationStatus.Unknown;
        message.NextAttemptAtUtc = now;

        message.LeaseOwner = null;
        message.LeaseUntilUtc = null;
        message.CompletedAtUtc = null;
        message.LastErrorCode = null;

        // Marca de inicio del nuevo ciclo.
        // Es un evento administrativo, no una llamada HTTP.
        _db.IntegrationAttempts.Add(new IntegrationAttempt
        {
            MessageId = message.Id,
            AttemptNumber = checked(previousAttemptNumber + 1),
            Operation = "Recovery",
            StartedAtUtc = now,
            FinishedAtUtc = now,
            HttpStatus = null,
            ResultCode = "RECOVERY_REQUESTED",
            ErrorSummary = null,
            CorrelationId = correlationId,
            DurationMs = 0
        });

        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            ActorType = "User",
            Action = "Integration.RecoveryRequested",
            EntityType = "OutboxMessage",
            EntityId = message.Id.ToString("D"),
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            SummaryJson = JsonSerializer.Serialize(new
            {
                messageId = message.Id,
                orderId = message.OrderId,
                reason,
                previousAttempts,
                previousCycle,
                previousError,
                newCycle = message.RecoveryCycle,
                nextStatus = message.Status.ToString()
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private async Task<string> RequireOperatorAsync()
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException();
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user is null ||
            !user.IsActive ||
            !((await _userManager.IsInRoleAsync(user, AppRoles.Admin)) ||
              (await _userManager.IsInRoleAsync(user, AppRoles.Operator))))
        {
            throw new UnauthorizedAccessException();
        }

        return userId;
    }

    private static string? GetBlockReason(
        OutboxMessage message,
        SalesOrder? order,
        DateTime now)
    {
        if (message.Status != IntegrationStatus.DeadLetter)
        {
            return "Solo se pueden recuperar mensajes en Requiere revisión.";
        }

        if (order is null || order.Status != OrderStatus.Queued)
        {
            return "El pedido ya no está pendiente de integración.";
        }

        if (message.LeaseOwner.HasValue ||
            (
                message.LeaseUntilUtc.HasValue &&
                message.LeaseUntilUtc.Value > now
            ))
        {
            return "El mensaje conserva una concesión de procesamiento. " +
                "Debe revisarse antes de recuperarlo.";
        }

        if (message.Provider != "Simulator" ||
            order.Provider != "Simulator" ||
            message.SchemaVersion != 1 ||
            message.IdempotencyKey != message.OrderId)
        {
            return "La configuración del mensaje es inconsistente.";
        }

        bool recoverableCode = message.LastErrorCode is
            "ERP_LOOKUP_EXHAUSTED" or
            "ERP_RESULT_UNRESOLVED" or
            "ERP_UNAUTHORIZED" or
            "ERP_FORBIDDEN" or
            "ERP_ENDPOINT_NOT_FOUND";

        if (!recoverableCode)
        {
            return "Este código requiere revisión técnica. " +
                "No admite reactivación desde esta pantalla.";
        }

        if (!HasValidPayload(message))
        {
            return "El contenido del mensaje o su hash son inconsistentes. " +
                "No se permite recuperarlo.";
        }

        return null;
    }

    private static bool HasValidPayload(OutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.PayloadJson))
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(message.PayloadJson);

        if (bytes.Length > 64 * 1024)
        {
            return false;
        }

        string calculatedHash = Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();

        if (!string.Equals(
                calculatedHash,
                message.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                message.PayloadJson);

            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(
                    "externalReference",
                    out var reference) &&
                reference.ValueKind == JsonValueKind.String &&
                reference.TryGetGuid(out var orderId) &&
                orderId == message.OrderId;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}