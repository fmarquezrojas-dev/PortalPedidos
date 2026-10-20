using Portal.Domain.Enums;

namespace Portal.Web.Formatting;

public static class OrderStatusDisplay
{
    public static string Label(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Draft => "Borrador",
            OrderStatus.Queued => "Pendiente de integración",
            OrderStatus.Confirmed => "Confirmado",
            OrderStatus.Rejected => "Rechazado",
            OrderStatus.Cancelled => "Cancelado",
            _ => "Desconocido"
        };
    }

    public static string CssClass(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Draft => "bg-secondary",
            OrderStatus.Queued => "bg-warning text-dark",
            OrderStatus.Confirmed => "bg-success",
            OrderStatus.Rejected => "bg-danger",
            OrderStatus.Cancelled => "bg-secondary",
            _ => "bg-secondary"
        };
    }
}