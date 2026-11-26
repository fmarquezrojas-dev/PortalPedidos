using Portal.Application.DTOs.Integration;
using Portal.Domain.Enums;

namespace Portal.Web.Formatting;

public static class OperationalAttentionDisplay
{
    public static string Label(AttentionCategory category) => category switch
    {
        AttentionCategory.Review => "Requiere revisión",
        AttentionCategory.Uncertain => "Resultado ERP por confirmar",
        AttentionCategory.ExpiredLease => "Procesamiento con plazo vencido",
        AttentionCategory.Delayed => "Pendiente con demora",
        AttentionCategory.Monitoring => "Seguimiento normal",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public static string Guidance(OperationalItem item, DateTime now) => item.Status switch
    {
        IntegrationStatus.DeadLetter => item.LastErrorCode switch
        {
            "ERP_LOOKUP_EXHAUSTED" or "ERP_RESULT_UNRESOLVED" =>
                "Se agotaron los intentos de verificar el resultado. Investiga la referencia original.",
            "ERP_UNAUTHORIZED" or "ERP_FORBIDDEN" => "El ERP denegó el acceso. Revisa las credenciales y permisos.",
            "ERP_ENDPOINT_NOT_FOUND" => "No se encontró la ruta del ERP. Revisa la configuración del servicio.",
            _ => "El procesamiento automático se detuvo. Revisa el código y los intentos en el detalle."
        },
        IntegrationStatus.Unknown => "El ERP podría haber aceptado el pedido. Espera la consulta de la referencia original.",
        IntegrationStatus.Processing when item.Category == AttentionCategory.ExpiredLease =>
            "Venció el plazo de procesamiento. El portal consultará el resultado en el ERP.",
        IntegrationStatus.Processing => "El pedido está en proceso. Espera hasta el fin del plazo indicado.",
        IntegrationStatus.RetryWaiting when item.NextAttemptAtUtc > now =>
            "Reintento programado para más adelante. Espera la hora indicada.",
        IntegrationStatus.RetryWaiting => "Ya llegó la hora del reintento. Actualiza la bandeja para consultar el resultado.",
        _ => "El pedido está en cola para su primer envío."
    };
}
