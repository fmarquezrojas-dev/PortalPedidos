using System;
using System.Globalization;

namespace Portal.Web.Formatting;

public static class PortalDateTime
{
    private static readonly CultureInfo PeruCulture =
        CultureInfo.GetCultureInfo("es-PE");

    private static readonly TimeZoneInfo PeruTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "SA Pacific Standard Time"
                : "America/Lima");

    public static DateTime AsUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public static string Format(DateTime? utcValue)
    {
        if (!utcValue.HasValue)
        {
            return "Sin registro";
        }

        DateTime peruTime = TimeZoneInfo.ConvertTimeFromUtc(
            AsUtc(utcValue.Value),
            PeruTimeZone);

        return peruTime.ToString(
            "dd/MM/yyyy HH:mm:ss",
            PeruCulture);
    }

    public static string Age(DateTime? utcValue, DateTime nowUtc)
    {
        if (!utcValue.HasValue)
        {
            return "Sin lectura";
        }

        TimeSpan elapsed = AsUtc(nowUtc) - AsUtc(utcValue.Value);

        if (elapsed.TotalSeconds < 60)
        {
            return "Hace menos de un minuto";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"Hace {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed.TotalHours < 24)
        {
            return $"Hace {(int)elapsed.TotalHours} h";
        }

        return $"Hace {(int)elapsed.TotalDays} días";
    }
}