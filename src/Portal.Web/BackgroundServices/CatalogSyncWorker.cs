using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Catalog;
using Portal.Infrastructure.Persistence;

namespace Portal.Web.BackgroundServices;

public sealed class CatalogSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogSyncWorker> _logger;

    public CatalogSyncWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<CatalogSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Sincronización automática del catálogo iniciada.");

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(60));

        try
        {
            // Primera ejecución al iniciar el portal.
            await SynchronizeOnceAsync(stoppingToken);

            // Las siguientes ejecuciones se realizan de forma secuencial.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SynchronizeOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // El portal se está cerrando.
        }

        _logger.LogInformation(
            "Sincronización automática del catálogo detenida.");
    }

    private async Task SynchronizeOnceAsync(
        CancellationToken cancellationToken)
    {
        DateTime attemptStartedAtUtc = DateTime.UtcNow;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var syncService = scope.ServiceProvider
                .GetRequiredService<ICatalogSyncService>();

            var result = await syncService.SynchronizeAsync(
                cancellationToken);

            _logger.LogInformation(
                "Catálogo sincronizado. Versión: {Version}. " +
                "Clientes: {Customers}. Productos: {Products}. " +
                "Existencias: {Stocks}.",
                result.CatalogVersion,
                result.CustomersReceived,
                result.ProductsReceived,
                result.StocksReceived);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ErpClientException exception)
            when (exception.Code == "CATALOG_SYNC_BUSY")
        {
            // Otra ejecución ya está guardando el catálogo.
            // No marcar como fallo una sincronización concurrente.
            _logger.LogInformation(
                "Se omitió una sincronización porque otra está en curso.");
        }
        catch (ErpClientException exception)
        {
            _logger.LogWarning(
                "No se pudo sincronizar el catálogo. Código: {Code}. " +
                "Detalle: {Message}",
                exception.Code,
                exception.Message);

            await RecordFailureAsync(
                exception.Code,
                attemptStartedAtUtc,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "La sincronización falló por un error de tipo {ErrorType}. " +
                "El proceso volverá a intentarlo.",
                exception.GetType().Name);

            await RecordFailureAsync(
                "CATALOG_SYNC_FAILED",
                attemptStartedAtUtc,
                cancellationToken);
        }
    }

    private async Task RecordFailureAsync(
        string errorCode,
        DateTime attemptStartedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            // Usar un contexto nuevo, separado del intento fallido.
            await using var scope = _scopeFactory.CreateAsyncScope();

            var dbContext = scope.ServiceProvider
                .GetRequiredService<PortalDbContext>();

            await dbContext.CatalogSyncStates
                .Where(state =>
                    state.Id == 1 &&
                    (
                        state.LastSuccessAtUtc == null ||
                        state.LastSuccessAtUtc < attemptStartedAtUtc
                    ))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        state => state.LastErrorCode,
                        errorCode),
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "No se pudo registrar el fallo de sincronización. " +
                "Tipo: {ErrorType}.",
                exception.GetType().Name);
        }
    }
}