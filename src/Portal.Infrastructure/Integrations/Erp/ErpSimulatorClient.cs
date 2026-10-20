using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Erp;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Erp;

namespace Portal.Infrastructure.Integrations.Erp;

public sealed class ErpSimulatorClient : IErpClient
{
    private readonly HttpClient _httpClient;

    public ErpSimulatorClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ErpCatalogSnapshotDto> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                "sim-api/v1/catalog",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ErpClientException(
                    "ERP_UNAUTHORIZED",
                    "El simulador rechazó la clave de acceso. " +
                    "Revisa la configuración Erp:ApiKey del portal.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new ErpClientException(
                    "ERP_FORBIDDEN",
                    "El simulador no permitió acceder al catálogo.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ErpClientException(
                    "ERP_ENDPOINT_NOT_FOUND",
                    "No se encontró el endpoint del catálogo. " +
                    "Revisa la dirección del simulador.");
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new ErpClientException(
                    "ERP_UNAVAILABLE",
                    "El simulador respondió con un error de servidor. " +
                    $"Estado HTTP: {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ErpClientException(
                    "ERP_HTTP_ERROR",
                    "No se pudo consultar el catálogo. " +
                    $"Estado HTTP: {(int)response.StatusCode}.");
            }

            var snapshot = await response.Content
                .ReadFromJsonAsync<ErpCatalogSnapshotDto>(
                    cancellationToken: cancellationToken);

            if (snapshot is null)
            {
                throw new ErpClientException(
                    "ERP_INVALID_RESPONSE",
                    "El simulador devolvió una respuesta vacía.");
            }

            if (snapshot.CatalogVersion <= 0 ||
                snapshot.ObservedAtUtc == default ||
                string.IsNullOrWhiteSpace(snapshot.Currency) ||
                string.IsNullOrWhiteSpace(snapshot.WarehouseCode) ||
                snapshot.Customers is null ||
                snapshot.Items is null ||
                snapshot.Stocks is null)
            {
                throw new ErpClientException(
                    "ERP_INVALID_RESPONSE",
                    "La respuesta del catálogo contiene " +
                    "datos generales inválidos o incompletos.");
            }

            return snapshot;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ErpClientException(
                "ERP_TIMEOUT",
                "El simulador no respondió dentro del tiempo permitido.");
        }
        catch (HttpRequestException)
        {
            throw new ErpClientException(
                "ERP_CONNECTION_FAILED",
                "No se pudo completar la comunicación con el simulador. " +
                "Comprueba que esté ejecutándose, que la dirección sea " +
                "correcta y que el certificado HTTPS sea de confianza.");
        }
        catch (JsonException)
        {
            throw new ErpClientException(
                "ERP_INVALID_RESPONSE",
                "La respuesta del simulador no tiene el formato JSON esperado.");
        }
    }
}