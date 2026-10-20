using System;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Erp;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Erp;

namespace Portal.Infrastructure.Integrations.Erp;

public sealed class ErpSimulatorOrderClient : IErpOrderClient
{
    private const int MaxPayloadBytes = 64 * 1024;

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public ErpSimulatorOrderClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ErpOrderResponseDto> SubmitAsync(
        Guid externalReference,
        Guid idempotencyKey,
        string payloadJson,
        string payloadHash,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        if (externalReference == Guid.Empty ||
            idempotencyKey == Guid.Empty ||
            externalReference != idempotencyKey)
        {
            throw LocalError(
                "OUTBOX_INVALID_REFERENCE",
                "La referencia y la clave del mensaje son inválidas.");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw LocalError(
                "OUTBOX_EMPTY_PAYLOAD",
                "El mensaje no contiene un pedido.");
        }

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        if (payloadBytes.Length > MaxPayloadBytes)
        {
            throw LocalError(
                "OUTBOX_PAYLOAD_TOO_LARGE",
                "El mensaje supera el tamaño máximo permitido.");
        }

        string calculatedHash = Convert.ToHexString(
                SHA256.HashData(payloadBytes))
            .ToLowerInvariant();

        if (!string.Equals(
                calculatedHash,
                payloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw LocalError(
                "OUTBOX_HASH_MISMATCH",
                "El contenido del mensaje no coincide con su hash.");
        }

        ValidatePayloadReference(payloadJson, externalReference);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "sim-api/v1/orders");

        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey.ToString("D"));

        // Enviar los bytes del JSON almacenado.
        // No reconstruir ni volver a serializar el pedido.
        request.Content = new ByteArrayContent(payloadBytes);

        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };

        var result = await SendAsync(
            request,
            externalReference,
            isSubmission: true,
            expectedPayloadHash: payloadHash,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        return result ?? throw InvalidResponse(
            isSubmission: true,
            httpStatusCode: null);
    }

    public Task<ErpOrderResponseDto?> GetResultAsync(
        Guid externalReference,
        string expectedPayloadHash,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        if (externalReference == Guid.Empty)
        {
            throw LocalError(
                "OUTBOX_INVALID_REFERENCE",
                "La referencia del pedido es inválida.");
        }

        return GetResultCoreAsync(
            externalReference,
            expectedPayloadHash,
            correlationId,
            cancellationToken);
    }

    private async Task<ErpOrderResponseDto?> GetResultCoreAsync(
        Guid externalReference,
        string expectedPayloadHash,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "sim-api/v1/orders/by-external-reference/" +
            externalReference.ToString("D"));

        return await SendAsync(
            request,
            externalReference,
            isSubmission: false,
            expectedPayloadHash: expectedPayloadHash,
            correlationId: correlationId,
            cancellationToken: cancellationToken);
    }

    private async Task<ErpOrderResponseDto?> SendAsync(
        HttpRequestMessage request,
        Guid expectedReference,
        bool isSubmission,
        string expectedPayloadHash,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (correlationId == Guid.Empty || string.IsNullOrWhiteSpace(expectedPayloadHash) || expectedPayloadHash.Length != 64 ||
            !expectedPayloadHash.All(Uri.IsHexDigit))
            throw LocalError("OUTBOX_INVALID_METADATA", "La correlación o el hash esperado no son válidos.");
        request.Headers.Add("X-Correlation-ID", correlationId.ToString("D"));
        int? receivedStatus = null;

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            receivedStatus = (int)response.StatusCode;

            string body = await response.Content.ReadAsStringAsync(
                cancellationToken);

            // Solo este 404 significa "resultado no registrado".
            // Un 404 de ruta incorrecta debe considerarse un error.
            if (!isSubmission &&
                response.StatusCode == HttpStatusCode.NotFound &&
                ReadProblemCode(body) == "ORDER_RESULT_NOT_FOUND")
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw CreateHttpException(
                    response,
                    isSubmission);
            }

            var result =
                JsonSerializer.Deserialize<ErpOrderResponseDto>(
                    body,
                    JsonOptions);

            if (!IsValidResult(result, expectedReference))
            {
                throw InvalidResponse(
                    isSubmission,
                    receivedStatus);
            }

            if (!string.Equals(result!.PayloadHash, expectedPayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                // Una respuesta para la misma referencia pero distinto contenido no puede confirmar este pedido.
                throw new ErpOrderCommunicationException(
                    code: "ERP_REMOTE_PAYLOAD_MISMATCH",
                    message: "El resultado remoto pertenece a otro contenido. Se requiere revisión técnica.",
                    isTransient: false, outcomeUncertain: true, httpStatusCode: receivedStatus);
            }
            return result;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ErpOrderCommunicationException(
                code: "ERP_TIMEOUT",
                message: "El ERP no respondió dentro del tiempo permitido.",
                isTransient: true,
                outcomeUncertain: isSubmission,
                httpStatusCode: receivedStatus,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ErpOrderCommunicationException(
                code: "ERP_CONNECTION_FAILED",
                message: "No se pudo completar la comunicación con el ERP.",
                isTransient: true,
                outcomeUncertain: isSubmission,
                httpStatusCode: receivedStatus,
                innerException: exception);
        }
        catch (JsonException exception)
        {
            throw new ErpOrderCommunicationException(
                code: "ERP_INVALID_RESPONSE",
                message: "El ERP devolvió una respuesta JSON inválida.",
                isTransient: true,
                outcomeUncertain: isSubmission,
                httpStatusCode: receivedStatus,
                innerException: exception);
        }
    }

    private static void ValidatePayloadReference(
        string payloadJson,
        Guid expectedReference)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);

            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(
                    "externalReference",
                    out var referenceElement) ||
                referenceElement.ValueKind != JsonValueKind.String ||
                !referenceElement.TryGetGuid(out var reference) ||
                reference != expectedReference)
            {
                throw LocalError(
                    "OUTBOX_REFERENCE_MISMATCH",
                    "La referencia del JSON no coincide con la del mensaje.");
            }
        }
        catch (JsonException exception)
        {
            throw new ErpOrderCommunicationException(
                code: "OUTBOX_INVALID_JSON",
                message: "El mensaje almacenado no contiene un JSON válido.",
                isTransient: false,
                outcomeUncertain: false,
                innerException: exception);
        }
    }

    private static bool IsValidResult(
        ErpOrderResponseDto? result,
        Guid expectedReference)
    {
        if (result is null ||
            string.IsNullOrWhiteSpace(result.PayloadHash) ||
            result.PayloadHash.Length != 64 || !result.PayloadHash.All(Uri.IsHexDigit) ||
            result.ExternalReference != expectedReference ||
            string.IsNullOrWhiteSpace(result.Code) ||
            result.Code.Length > 80 ||
            string.IsNullOrWhiteSpace(result.Message) ||
            result.Message.Length > 500 ||
            result.ProcessedAtUtc == default ||
            result.ProcessedAtUtc.Offset != TimeSpan.Zero)
        {
            return false;
        }

        if (result.Status == "Accepted")
        {
            return result.Code == "ORDER_ACCEPTED" &&
                !string.IsNullOrWhiteSpace(result.ExternalOrderId) &&
                result.ExternalOrderId.Length <= 50;
        }

        if (result.Status == "Rejected")
        {
            return result.ExternalOrderId is null;
        }

        return false;
    }

    private static string? ReadProblemCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind ==
                    JsonValueKind.Object &&
                document.RootElement.TryGetProperty(
                    "code",
                    out var code) &&
                code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
            // No corresponde al formato ProblemDetails esperado.
        }

        return null;
    }

    private static ErpOrderCommunicationException CreateHttpException(
        HttpResponseMessage response,
        bool isSubmission)
    {
        int status = (int)response.StatusCode;

        bool transient =
            status == 408 ||
            status == 429 ||
            status >= 500;

        // Un conflicto requiere conciliación o revisión.
        // No debe interpretarse como rechazo del pedido.
        bool outcomeUncertain =
            isSubmission &&
            (transient || status == 409);

        string code = status switch
        {
            400 => "ERP_INVALID_REQUEST",
            401 => "ERP_UNAUTHORIZED",
            403 => "ERP_FORBIDDEN",
            404 => "ERP_ENDPOINT_NOT_FOUND",
            408 => "ERP_REQUEST_TIMEOUT",
            409 => "ERP_IDEMPOTENCY_CONFLICT",
            413 => "ERP_PAYLOAD_TOO_LARGE",
            415 => "ERP_UNSUPPORTED_CONTENT_TYPE",
            429 => "ERP_RATE_LIMITED",
            >= 500 => "ERP_SERVER_ERROR",
            _ => "ERP_UNEXPECTED_HTTP_STATUS"
        };

        return new ErpOrderCommunicationException(
            code: code,
            message: $"El ERP respondió con el estado HTTP {status}.",
            isTransient: transient,
            outcomeUncertain: outcomeUncertain,
            httpStatusCode: status,
            retryAfterSeconds: ReadRetryAfterSeconds(response));
    }

    private static int? ReadRetryAfterSeconds(
        HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        double seconds;

        if (retryAfter.Delta.HasValue)
        {
            seconds = retryAfter.Delta.Value.TotalSeconds;
        }
        else if (retryAfter.Date.HasValue)
        {
            seconds = (
                retryAfter.Date.Value -
                DateTimeOffset.UtcNow).TotalSeconds;
        }
        else
        {
            return null;
        }

        return (int)Math.Clamp(
            Math.Ceiling(seconds),
            0d,
            86400d);
    }

    private static ErpOrderCommunicationException LocalError(
        string code,
        string message)
    {
        return new ErpOrderCommunicationException(
            code: code,
            message: message,
            isTransient: false,
            outcomeUncertain: false);
    }

    private static ErpOrderCommunicationException InvalidResponse(
        bool isSubmission,
        int? httpStatusCode)
    {
        return new ErpOrderCommunicationException(
            code: "ERP_INVALID_RESPONSE",
            message:
                "La respuesta del ERP no contiene un resultado válido " +
                "para el pedido solicitado.",
            isTransient: true,
            outcomeUncertain: isSubmission,
            httpStatusCode: httpStatusCode);
    }
}