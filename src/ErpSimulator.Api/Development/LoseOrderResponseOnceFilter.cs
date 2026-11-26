using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ErpSimulator.Api.Persistence;

namespace ErpSimulator.Api.Development;

public sealed class LoseOrderResponseOnceFilter : IEndpointFilter
{
    private readonly Guid _targetOrderId;

    private readonly ILogger<LoseOrderResponseOnceFilter> _logger;

    private int _faultConsumed;

    public LoseOrderResponseOnceFilter(
        Guid targetOrderId,
        ILogger<LoseOrderResponseOnceFilter> logger)
    {
        if (targetOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador del pedido no puede estar vacío.",
                nameof(targetOrderId));
        }

        _targetOrderId = targetOrderId;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;

        // El filtro pertenece al grupo de endpoints,
        // pero solo puede actuar sobre POST /orders.
        string requestPath =
            request.Path.Value?.TrimEnd('/') ?? string.Empty;

        bool isOrderSubmission =
            HttpMethods.IsPost(request.Method) &&
            string.Equals(
                requestPath,
                "/sim-api/v1/orders",
                StringComparison.OrdinalIgnoreCase);

        if (!isOrderSubmission)
        {
            return await next(context);
        }

        var keyValues = request.Headers["Idempotency-Key"];

        bool matchesTarget =
            keyValues.Count == 1 &&
            Guid.TryParse(keyValues.ToString(), out Guid key) &&
            key == _targetOrderId;

        if (!matchesTarget ||
            Volatile.Read(ref _faultConsumed) != 0)
        {
            return await next(context);
        }

        // La pérdida de respuesta se simula después de confirmar la transacción del ERP.
        object? result = await next(context);

        // El contrato actual del simulador devuelve HTTP 200
        // cuando entrega una decisión comercial.
        if (result is not IStatusCodeHttpResult statusResult ||
            statusResult.StatusCode != StatusCodes.Status200OK)
        {
            return result;
        }

        if (httpContext.Response.HasStarted)
        {
            _logger.LogWarning(
                "No se aplicó la falla al pedido {OrderId}: " +
                "la respuesta HTTP ya había comenzado.",
                _targetOrderId);

            return result;
        }

        var dbContext = httpContext.RequestServices
            .GetRequiredService<ErpSimulatorDbContext>();

        // Verificar mediante una consulta que existe
        // una aceptación registrada para este pedido.
        // Un rechazo no debe consumir esta prueba.
        bool hasAcceptedReceipt = await dbContext.OrderReceipts
            .AsNoTracking()
            .AnyAsync(
                receipt =>
                    receipt.ExternalReference == _targetOrderId &&
                    receipt.IdempotencyKey == _targetOrderId &&
                    receipt.Status == "Accepted",
                httpContext.RequestAborted);

        if (!hasAcceptedReceipt)
        {
            return result;
        }

        // Aunque lleguen solicitudes concurrentes,
        // solo una puede consumir esta falla.
        if (Interlocked.CompareExchange(
                ref _faultConsumed,
                1,
                0) != 0)
        {
            return result;
        }

        _logger.LogWarning(
            "SIMULATED_RESPONSE_LOSS: " +
            "el pedido {OrderId} tiene una aceptación persistida. " +
            "Se interrumpirá su respuesta HTTP para comprobar " +
            "la recuperación del portal.",
            _targetOrderId);

        httpContext.Abort();

        // No entregar el resultado de aceptación al cliente.
        return Results.Empty;
    }
}