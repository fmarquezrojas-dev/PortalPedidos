using System;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence;

public class PortalDbContext : IdentityDbContext<ApplicationUser>
{
    public PortalDbContext(
        DbContextOptions<PortalDbContext> options)
        : base(options)
    {
    }

    // Clientes y catálogo.
    public DbSet<Customer> Customers
        => Set<Customer>();

    public DbSet<Product> Products
        => Set<Product>();

    public DbSet<InventorySnapshot> InventorySnapshots
        => Set<InventorySnapshot>();

    public DbSet<CatalogSyncState> CatalogSyncStates
        => Set<CatalogSyncState>();

    // Pedidos.
    public DbSet<SalesOrder> SalesOrders
        => Set<SalesOrder>();

    public DbSet<SalesOrderLine> SalesOrderLines
        => Set<SalesOrderLine>();

    public DbSet<OrderStatusHistory> OrderStatusHistories
        => Set<OrderStatusHistory>();

    // Integración con el ERP.
    public DbSet<OutboxMessage> OutboxMessages
        => Set<OutboxMessage>();

    public DbSet<IntegrationAttempt> IntegrationAttempts
        => Set<IntegrationAttempt>();

    // Auditoría.
    public DbSet<AuditEvent> AuditEvents
        => Set<AuditEvent>();

    // Equipos comerciales.
    public DbSet<SalesTeam> SalesTeams
        => Set<SalesTeam>();

    public DbSet<SalesTeamMember> SalesTeamMembers
        => Set<SalesTeamMember>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder
            .Properties<decimal>()
            .HavePrecision(19, 4);

        configurationBuilder
            .Properties<DateTime>()
            .HaveColumnType("datetime2(3)");
    }

    protected override void OnModelCreating(
        ModelBuilder builder)
    {
        // Configuración de las tablas de ASP.NET Core Identity.
        base.OnModelCreating(builder);

        // Numeración pública de pedidos.
        builder.HasSequence<long>("OrderNumbers", "dbo")
            .StartsAt(1001)
            .IncrementsBy(1);

        // Aplicar las configuraciones de entidades de Infrastructure.
        builder.ApplyConfigurationsFromAssembly(
            typeof(PortalDbContext).Assembly);
    }

    public DbSet<CustomerAssignment> CustomerAssignments
    => Set<CustomerAssignment>();

}