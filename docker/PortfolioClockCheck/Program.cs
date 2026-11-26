using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Customers;
using Portal.Infrastructure.Services.Customers;

var config = new ConfigurationBuilder().AddJsonFile("/run/secrets/demo.json").Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("PortalDb"));
if (Environment.GetEnvironmentVariable("PORTAL_DOCKER_DEMO") != "true" ||
    connection.DataSource != "sql,1433" || connection.InitialCatalog != "PortalPedidosDockerDemo" ||
    !config.GetValue<bool>("CustomerPortfolio:Restricted"))
    throw new InvalidOperationException("Se requiere la copia Docker restringida de pruebas.");

var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection.ConnectionString).Options;
await using var db = new PortalDbContext(options);
var customer = await db.Customers.SingleAsync(c => c.ExternalCode == "TC0150");
var actor = new Seller((await db.Users.SingleAsync(u => u.Email == "suplente@portal.test")).Id);
var coverage = await (from c in db.CustomerCoverages join a in db.CustomerAssignments on c.AssignmentId equals a.Id
    where a.CustomerId == customer.Id && c.RevokedAtUtc == null select c).SingleAsync();
var start = DateTime.SpecifyKind(coverage.StartsAtUtc, DateTimeKind.Utc);
var end = DateTime.SpecifyKind(coverage.EndsAtUtc, DateTimeKind.Utc);
if (start.Hour != 5 || end.Hour != 5 || end.DayOfWeek != DayOfWeek.Saturday)
    throw new Exception("El intervalo debe terminar tras el viernes completo en Lima.");
foreach (var (instant, expected) in new[] { (start.AddMilliseconds(-1), false), (start, true),
    (end.AddMilliseconds(-1), true), (end, false) })
{
    var clock = new FixedClock(instant);
    var portfolio = new CommercialPortfolioQuery(db, actor, clock);
    var eligibility = new CustomerEligibilityService(portfolio, actor, config, clock);
    var decision = await eligibility.EvaluateAsync(customer, default);
    var listed = await portfolio.FindAsync(customer.Id, false, default);
    var selectable = await db.Customers.Where(eligibility.GetEligibleCustomerPredicate()).AnyAsync(c => c.Id == customer.Id);
    if (expected != decision.IsEligible || expected != (listed != null) || expected != selectable)
        throw new Exception($"Cartera, selector o permiso incorrecto a las {instant:O}");
    Console.WriteLine($"{instant:O} {(expected ? "permitido" : "denegado")}");
}

sealed class FixedClock(DateTime utc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utc);
}
sealed record Seller(string UserId) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public bool IsAdministrator => false;
    public bool CanViewOrders => true;
    public bool CanViewAllOrders => false;
    public bool CanViewTeamOrders => false;
    public bool CanCreateOrders => true;
}
