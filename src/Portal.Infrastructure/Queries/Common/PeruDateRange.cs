using System;
namespace Portal.Infrastructure.Queries.Common;

internal static class PeruDateRange
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Lima");

    public static (DateTime? FromUtc, DateTime? UntilUtc) Bounds(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new ArgumentException("La fecha inicial no puede ser posterior a la fecha final.");
        if (from?.Year == 9999 || to?.Year == 9999)
            throw new ArgumentException("Selecciona fechas anteriores al año 9999.");
        DateTime Convert(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
        return (from.HasValue ? Convert(from.Value) : null,
            to.HasValue ? Convert(to.Value.AddDays(1)) : null);
    }
    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
}
