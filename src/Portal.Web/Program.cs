using System;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using PortalPedidos.Hosting;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Audit;
using Portal.Application.Interfaces.Catalog;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Dashboard;
using Portal.Application.Interfaces.Erp;
using Portal.Application.Interfaces.Integration;
using Portal.Application.Interfaces.Orders;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Integration;
using Portal.Infrastructure.Integrations.Erp;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Audit;
using Portal.Infrastructure.Queries.Catalog;
using Portal.Infrastructure.Queries.Customers;
using Portal.Infrastructure.Queries.Dashboard;
using Portal.Infrastructure.Queries.Integration;
using Portal.Infrastructure.Queries.Orders;
using Portal.Infrastructure.Queries.Security;
using Portal.Infrastructure.Services.Orders;
using Portal.Infrastructure.Synchronization;
using Portal.Web.BackgroundServices;
using Portal.Web.HealthChecks;
using Portal.Infrastructure.Services.Security;
using Portal.Web.Development;
using Portal.Web.Security;
using Portal.Application.Interfaces.Teams;
using Portal.Infrastructure.Services.Teams;
using Portal.Infrastructure.Services.Customers;


// ============================================================
// 1. Identificar los comandos de desarrollo.
// ============================================================

bool seedRoles = args.Contains("--seed-roles");
bool demoMigrate = args.Contains("--demo-migrate");
bool seedUsers = args.Contains("--seed-users");
bool checkErp = args.Contains("--check-erp");
bool syncCatalog = args.Contains("--sync-catalog");

bool isMaintenanceCommand =
    demoMigrate ||
    seedRoles ||
    seedUsers ||
    checkErp ||
    syncCatalog;

// La creación de roles y usuarios puede ejecutarse conjuntamente.
// Las operaciones del catálogo deben ejecutarse por separado.
int commandGroups =
    (demoMigrate ? 1 : 0) +
    (seedRoles || seedUsers ? 1 : 0) +
    (checkErp ? 1 : 0) +
    (syncCatalog ? 1 : 0);

if (commandGroups > 1)
{
    throw new InvalidOperationException(
        "Ejecuta por separado la preparación de usuarios, " +
        "--check-erp y --sync-catalog.");
}

// Excluir nuestros comandos personalizados de la configuración.
var builder = WebApplication.CreateBuilder(
    args.Where(arg =>
        arg != "--demo-migrate" &&
        arg != "--seed-roles" &&
        arg != "--seed-users" &&
        arg != "--check-erp" &&
        arg != "--sync-catalog")
    .ToArray());

bool dockerDemo = DockerDemo.Configure(builder, "PortalDb", "PortalPedidosDockerDemo");
if (demoMigrate && !dockerDemo)
    throw new InvalidOperationException("--demo-migrate requires Docker demo mode.");

if (dockerDemo)
{
    var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        "/https/server.pfx", builder.Configuration["Kestrel:Certificates:Default:Password"]);
    builder.Services.AddDataProtection()
        .SetApplicationName("PortalPedidos.DockerDemo")
        .PersistKeysToFileSystem(new DirectoryInfo("/var/portal-keys"))
        .ProtectKeysWithCertificate(certificate);
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = "PortalPedidos.Demo.Antiforgery";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(options =>
    {
        options.Cookie.Name = "PortalPedidos.Demo.TempData";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
}

// ============================================================
// 2. Registrar MVC y el acceso al contexto HTTP.
// ============================================================

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Portal.Application.Interfaces.Search.ISearchAssistant,
    Portal.Infrastructure.Services.Search.LocalSearchAssistant>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddPortalHealthChecks();
builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();
// Revocar cookies con permisos anteriores en la siguiente solicitud.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

// ============================================================
// 3. Obtener la conexión a SQL Server.
// ============================================================

var connectionString = builder.Configuration
    .GetConnectionString("PortalDb");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No se encontró una conexión válida para PortalDb.");
}

// ============================================================
// 4. Registrar el contexto de base de datos.
// ============================================================

builder.Services.AddDbContext<PortalDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// ============================================================
// 5. Configurar usuarios, roles y contraseñas.
// ============================================================

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);

        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<PortalDbContext>()
    .AddDefaultTokenProviders();

// ============================================================
// 6. Configurar la cookie y la validación de la sesión.
// ============================================================

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = dockerDemo ? "PortalPedidos.Demo.Auth" : "PortalPedidos.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;

    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;

    options.Events.OnValidatePrincipal =
        ApplicationCookieValidator.ValidateAsync;
});

// ============================================================
// 7. Registrar las políticas centralizadas.
// ============================================================

// Las políticas se definen en Security/PortalAuthorization.cs.
builder.Services.AddAuthorization(
    PortalAuthorization.Configure);

// ============================================================
// 8. Registrar servicios y consultas del portal.
// ============================================================

// Usuario autenticado y permisos.
builder.Services.AddScoped<
    ICurrentUser,
    CurrentUser>();

// Catálogo de productos.
builder.Services.AddScoped<
    IProductCatalogQuery,
    ProductCatalogQuery>();

// Sincronización del catálogo.
builder.Services.AddScoped<
    ICatalogSyncService,
    CatalogSyncService>();

// Directorio de clientes.
builder.Services.AddScoped<
    ICustomerDirectoryQuery,
    CustomerDirectoryQuery>();

// Consulta de pedidos.
builder.Services.AddScoped<
    IOrderQuery,
    OrderQuery>();

// Gestión de borradores.
builder.Services.AddScoped<
    IOrderDraftService,
    OrderDraftService>();

// Copia de pedidos rechazados a nuevos borradores.
builder.Services.AddScoped<
    ICopyRejectedOrderService,
    CopyRejectedOrderService>();

// Procesamiento de mensajes pendientes de integración.
builder.Services.AddScoped<
    IOrderOutboxProcessor,
    OrderOutboxProcessor>();

// Consulta del monitor de integraciones.
builder.Services.AddScoped<
    IIntegrationMonitorQuery,
    IntegrationMonitorQuery>();

// Recuperación autorizada de integraciones.
builder.Services.AddScoped<
    IIntegrationRecoveryService,
    IntegrationRecoveryService>();

// Consulta de auditoría.
builder.Services.AddScoped<
    IAuditQuery,
    AuditQuery>();

// Consulta del panel.
builder.Services.AddScoped<
    IDashboardQuery,
    DashboardQuery>();

// Consulta administrativa de roles.
builder.Services.AddScoped<
    IRoleDirectoryQuery,
    RoleDirectoryQuery>();

// Gestión de equipos comerciales.
builder.Services.AddScoped<
    ISalesTeamService,
    SalesTeamService>();




builder.Services.AddScoped<
    ICustomerEligibilityService,
    CustomerEligibilityService>();

builder.Services.AddScoped<
    ICustomerEligibilityQuery,
    CustomerEligibilityQuery>();


// ============================================================
// 9. Registrar el cliente HTTP del catálogo del ERP.
// ============================================================

builder.Services
    .AddHttpClient<IErpClient, ErpSimulatorClient>(
        (serviceProvider, client) =>
        {
            var configuration = serviceProvider
                .GetRequiredService<IConfiguration>();

            string baseUrl =
                configuration["Erp:BaseUrl"] ?? string.Empty;

            string apiKey =
                configuration["Erp:ApiKey"] ?? string.Empty;

            if (!Uri.TryCreate(
                    baseUrl,
                    UriKind.Absolute,
                    out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(baseUri.UserInfo) ||
                !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment) ||
                baseUri.AbsolutePath != "/")
            {
                throw new InvalidOperationException(
                    "Erp:BaseUrl debe ser una dirección HTTPS raíz válida. " +
                    "Ejemplo: https://localhost:7117/");
            }

            if (string.IsNullOrWhiteSpace(apiKey) ||
                apiKey.Length < 32 ||
                apiKey.Length > 512 ||
                apiKey.Contains('\r') ||
                apiKey.Contains('\n') ||
                apiKey == "REEMPLAZA_CON_LA_CLAVE_REAL_DEL_SIMULADOR" ||
                apiKey == "PEGA_AQUI_LA_CLAVE_GENERADA" ||
                apiKey == "PEGA_TU_CLAVE_AQUI")
            {
                throw new InvalidOperationException(
                    "Configura una clave válida en Erp:ApiKey " +
                    "dentro de los secretos de Portal.Web.");
            }

            client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(30);

            // Límite de 2 MB para el catálogo.
            client.MaxResponseContentBufferSize =
                2 * 1024 * 1024;

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            client.DefaultRequestHeaders.Add(
                "X-Api-Key",
                apiKey);
        })
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });

// ============================================================
// 10. Registrar el cliente HTTP de pedidos del ERP.
// ============================================================

builder.Services
    .AddHttpClient<IErpOrderClient, ErpSimulatorOrderClient>(
        (serviceProvider, client) =>
        {
            var configuration = serviceProvider
                .GetRequiredService<IConfiguration>();

            string baseUrl =
                configuration["Erp:BaseUrl"] ?? string.Empty;

            string apiKey =
                configuration["Erp:ApiKey"] ?? string.Empty;

            if (!Uri.TryCreate(
                    baseUrl,
                    UriKind.Absolute,
                    out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps ||
                baseUri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment) ||
                !string.IsNullOrEmpty(baseUri.UserInfo))
            {
                throw new InvalidOperationException(
                    "Erp:BaseUrl debe ser una dirección HTTPS raíz válida. " +
                    "Ejemplo: https://localhost:7117/");
            }

            if (string.IsNullOrWhiteSpace(apiKey) ||
                apiKey.Length < 32 ||
                apiKey.Length > 512 ||
                apiKey.Contains('\r') ||
                apiKey.Contains('\n') ||
                apiKey == "REEMPLAZA_CON_LA_CLAVE_REAL_DEL_SIMULADOR" ||
                apiKey == "PEGA_AQUI_LA_CLAVE_GENERADA" ||
                apiKey == "PEGA_TU_CLAVE_AQUI")
            {
                throw new InvalidOperationException(
                    "Configura una clave válida en Erp:ApiKey " +
                    "dentro de los secretos de Portal.Web.");
            }

            client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(30);

            // Límite de 64 KB para respuestas de pedidos.
            client.MaxResponseContentBufferSize =
                64 * 1024;

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            client.DefaultRequestHeaders.Add(
                "X-Api-Key",
                apiKey);
        })
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });

// ============================================================
// 11. Registrar los servicios en segundo plano.
// ============================================================

// Durante los comandos de mantenimiento no se registran
// los trabajadores automáticos.
bool catalogSyncEnabled = builder.Configuration
    .GetValue<bool>("CatalogSync:Enabled");

if (catalogSyncEnabled && !isMaintenanceCommand)
{
    builder.Services.AddHostedService<CatalogSyncWorker>();
}

bool outboxEnabled = builder.Configuration
    .GetValue<bool>("Outbox:Enabled");

if (outboxEnabled && !isMaintenanceCommand)
{
    builder.Services.AddHostedService<OrderOutboxWorker>();
}



builder.Services.AddScoped<
    ICustomerPortfolioService,
    CustomerPortfolioService>();

builder.Services.AddScoped<
    ICustomerPortfolioQuery,
    CustomerPortfolioQuery>();



builder.Services.AddScoped<
    ICustomerPortfolioListQuery,
    CustomerPortfolioListQuery>();
// ============================================================
// 12. Construir la aplicación.
// ============================================================

// Todos los registros de servicios deben estar antes de esta línea.
var app = builder.Build();

if (demoMigrate)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.MigrateAsync();
    return;
}

// ============================================================
// 13. Restringir los comandos al entorno Development.
// ============================================================

if (isMaintenanceCommand &&
    !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Los comandos --seed-roles, --seed-users, " +
        "--check-erp y --sync-catalog solo pueden ejecutarse " +
        "en el entorno Development.");
}

// ============================================================
// 14. Sincronizar el catálogo por comando.
// ============================================================

if (syncCatalog)
{
    using var scope = app.Services.CreateScope();

    try
    {
        var syncService = scope.ServiceProvider
            .GetRequiredService<ICatalogSyncService>();

        Console.WriteLine();
        Console.WriteLine(
            "Iniciando sincronización del catálogo...");

        var result = await syncService.SynchronizeAsync();

        Console.WriteLine();
        Console.WriteLine(
            "Catálogo sincronizado correctamente.");

        Console.WriteLine(
            $"Versión guardada: {result.CatalogVersion}");

        Console.WriteLine(
            $"Clientes recibidos: {result.CustomersReceived}");

        Console.WriteLine(
            $"Productos recibidos: {result.ProductsReceived}");

        Console.WriteLine(
            $"Registros de stock recibidos: {result.StocksReceived}");

        Console.WriteLine(
            $"Fecha de sincronización UTC: {result.CompletedAtUtc:O}");
    }
    catch (ErpClientException exception)
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            $"Error de sincronización: {exception.Code}");

        Console.Error.WriteLine(exception.Message);

        Environment.ExitCode = 1;
    }
    catch (DbUpdateException)
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            "No se pudieron guardar los cambios del catálogo.");

        Console.Error.WriteLine(
            "Revisa las migraciones, las restricciones y " +
            "la estructura de PortalDb. " +
            "La transacción no se confirmó.");

        Environment.ExitCode = 1;
    }
    catch (SqlException exception)
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            $"Error de SQL Server. Número: {exception.Number}");

        Console.Error.WriteLine(
            "Revisa la conexión y la disponibilidad de PortalDb.");

        Environment.ExitCode = 1;
    }

    // Terminar sin iniciar el servidor web.
    return;
}

// ============================================================
// 15. Consultar la conexión con el ERP por comando.
// ============================================================

// Esta operación consulta el catálogo, pero no lo guarda.
if (checkErp)
{
    using var scope = app.Services.CreateScope();

    try
    {
        var erpClient = scope.ServiceProvider
            .GetRequiredService<IErpClient>();

        Console.WriteLine();

        Console.WriteLine(
            "Consultando el catálogo del ERP simulado...");

        var snapshot = await erpClient.GetCatalogAsync();

        Console.WriteLine();

        Console.WriteLine(
            "Conexión con el ERP completada correctamente.");

        Console.WriteLine(
            $"Versión del catálogo: {snapshot.CatalogVersion}");

        Console.WriteLine(
            $"Fecha de lectura UTC: {snapshot.ObservedAtUtc:O}");

        Console.WriteLine(
            $"Moneda: {snapshot.Currency}");

        Console.WriteLine(
            $"Almacén: {snapshot.WarehouseCode}");

        Console.WriteLine(
            $"Clientes: {snapshot.Customers.Count}");

        Console.WriteLine(
            $"Productos: {snapshot.Items.Count}");

        Console.WriteLine(
            $"Registros de stock: {snapshot.Stocks.Count}");

        Console.WriteLine();

        Console.WriteLine(
            "Esta consulta no modifica la base de datos del portal.");
    }
    catch (ErpClientException exception)
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            $"Error de integración: {exception.Code}");

        Console.Error.WriteLine(exception.Message);

        Environment.ExitCode = 1;
    }

    // Terminar sin iniciar el servidor web.
    return;
}

// ============================================================
// 16. Preparar roles y usuarios por comando.
// ============================================================

if (seedRoles || seedUsers)
{
    // Crear primero los roles que todavía no existan.
    using (var roleScope = app.Services.CreateScope())
    {
        var roleManager = roleScope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

        await DevelopmentRoleSeeder.RunAsync(roleManager);
    }

    if (seedUsers)
    {
        var accounts = new[]
        {
            (
                Email: "vendedor@portal.test",
                Role: AppRoles.Seller
            ),
            (
                Email: "operador@portal.test",
                Role: AppRoles.Operator
            ),
            (
                Email: "admin@portal.test",
                Role: AppRoles.Admin
            )
        };

        foreach (var account in accounts)
        {
            // Cada cuenta utiliza su propio contexto y servicios.
            using var userScope = app.Services.CreateScope();

            var dbContext = userScope.ServiceProvider
                .GetRequiredService<PortalDbContext>();

            var userManager = userScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var roleManager = userScope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole>>();

            var existingUser = await userManager.FindByEmailAsync(
                account.Email);

            string password = string.Empty;

            // Solicitar contraseña únicamente para cuentas nuevas.
            if (existingUser is null && dockerDemo)
            {
                password = File.ReadAllText("/run/secrets/seed-password").Trim();
                if (string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("A demo seed password is required.");
            }
            else if (existingUser is null)
            {
                Console.WriteLine();

                Console.WriteLine(
                    $"Crear usuario: {account.Email} | " +
                    $"Rol: {account.Role}");

                Console.WriteLine(
                    "Usa al menos 12 caracteres, con mayúscula, " +
                    "minúscula, número y símbolo.");

                Console.WriteLine(
                    "La contraseña no se mostrará al escribir. " +
                    "Presiona Esc para cancelar.");

                while (true)
                {
                    password = ConsolePasswordReader.Read(
                        "Contraseña: ");

                    string confirmation = ConsolePasswordReader.Read(
                        "Repite la contraseña: ");

                    if (string.IsNullOrWhiteSpace(password))
                    {
                        Console.WriteLine(
                            "La contraseña no puede estar vacía.");

                        continue;
                    }

                    if (password != confirmation)
                    {
                        Console.WriteLine(
                            "Las contraseñas no coinciden. " +
                            "Inténtalo otra vez.");

                        continue;
                    }

                    break;
                }
            }

            await DevelopmentUserSeeder.RunAsync(
                dbContext,
                userManager,
                roleManager,
                account.Email,
                password,
                account.Role);
        }

        Console.WriteLine();

        Console.WriteLine(
            "Proceso de creación de usuarios finalizado.");
    }

    Console.WriteLine(
        "Preparación de datos finalizada.");

    // Terminar sin iniciar el servidor web.
    return;
}

// ============================================================
// 17. Configurar errores y HSTS fuera de desarrollo.
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ============================================================
// 18. Configurar el procesamiento de solicitudes.
// ============================================================

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ============================================================
// 19. Configurar los archivos estáticos.
// ============================================================

// Los recursos de wwwroot son públicos (CSS, JS, iconos y bibliotecas).
// Atenderlos desde el enrutamiento evita validar Identity en cada descarga.
// Los controladores siguen recorriendo autenticación y autorización.
app.MapStaticAssets().ShortCircuit();

// ============================================================
// 20. Configurar la ruta principal de MVC.
// ============================================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// ============================================================
// 21. Endpoint de estado del proceso.
// ============================================================

// Indica que el servidor atiende solicitudes.
// No comprueba SQL Server ni la conexión con el ERP.
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        service = "Portal.Web",
        status = "running"
    });
});

// ============================================================
// 22. Iniciar la aplicación.
// ============================================================

app.MapPortalHealthChecks();

app.Run();
