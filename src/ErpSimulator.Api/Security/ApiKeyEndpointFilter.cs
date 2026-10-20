using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ErpSimulator.Api.Security;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly byte[] _expectedKeyHash;

    public ApiKeyEndpointFilter(string expectedApiKey)
    {
        _expectedKeyHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expectedApiKey));
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        httpContext.Response.Headers["Cache-Control"] = "no-store";

        if (!httpContext.Request.Headers.TryGetValue(
                "X-Api-Key", out var values) ||
            values.Count != 1)
        {
            return Unauthorized();
        }

        string suppliedKey = values[0] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(suppliedKey) ||
            suppliedKey.Length > 512)
        {
            return Unauthorized();
        }

        byte[] suppliedKeyHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedKey));

        bool matches = CryptographicOperations.FixedTimeEquals(
            _expectedKeyHash,
            suppliedKeyHash);

        if (!matches)
        {
            return Unauthorized();
        }

        return await next(context);
    }

    private static IResult Unauthorized()
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Acceso no autorizado",
            detail: "Se requiere una clave de acceso válida.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "INVALID_API_KEY"
            });
    }
}