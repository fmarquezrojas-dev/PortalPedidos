using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Application.Interfaces.Integration;

namespace Portal.Web.BackgroundServices;

public sealed class OrderOutboxWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(5);

    private const int MaxMessagesPerCycle = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderOutboxWorker> _logger;

    public OrderOutboxWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "El servicio de procesamiento de pedidos se inició.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessCycleAsync(stoppingToken);

                await Task.Delay(
                    PollInterval,
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Cierre normal de la aplicación.
        }
        finally
        {
            _logger.LogInformation(
                "El servicio de procesamiento de pedidos se detuvo.");
        }
    }

    private async Task ProcessCycleAsync(
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < MaxMessagesPerCycle; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Cada mensaje utiliza sus propias dependencias
                // y su propio contexto de base de datos.
                await using var scope =
                    _scopeFactory.CreateAsyncScope();

                var processor = scope.ServiceProvider
                    .GetRequiredService<IOrderOutboxProcessor>();

                bool processed = await processor.ProcessNextAsync(
                    cancellationToken);

                if (!processed)
                {
                    // No hay más mensajes disponibles en este momento.
                    return;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Se interrumpió un ciclo de procesamiento de pedidos. " +
                    "El servicio volverá a revisar la cola.");

                // Evitar repetir inmediatamente una operación
                // después de un fallo inesperado.
                return;
            }
        }
    }
}