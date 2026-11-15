using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PortalPedidos.Hosting;

// Opt-in configuration shared by the two executables. Never reads host User Secrets.
internal static class DockerDemo
{
    public static bool Configure(WebApplicationBuilder builder, string connectionName, string database)
    {
        if (Environment.GetEnvironmentVariable("PORTAL_DOCKER_DEMO") != "true")
            return false;

        if (!builder.Environment.IsDevelopment() ||
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
            throw new InvalidOperationException("Docker demo requires a Development container.");

        builder.Configuration.AddJsonFile("/run/secrets/demo.json", optional: false, reloadOnChange: false);
        var connection = new SqlConnectionStringBuilder(builder.Configuration.GetConnectionString(connectionName));
        if (connection.DataSource != "sql,1433" || connection.InitialCatalog != database || connection.IntegratedSecurity)
            throw new InvalidOperationException("Docker demo refused an unexpected database destination.");

        Console.WriteLine($"Docker demo destination verified: sql,1433 / {database}");
        return true;
    }
}
