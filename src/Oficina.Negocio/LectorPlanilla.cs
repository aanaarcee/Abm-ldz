using System.Text;
using ExcelDataReader;

namespace Oficina.Negocio;

// lee una planilla (CSV o Excel) y devuelve sus filas con el número que tienen en el archivo (la 1 es el encabezado).
// las filas completamente vacías se saltean. Cada celda llega como la trae el archivo: texto, número o fecha.
internal static class LectorPlanilla
{
    static LectorPlanilla() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static List<(int Nro, object[] Celdas)> Leer(string ruta)
    {
        if (!File.Exists(ruta)) throw new ExcepcionNegocio("No se encuentra el archivo.");
        string ext = Path.GetExtension(ruta).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".xlsx" or ".xlsm" or ".xls" => LeerExcel(ruta),
                ".csv" or ".txt" => LeerCsv(ruta),
                _ => throw new ExcepcionNegocio("Formato no soportado. Use un archivo .csv o .xlsx (Excel).")
            };
        }
        catch (IOException ex)
        {
            throw new ExcepcionNegocio($"No se pudo abrir el archivo (¿está abierto en Excel?): {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not ExcepcionNegocio && ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            throw new ExcepcionNegocio("El archivo no se pudo leer: no parece una planilla válida.", ex);
        }
    }

    private static List<(int, object[])> LeerExcel(string ruta)
    {
        var filas = new List<(int, object[])>();
        using var s = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = ExcelReaderFactory.CreateReader(s);
        int nro = 0;
        while (r.Read())   // solo la primera hoja
        {
            nro++;
            var celdas = new object[r.FieldCount];
            for (int i = 0; i < celdas.Length; i++) celdas[i] = r.GetValue(i);
            if (celdas.Any(c => c != null && !string.IsNullOrWhiteSpace(Convert.ToString(c)))) filas.Add((nro, celdas));
        }
        return filas;
    }

    private static List<(int, object[])> LeerCsv(string ruta)
    {
        byte[] bytes;
        using (var s = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var m = new MemoryStream()) { s.CopyTo(m); bytes = m.ToArray(); }

        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { texto = Encoding.GetEncoding(1252).GetString(bytes); }   // CSV guardado desde Excel en Windows
        texto = texto.TrimStart((char)0xFEFF);   // marca de orden de bytes (BOM) que agrega Excel

        char sep = Separador(texto);
        var filas = new List<(int, object[])>();
        var celdas = new List<object>();
        var celda = new StringBuilder();
        bool comillas = false;
        int nro = 1;

        void CerrarCelda() { celdas.Add(celda.ToString()); celda.Clear(); }
        void CerrarFila()
        {
            CerrarCelda();
            if (celdas.Any(c => !string.IsNullOrWhiteSpace((string)c))) filas.Add((nro, celdas.ToArray()));
            celdas.Clear();
            nro++;
        }

        for (int i = 0; i < texto.Length; i++)
        {
            char c = texto[i];
            if (comillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { celda.Append('"'); i++; }
                else if (c == '"') comillas = false;
                else celda.Append(c);
            }
            else if (c == '"' && celda.Length == 0) comillas = true;
            else if (c == sep) CerrarCelda();
            else if (c == '\r') { if (i + 1 < texto.Length && texto[i + 1] == '\n') i++; CerrarFila(); }
            else if (c == '\n') CerrarFila();
            else celda.Append(c);
        }
        if (celda.Length > 0 || celdas.Count > 0) CerrarFila();
        return filas;
    }

    // el separador es el que más aparece en el encabezado (Excel en español usa ;)
    private static char Separador(string texto)
    {
        int fin = texto.IndexOfAny(['\r', '\n']);
        string cabecera = fin < 0 ? texto : texto[..fin];
        return new[] { ';', ',', '\t' }.OrderByDescending(c => cabecera.Count(x => x == c)).First();
    }
}
