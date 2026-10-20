using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ErpSimulator.Api.Development;
using ErpSimulator.Api.Endpoints;
using ErpSimulator.Api.Persistence;
using ErpSimulator.Api.Security;
using ErpSimulator.Api.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ErpSimulator.Api.Services.Orders;
using Microsoft.Extensions.Logging;

// 1. Detectar el comando de carga inicial.
bool seedCatalog = args.Contains("--seed-catalog");

var builder = WebApplication.CreateBuilder(
    args.Where(arg => arg != "--seed-catalog").ToArray());

builder.Services.AddHealthChecks().AddCheck<SimulatorDatabaseHealthCheck>(
    "simulator_database", failureStatus: HealthStatus.Unhealthy,
    tags: new[] { "ready" }, timeout: TimeSpan.FromSeconds(10));

// 2. Registrar respuestas estándar para errores.
builder.Services.AddProblemDetails();

// 3. Obtener la conexión del simulador.
var connectionString = builder.Configuration
    .GetConnectionString("ErpSimulatorDb");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No se encontró una conexión válida para ErpSimulatorDb.");
}

// 4. Registrar el contexto de SQL Server.
builder.Services.AddDbContext<ErpSimulatorDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// 5. Registrar el servicio de recepción de pedidos.
builder.Services.AddScoped<ErpOrderService>();

// 6. Construir la aplicación.
var app = builder.Build();

// 7. Ejecutar la carga inicial solo mediante comando.
if (seedCatalog)
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "La preparación del catálogo solo puede ejecutarse " +
            "en el entorno Development.");
    }

    using var scope = app.Services.CreateScope();

    var dbContext = scope.ServiceProvider
        .GetRequiredService<ErpSimulatorDbContext>();

    await ErpCatalogSeeder.RunAsync(dbContext);

    return;
}

// 8. Leer la clave desde la configuración.
string apiKey = builder.Configuration["Simulator:ApiKey"]
    ?? string.Empty;

if (string.IsNullOrWhiteSpace(apiKey) ||
    apiKey.Length < 32 ||
    apiKey.Length > 512 ||
    apiKey.Contains('\r') ||
    apiKey.Contains('\n') ||
    apiKey == "PEGA_AQUI_LA_CLAVE_GENERADA" ||
    apiKey == "PEGA_TU_CLAVE_AQUI")
{
    throw new InvalidOperationException(
        "Configura Simulator:ApiKey en los secretos de " +
        "ErpSimulator.Api con la clave que generaste.");
}

// 9. Configurar el tratamiento de errores y HTTPS.
app.UseExceptionHandler();
app.UseHttpsRedirection();

// 10. Comprobaciones del proceso.
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        service = "ErpSimulator.Api",
        status = "running"
    });
});

app.MapGet("/health/live", () =>
{
    return Results.Ok(new
    {
        service = "ErpSimulator.Api",
        status = "running"
    });
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    AllowCachingResponses = false
});

// 11. Crear el grupo protegido de endpoints.
var simulatorApi = app.MapGroup("/sim-api/v1");
simulatorApi.AddEndpointFilter(new CorrelationIdEndpointFilter());

simulatorApi.AddEndpointFilter(
    new ApiKeyEndpointFilter(apiKey));


// Falla controlada para pruebas de respuesta perdida.
bool loseOrderResponseOnce = app.Configuration.GetValue<bool>(
    "Simulator:Faults:LoseOrderResponseOnce");

if (loseOrderResponseOnce)
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "La simulación de respuesta perdida " +
            "solo puede habilitarse en Development.");
    }

    string? targetOrderText = app.Configuration[
        "Simulator:Faults:TargetOrderId"];

    if (!Guid.TryParse(
            targetOrderText,
            out Guid targetOrderId) ||
        targetOrderId == Guid.Empty)
    {
        throw new InvalidOperationException(
            "Configura Simulator:Faults:TargetOrderId " +
            "con el identificador GUID del pedido de prueba.");
    }

    var faultLogger = app.Services
        .GetRequiredService<
            ILogger<LoseOrderResponseOnceFilter>>();

    simulatorApi.AddEndpointFilter(
        new LoseOrderResponseOnceFilter(
            targetOrderId,
            faultLogger));

    app.Logger.LogWarning(
        "Prueba de respuesta perdida habilitada " +
        "para el pedido {OrderId}. " +
        "Se interrumpirá una respuesta después de su aceptación.",
        targetOrderId);
}

// 12. Publicar el catálogo.
simulatorApi.MapGet(
    "/catalog",
    CatalogEndpoints.GetAsync);

// 13. Recibir pedidos.
simulatorApi.MapPost(
    "/orders",
    OrderEndpoints.SubmitAsync);

// 14. Consultar el resultado de un pedido.
simulatorApi.MapGet(
    "/orders/by-external-reference/{id:guid}",
    OrderEndpoints.GetByExternalReferenceAsync);

// 15. Iniciar el servidor.
app.Run();
