using System.Collections.Generic;
using System.Text;
namespace Portal.Web.Formatting;

public static class CsvDocument
{
    // Exportación para consulta en hojas de cálculo, no para importación de datos.
    // Delimitar y escapar todas las celdas impide introducir columnas adicionales.
    public static string Cell(string? value)
    {
        string text = (value ?? string.Empty).Replace("\0", string.Empty);
        string trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+-@＝＋－＠".Contains(trimmed[0]))
        {
            // Prefijo dentro del campo entrecomillado para Excel.
            text = "\t" + text;
        }
        else if (text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n'))
        {
            text = "'" + text;
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    public static byte[] Build(IEnumerable<IEnumerable<string?>> rows)
    {
        var result = new StringBuilder();
        foreach (var row in rows)
            result.AppendLine(string.Join(";", System.Linq.Enumerable.Select(row, Cell)));
        // BOM UTF-8 facilita abrir acentos en Excel para Windows.
        return Encoding.UTF8.GetBytes("\uFEFF" + result);
    }
}
