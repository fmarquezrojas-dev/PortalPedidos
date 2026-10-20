using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ErpSimulator.Api.Contracts.Orders;
using ErpSimulator.Api.Services.Orders;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErpSimulator.Api.Endpoints;

public static class OrderEndpoints
{
    private const int MaxBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16
        };

    public static async Task<IResult> SubmitAsync(
        HttpContext context,
        ErpOrderService service,
        ILogger<ErpOrderService> logger,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        try
        {
            if (!context.Request.HasJsonContentType())
            {
                return Problem(
                    context,
                    415,
                    "JSON_REQUIRED",
                    "Envía el pedido con Content-Type application/json.");
            }

            if (!context.Request.Headers.TryGetValue(
                    "Idempotency-Key",
                    out var headerValues) ||
                headerValues.Count != 1 ||
                !Guid.TryParse(
                    headerValues[0],
                    out var idempotencyKey) ||
                idempotencyKey == Guid.Empty)
            {
                return Problem(
                    context,
                    400,
                    "INVALID_IDEMPOTENCY_KEY",
                    "Se requiere un encabezado Idempotency-Key válido.");
            }

            byte[] body = await ReadBodyAsync(
                context.Request,
                cancellationToken);

            var request =
                JsonSerializer.Deserialize<SubmitErpOrderRequest>(
                    body,
                    JsonOptions);

            if (request is null)
            {
                return Problem(
                    context,
                    400,
                    "INVALID_JSON",
                    "El cuerpo debe contener un pedido.");
            }

            string payloadHash = Convert.ToHexString(
                    SHA256.HashData(body))
                .ToLowerInvariant();

            string requestJson = Encoding.UTF8.GetString(body);

            var result = await service.SubmitAsync(
                idempotencyKey,
                request,
                requestJson,
                payloadHash,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (ErpOrderApiException exception)
        {
            return Problem(
                context,
                exception.StatusCode,
                exception.Code,
                exception.Message);
        }
        catch (JsonException)
        {
            return Problem(
                context,
                400,
                "INVALID_JSON",
                "El JSON no corresponde al contrato de pedidos.");
        }
        catch (BadHttpRequestException exception)
        {
            return Problem(
                context,
                exception.StatusCode,
                "INVALID_HTTP_REQUEST",
                "No se pudo leer correctamente la solicitud.");
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(
                exception,
                "No se pudo persistir el resultado del pedido.");

            return DatabaseUnavailable(context);
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "Fallo de SQL Server al recibir un pedido.");

            return DatabaseUnavailable(context);
        }
    }

    public static async Task<IResult> GetByExternalReferenceAsync(
        Guid id,
        HttpContext context,
        ErpOrderService service,
        ILogger<ErpOrderService> logger,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        if (id == Guid.Empty)
        {
            return Problem(
                context,
                400,
                "INVALID_EXTERNAL_REFERENCE",
                "La referencia externa es inválida.");
        }

        try
        {
            var result = await service.GetByExternalReferenceAsync(
                id,
                cancellationToken);

            if (result is null)
            {
                return Problem(
                    context,
                    404,
                    "ORDER_RESULT_NOT_FOUND",
                    "Todavía no existe un resultado registrado " +
                    "para esta referencia.");
            }

            return Results.Ok(result);
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "Fallo de SQL Server al consultar el pedido {Reference}.",
                id);

            return DatabaseUnavailable(context);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is long contentLength &&
            contentLength > MaxBodyBytes)
        {
            throw new ErpOrderApiException(
                413,
                "PAYLOAD_TOO_LARGE",
                "El pedido supera el tamaño máximo de 64 KB.");
        }

        using var body = new MemoryStream();

        var buffer = new byte[8192];

        while (true)
        {
            int read = await request.Body.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (read == 0)
            {
                break;
            }

            if (body.Length + read > MaxBodyBytes)
            {
                throw new ErpOrderApiException(
                    413,
                    "PAYLOAD_TOO_LARGE",
                    "El pedido supera el tamaño máximo de 64 KB.");
            }

            body.Write(buffer, 0, read);
        }

        if (body.Length == 0)
        {
            throw new ErpOrderApiException(
                400,
                "EMPTY_BODY",
                "El cuerpo de la solicitud está vacío.");
        }

        return body.ToArray();
    }

    private static IResult DatabaseUnavailable(
        HttpContext context)
    {
        context.Response.Headers["Retry-After"] = "5";

        return Problem(
            context,
            503,
            "ERP_TEMPORARILY_UNAVAILABLE",
            "No se pudo completar la operación. Consulta su resultado " +
            "o reintenta con la misma clave y el mismo contenido.");
    }

    private static IResult Problem(
        HttpContext context,
        int status,
        string code,
        string detail)
    {
        return Results.Problem(
            statusCode: status,
            title: "No se pudo completar la solicitud",
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = context.TraceIdentifier,
                ["correlationId"] = context.Items["CorrelationId"]
            });
    }
}