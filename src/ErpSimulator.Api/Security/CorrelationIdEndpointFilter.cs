using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace ErpSimulator.Api.Security;
public sealed class CorrelationIdEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var supplied = http.Request.Headers["X-Correlation-ID"];
        Guid id = supplied.Count == 1 && Guid.TryParse(supplied[0], out var parsed) && parsed != Guid.Empty
            ? parsed : Guid.NewGuid();
        http.Items["CorrelationId"] = id;
        http.Response.Headers["X-Correlation-ID"] = id.ToString("D");
        var logger = http.RequestServices.GetRequiredService<ILogger<CorrelationIdEndpointFilter>>();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            logger.LogInformation("Solicitud ERP {Method} {Path} CorrelationId {CorrelationId}.",
                http.Request.Method, http.Request.Path, id);
            return await next(context);
        }
    }
}
