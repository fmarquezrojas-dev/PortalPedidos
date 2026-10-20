using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Portal.Application.DTOs.Search;
using Portal.Application.Interfaces.Search;
using Portal.Domain.Enums;
namespace Portal.Infrastructure.Services.Search;

// Intérprete acotado de consultas. No es un modelo generativo y no ejecuta operaciones.
public sealed class LocalSearchAssistant : ISearchAssistant
{
    private static readonly TimeZoneInfo Peru = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Lima");
    private readonly TimeProvider _clock;
    public LocalSearchAssistant(TimeProvider clock) => _clock = clock;

    public SearchIntentDto Interpret(string? question)
    {
        string source = (question ?? string.Empty).Trim();
        if (source.Length is < 2 or > 160)
            return Unknown("Escribe una consulta de 2 a 160 caracteres.");
        string normalized = Normalize(source);
        var areaMatch = Regex.Match(normalized,
            @"^(?:(?:busca|buscar|ver|mostrar|muestra|consultar)\s+)?(?:mis\s+|los\s+)?(?<area>pedidos?|clientes?|productos?|catalogo)\b\s*(?<tail>.*)$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!areaMatch.Success)
            return Unknown("Prueba con «pedidos rechazados», «clientes activos» o «productos cemento».");
        string area = areaMatch.Groups["area"].Value;
        string tail = areaMatch.Groups["tail"].Value.Trim();
        if (area.StartsWith("pedido", StringComparison.OrdinalIgnoreCase))
        {
            tail = OriginalTail(source);
            if (tail.StartsWith("de ", StringComparison.OrdinalIgnoreCase)) tail = tail[3..].Trim();
            OrderStatus? status = null;
            var states = new (string Phrase, OrderStatus Status)[] {
                ("por confirmar", OrderStatus.Queued), ("pendientes", OrderStatus.Queued),
                ("pendiente", OrderStatus.Queued), ("borradores", OrderStatus.Draft), ("borrador", OrderStatus.Draft),
                ("rechazados", OrderStatus.Rejected), ("rechazado", OrderStatus.Rejected),
                ("confirmados", OrderStatus.Confirmed), ("confirmado", OrderStatus.Confirmed),
                ("cancelados", OrderStatus.Cancelled), ("cancelado", OrderStatus.Cancelled) };
            foreach (var entry in states)
            {
                if (tail.Equals(entry.Phrase, StringComparison.OrdinalIgnoreCase) || tail.StartsWith(entry.Phrase + " ", StringComparison.OrdinalIgnoreCase))
                { status = entry.Status; tail = tail[entry.Phrase.Length..].Trim(); break; }
            }
            DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Peru).DateTime);
            DateOnly? from = null, to = null;
            string dateText = Normalize(tail);
            if (dateText is "hoy" or "de hoy") { from = to = today; tail = ""; }
            else if (dateText is "ayer" or "de ayer") { from = to = today.AddDays(-1); tail = ""; }
            else if (dateText is "este mes" or "de este mes") { from = new DateOnly(today.Year, today.Month, 1); to = today; tail = ""; }
            // Un cliente puede escribirse explícitamente: pedidos rechazados cliente Acme.
            if (tail.StartsWith("cliente ", StringComparison.OrdinalIgnoreCase)) tail = tail[8..].Trim();
            else if (tail.StartsWith("del cliente ", StringComparison.OrdinalIgnoreCase)) tail = tail[12..].Trim();
            else if (tail.StartsWith("de ", StringComparison.OrdinalIgnoreCase)) tail = tail[3..].Trim();
            if (tail.Length > 100) return Unknown("Acorta el nombre o número a un máximo de 100 caracteres.");
            return new SearchIntentDto { Area = SearchArea.Orders, Term = EmptyToNull(tail),
                OrderStatus = status, From = from, To = to,
                Message = "Preparé una búsqueda de pedidos. Revisa los filtros antes de abrir los resultados." };
        }
        // Conservar acentos del nombre al extraer el resto de una consulta de catálogo o clientes.
        // La comparación en base de datos sigue su colación configurada.
        string originalTail = OriginalTail(source);
        if (area.StartsWith("cliente", StringComparison.OrdinalIgnoreCase))
        {
            bool? active = null;
            if (tail is "activos" or "activo") { active = true; originalTail = ""; }
            else if (tail is "inactivos" or "inactivo") { active = false; originalTail = ""; }
            if (originalTail.Length > 100) return Unknown("Acorta el nombre del cliente a un máximo de 100 caracteres.");
            return new SearchIntentDto { Area = SearchArea.Customers, Term = EmptyToNull(originalTail),
                CustomerIsActive = active, Message = "Preparé una búsqueda en el directorio de clientes." };
        }
        if (originalTail.Length > 100) return Unknown("Acorta el nombre del producto a un máximo de 100 caracteres.");
        return new SearchIntentDto { Area = SearchArea.Catalog, Term = EmptyToNull(originalTail),
            Message = "Preparé una búsqueda por nombre o código de producto." };
    }
    private static string OriginalTail(string source)
    {
        var match = Regex.Match(source, @"^(?:(?:busca|buscar|ver|mostrar|muestra|consultar)\s+)?(?:mis\s+|los\s+)?(?:pedidos?|clientes?|productos?|cat[aá]logo)\b\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups[1].Value.Trim() : source;
    }
    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
    private static SearchIntentDto Unknown(string message) => new() { Area = SearchArea.Unrecognized, Message = message };
    private static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(c);
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
