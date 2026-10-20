using Portal.Domain.Enums;

namespace Portal.Web.Formatting;

public static class IntegrationStatusDisplay
{
    public static string Label(IntegrationStatus status)
    {
        return status switch
        {
            IntegrationStatus.Pending =>
                "Pendiente de envío",

            IntegrationStatus.Processing =>
                "Procesando",

            IntegrationStatus.RetryWaiting =>
                "Reintento programado",

            IntegrationStatus.Unknown =>
                "Verificando resultado",

            IntegrationStatus.Succeeded =>
                "Integrado",

            IntegrationStatus.Rejected =>
                "Rechazado por el ERP",

            IntegrationStatus.DeadLetter =>
                "Requiere revisión",

            _ => "Estado desconocido"
        };
    }

    public static string CssClass(IntegrationStatus status)
    {
        return status switch
        {
            IntegrationStatus.Pending =>
                "bg-secondary",

            IntegrationStatus.Processing =>
                "bg-primary",

            IntegrationStatus.RetryWaiting =>
                "bg-warning text-dark",

            IntegrationStatus.Unknown =>
                "bg-warning text-dark",

            IntegrationStatus.Succeeded =>
                "bg-success",

            IntegrationStatus.Rejected =>
                "bg-danger",

            IntegrationStatus.DeadLetter =>
                "bg-danger",

            _ => "bg-secondary"
        };
    }

    public static bool HasScheduledWork(IntegrationStatus status)
    {
        return status is
            IntegrationStatus.Pending or
            IntegrationStatus.RetryWaiting or
            IntegrationStatus.Unknown;
    }
}