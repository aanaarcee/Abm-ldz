using System.Globalization;
using System.Text;
using Oficina.Datos;
using Oficina.Entidades;

namespace Oficina.Negocio;

// funcion para traer datos de un excel 

public class FilaImportacion
{
    public int Numero { get; internal set; }        // número de fila en el archivo (la 1 es el encabezado)
    public string Resumen { get; internal set; }
    public string Error { get; internal set; }
    public string Aviso { get; internal set; }
    public bool Valida => Error == null;
    public string Estado => Valida ? "Lista" : "Error";
    public string Mensaje => Error ?? Aviso;

    internal string[] Original;
    internal Acta Acta; internal Persona Persona; internal Movimiento Movimiento; internal Reclamo Reclamo; internal Lote Lote;
}

public class VistaPreviaImportacion
{
    public Sector Sector { get; internal set; }
    public string Archivo { get; internal set; }
    public List<FilaImportacion> Filas { get; internal set; } = [];
    public List<string> ColumnasReconocidas { get; internal set; } = [];
    public List<string> ColumnasIgnoradas { get; internal set; } = [];
    public int Listas => Filas.Count(f => f.Valida);
    public int ConError => Filas.Count(f => !f.Valida);
    public int ActasNuevas { get; internal set; }
    internal string[] Encabezados;
}

public class ResultadoImportacion
{
    public int Importadas { get; internal set; }
    public int ActasNuevas { get; internal set; }
    public List<FilaImportacion> Errores { get; internal set; } = [];
}

public class ServicioImportacion : ServicioBase
{
    public const int MaximoFilas = 5000;
    private readonly ServicioRegistros _registros;

    public ServicioImportacion(Sesion sesion, IDatosRegistros datos) : base(sesion, datos)
        => _registros = new ServicioRegistros(sesion, datos);

    // columnas que se reconocen (sin distinguir mayúsculas, acentos ni signos)

    private record Campo(string Clave, bool Obligatorio, params string[] Nombres);

    private static Campo[] CamposDe(TipoSector tipo) => tipo switch
    {
        TipoSector.Actas =>
        [
            new("fecha", true, "Fecha", "Fecha del movimiento"),
            new("causa", true, "N° de causa", "Nro causa", "Causa", "Número de causa"),
            new("reparticion", true, "Repartición"),
            new("anio", false, "Año", "Anio"),
            new("nroacta", true, "N° de acta", "Nro acta", "Acta", "Número de acta"),
            new("fechaacta", false, "Fecha del acta", "Fecha de acta"),
            new("persona", false, "Persona", "Apellido y nombre", "Titular", "Infractor"),
            new("apellido", false, "Apellido", "Razón social"),
            new("nombre", false, "Nombre"),
            new("documento", false, "Documento", "DNI", "N° de documento", "CUIT", "DNI/CUIT"),
            new("tipodoc", false, "Tipo de documento", "Tipo documento"),
            new("dominio", false, "Dominio", "Patente"),
            new("juzgado", false, "Juzgado"),
            new("movimiento", true, "Movimiento", "Detalle"),
            new("remito", false, "Remito"),
            new("suma", false, "Suma reclamada", "Monto", "Monto de sentencia"),
            new("gastos", false, "Gastos administrativos", "Gastos"),
            new("obs", false, "Observaciones"),
            new("obsacta", false, "Observaciones del acta")
        ],
        TipoSector.Reclamos =>
        [
            new("caso", true, "N° de caso", "Nro caso", "Caso"),
            new("fecha", true, "Fecha de ingreso", "Ingreso", "Fecha"),
            new("origen", false, "Origen"),
            new("categoria", false, "Categoría", "Tipo de reclamo"),
            new("estado", false, "Estado"),
            new("direccion", true, "Dirección", "Ubicación"),
            new("entrecalles", false, "Entrecalles"),
            new("delegacion", false, "Delegación"),
            new("vehiculo", false, "Vehículo"),
            new("denunciante", false, "Denunciante"),
            new("verificado", false, "Verificado"),
            new("fechaverif", false, "Primera verificación", "Fecha de verificación"),
            new("inspector", false, "Inspector", "Verificado por"),
            new("actainfr", false, "Acta de infracción", "N° de acta de infracción"),
            new("fechanotif", false, "Cédula de notificación", "Fecha de notificación"),
            new("obs", false, "Observaciones")
        ],
        TipoSector.Lotes =>
        [
            new("lote", true, "N° de lote", "Nro lote", "Lote"),
            new("fecha", true, "Fecha"),
            new("transito", false, "Tránsito"),
            new("competencia", false, "Competencia"),
            new("prescriptas", false, "Prescriptas"),
            new("obs", false, "Observaciones")
        ],
        _ => throw new ExcepcionNegocio("Este sector no recibe datos.")
    };

    // columnas que trae el exportador y que no se cargan (se calculan o son de la base): no se avisan como ignoradas
    // (el N° de apremio no se importa: lo carga Apremio desde la ficha del acta)
    private static readonly string[] ColumnasQueSeCalculan = ["ultimocambio", "total", "sector", "ndeapremio"];

    // pantilla

    public void GuardarPlantilla(int idSector, string ruta)
    {
        var sector = Sesion.Sector(idSector);
        Sesion.Exigir(idSector, Accion.Cargar);
        var encabezado = string.Join(";", CamposDe(sector.Tipo).Select(c => Celda(c.Nombres[0])));
        Escribir(ruta, encabezado + Environment.NewLine);
    }

    public static string NombrePlantilla(Sector s) => $"Plantilla {s.Nombre}.csv";

    //  leer y validar

    public VistaPreviaImportacion Analizar(int idSector, string ruta)
    {
        var sector = Sesion.Sector(idSector);
        Sesion.Exigir(idSector, Accion.Cargar);
        var campos = CamposDe(sector.Tipo);

        var tabla = LectorPlanilla.Leer(ruta);
        if (tabla.Count == 0) throw new ExcepcionNegocio("El archivo está vacío.");
        if (tabla.Count - 1 > MaximoFilas) throw new ExcepcionNegocio($"El archivo tiene demasiadas filas ({tabla.Count - 1}). El máximo es {MaximoFilas} por vez: divídalo en partes.");
        if (tabla.Count == 1) throw new ExcepcionNegocio("El archivo tiene el encabezado pero ninguna fila de datos.");

        // qué columna del archivo es cada campo
        var encabezados = tabla[0].Celdas.Select(c => Texto(c) ?? "").ToArray();
        var columna = new Dictionary<string, int>();
        var vp = new VistaPreviaImportacion { Sector = sector, Archivo = ruta, Encabezados = encabezados };
        for (int i = 0; i < encabezados.Length; i++)
        {
            if (encabezados[i] == "") continue;
            string n = Norm(encabezados[i]);
            var campo = campos.FirstOrDefault(c => c.Nombres.Any(x => Norm(x) == n));
            if (campo != null && !columna.ContainsKey(campo.Clave)) { columna[campo.Clave] = i; vp.ColumnasReconocidas.Add(encabezados[i]); }
            else if (!ColumnasQueSeCalculan.Contains(n)) vp.ColumnasIgnoradas.Add(encabezados[i]);
        }
        var faltan = campos.Where(c => c.Obligatorio && !columna.ContainsKey(c.Clave)).Select(c => $"«{c.Nombres[0]}»").ToList();
        if (faltan.Count > 0)
            throw new ExcepcionNegocio($"Al archivo le faltan columnas obligatorias: {string.Join(", ", faltan)}.\nUse el botón «Plantilla» para ver las columnas.");

        var ctx = new Contexto(this, sector, columna);
        foreach (var (nro, celdas) in tabla.Skip(1))
        {
            var fila = new FilaImportacion { Numero = nro, Original = celdas.Select(c => Texto(c) ?? "").ToArray() };
            try
            {
                switch (sector.Tipo)
                {
                    case TipoSector.Actas: ParsearActa(ctx, celdas, fila); break;
                    case TipoSector.Reclamos: ParsearReclamo(ctx, celdas, fila); break;
                    case TipoSector.Lotes: ParsearLote(ctx, celdas, fila); break;
                }
            }
            catch (ExcepcionNegocio ex) { fila.Error = ex.Message; }
            fila.Resumen ??= string.Join(" · ", fila.Original.Where(x => x != "").Take(4));
            vp.Filas.Add(fila);
        }
        vp.ActasNuevas = ctx.ActasNuevas;
        return vp;
    }

    // datos que se piden una sola vez por archivo
    private class Contexto
    {
        public readonly Sector Sector;
        public readonly Dictionary<string, int> Col;
        public readonly List<Reparticion> Reparticiones;
        public readonly List<Juzgado> Juzgados;
        public readonly HashSet<string> Vistos = new();                  // claves ya usadas en el archivo
        public readonly Dictionary<string, Acta> Actas = new();           // actas ya consultadas (null = no existe)
        public readonly HashSet<string> Existentes = new();               // casos o lotes que ya hay en la base
        public int ActasNuevas;

        public Contexto(ServicioImportacion s, Sector sector, Dictionary<string, int> col)
        {
            Sector = sector; Col = col;
            if (sector.Tipo == TipoSector.Actas) { Reparticiones = s.Datos.ListarReparticiones(); Juzgados = s.Datos.ListarJuzgados(); }
            else if (sector.Tipo == TipoSector.Reclamos) foreach (var r in s.Datos.ListarReclamos()) Existentes.Add(Norm(r.NroCaso));
            else foreach (var l in s.Datos.ListarLotes()) Existentes.Add(Norm(l.NroLote));
        }
    }

    private static object Valor(Contexto c, object[] celdas, string clave)
        => c.Col.TryGetValue(clave, out int i) && i < celdas.Length ? celdas[i] : null;

    // actas y movimiento

    private void ParsearActa(Contexto c, object[] x, FilaImportacion fila)
    {
        object V(string k) => Valor(c, x, k);
        var fecha = Fecha(V("fecha"), "Fecha") ?? throw new ExcepcionNegocio("Falta la fecha.");
        var fechaActa = Fecha(V("fechaacta"), "Fecha del acta") ?? fecha;

        var rep = BuscarReparticion(c.Reparticiones, Texto(V("reparticion")));
        int anio = Entero(V("anio"), "Año") ?? fechaActa.Year;
        Juzgado juzgado = null;
        if (Texto(V("juzgado")) is { } tj) juzgado = BuscarJuzgado(c.Juzgados, tj);

        var acta = new Acta
        {
            NroCausa = Texto(V("causa")), IdReparticion = rep.IdReparticion, Reparticion = rep.Nombre, Anio = anio,
            NroActa = Texto(V("nroacta")), FechaActa = fechaActa.Date, IdJuzgado = juzgado?.IdJuzgado, Dominio = Texto(V("dominio")),
            SumaReclamada = Monto(V("suma"), "Suma reclamada"), GastosAdministrativos = Monto(V("gastos"), "Gastos administrativos"),
            Observaciones = Texto(V("obsacta"))
        };
        var persona = LeerPersona(c, x);
        var mov = new Movimiento { Fecha = fecha.Date, Detalle = Texto(V("movimiento")), Remito = Texto(V("remito")), Observaciones = Texto(V("obs")) };

        ServicioRegistros.ValidarClave(acta);
        ServicioRegistros.ValidarMovimiento(mov);

        string claveActa = $"{acta.NroCausa.ToLowerInvariant()}|{acta.IdReparticion}|{acta.Anio}";
        if (!c.Actas.TryGetValue(claveActa, out var existente))
            c.Actas[claveActa] = existente = Datos.BuscarActa(acta.NroCausa, acta.IdReparticion, acta.Anio);

        if (existente == null)
        {
            ServicioRegistros.ValidarActa(acta);
            ServicioRegistros.ValidarPersona(persona);
            if (c.Vistos.Add("acta|" + claveActa)) { c.ActasNuevas++; fila.Aviso = "Acta nueva"; }
            else fila.Aviso = "Otro movimiento de un acta nueva del archivo";
        }
        else
        {
            if (existente.Eliminado) throw new ExcepcionNegocio("Esa acta está dada de baja.");
            if (existente.Archivada) throw new ExcepcionNegocio("Esa acta está archivada en un lote: está terminada y no admite movimientos.");
            fila.Aviso = "El acta ya existe: se agrega solo el movimiento";
            // que no se duplique si el mismo archivo se carga dos veces
            if (Datos.ListarMovimientosDeActa(existente.IdActa).Any(m => m.IdSector == c.Sector.IdSector && m.Fecha.Date == mov.Fecha.Date
                    && string.Equals(m.Detalle?.Trim(), mov.Detalle, StringComparison.CurrentCultureIgnoreCase) && (m.Remito ?? "") == (mov.Remito ?? "")))
                throw new ExcepcionNegocio("Ya está cargado ese movimiento (misma fecha, movimiento y remito).");
        }
        if (!c.Vistos.Add($"mov|{claveActa}|{mov.Fecha:yyyyMMdd}|{Norm(mov.Detalle)}|{Norm(mov.Remito)}"))
            throw new ExcepcionNegocio("Está repetido en el archivo (misma causa, fecha, movimiento y remito).");

        fila.Acta = acta; fila.Persona = persona; fila.Movimiento = mov;
        fila.Resumen = $"{mov.Fecha:dd/MM/yyyy} · Causa {acta.NroCausa} · {rep.Codigo} · {acta.Anio} · Acta {acta.NroActa} · {mov.Detalle}";
    }

    private Persona LeerPersona(Contexto c, object[] x)
    {
        object V(string k) => Valor(c, x, k);
        string apellido, nombre = null;
        if (Texto(V("persona")) is { } completo)
        {
            int i = completo.IndexOf(',');
            if (i >= 0) { apellido = completo[..i].Trim(); nombre = completo[(i + 1)..].Trim(); }
            else apellido = completo;
        }
        else { apellido = Texto(V("apellido")); nombre = Texto(V("nombre")); }
        if (string.IsNullOrWhiteSpace(apellido)) return null;

        string doc = Texto(V("documento"));
        if (doc != null) doc = new string(doc.Where(ch => ch is not ('.' or '-' or ' ')).ToArray());
        string tipo = Texto(V("tipodoc"))?.ToUpperInvariant() ?? (doc?.Length == 11 ? "CUIT" : "DNI");
        if (tipo is not ("DNI" or "CUIT")) throw new ExcepcionNegocio($"El tipo de documento «{tipo}» no es válido (DNI o CUIT).");
        return new Persona { Apellido = apellido, Nombre = nombre, TipoDocumento = tipo, NroDocumento = doc };
    }

    private static Reparticion BuscarReparticion(List<Reparticion> lista, string t)
    {
        if (t == null) throw new ExcepcionNegocio("Falta la repartición.");
        // el exportador escribe "62 · Fotomultas": se acepta el número, el nombre o el texto completo
        string inicio = new(t.TakeWhile(char.IsDigit).ToArray());
        string n = Norm(t);
        var r = lista.FirstOrDefault(x => Norm(x.ToString()) == n)
                ?? lista.FirstOrDefault(x => Norm(x.Nombre) == n)
                ?? (inicio != "" && (t.Length == inicio.Length || t[inicio.Length..].TrimStart().StartsWith('·'))
                    ? lista.FirstOrDefault(x => x.Codigo.ToString() == inicio) : null);
        return r ?? throw new ExcepcionNegocio($"La repartición «{t}» no está en la lista (agréguela en Configuración → Listas).");
    }

    private static Juzgado BuscarJuzgado(List<Juzgado> lista, string t)
    {
        string n = Norm(t);
        var j = lista.FirstOrDefault(x => Norm(x.Nombre) == n)
                ?? (n.All(char.IsDigit) ? lista.FirstOrDefault(x => Norm(x.Nombre).EndsWith(n)) : null);
        return j ?? throw new ExcepcionNegocio($"El juzgado «{t}» no está en la lista.");
    }

    // reclamos

    private void ParsearReclamo(Contexto c, object[] x, FilaImportacion fila)
    {
        object V(string k) => Valor(c, x, k);
        var r = new Reclamo
        {
            NroCaso = Texto(V("caso")), Fecha = Fecha(V("fecha"), "Fecha de ingreso") ?? throw new ExcepcionNegocio("Falta la fecha de ingreso."),
            Origen = Origen(Texto(V("origen"))), Categoria = Categoria(Texto(V("categoria"))), Estado = Estado(Texto(V("estado"))),
            Ubicacion = Texto(V("direccion")), Entrecalles = Texto(V("entrecalles")), Delegacion = Texto(V("delegacion")),
            Vehiculo = Texto(V("vehiculo")), Denunciante = Texto(V("denunciante")),
            FechaVerificacion = Fecha(V("fechaverif"), "Primera verificación"), VerificadoPor = Texto(V("inspector")),
            NroActaInfraccion = Texto(V("actainfr")), FechaNotificacion = Fecha(V("fechanotif"), "Cédula de notificación"),
            Observaciones = Texto(V("obs"))
        };
        // sin columna "Verificado" se entiende que está verificado si tiene fecha de verificación
        r.Verificado = c.Col.ContainsKey("verificado") ? Booleano(V("verificado"), "Verificado") : r.FechaVerificacion != null;
        ServicioRegistros.ValidarReclamo(r);

        string clave = Norm(r.NroCaso);
        if (c.Existentes.Contains(clave)) throw new ExcepcionNegocio("Ya existe un reclamo con ese N° de caso.");
        if (!c.Vistos.Add("caso|" + clave)) throw new ExcepcionNegocio("El N° de caso está repetido en el archivo.");
        fila.Reclamo = r;
        fila.Resumen = $"{r.Fecha:dd/MM/yyyy} · Caso {r.NroCaso} · {r.Ubicacion} · {r.TextoEstado}";
    }

    private static OrigenReclamo? Origen(string t)
    {
        if (t == null) return null;
        string n = Norm(t);
        if (n is "1" or "transito") return OrigenReclamo.Transito;
        if (n is "2" || n.StartsWith("gestion")) return OrigenReclamo.GestionDeReclamos;
        throw new ExcepcionNegocio($"El origen «{t}» no es válido (Tránsito o Gestión de reclamos).");
    }

    private static CategoriaReclamo? Categoria(string t)
    {
        if (t == null) return null;
        string n = Norm(t);
        if (n is "1" or "transito") return CategoriaReclamo.Transito;
        if (n is "2" or "demanda") return CategoriaReclamo.Demanda;
        if (n is "3" || n.StartsWith("municipal")) return CategoriaReclamo.Municipales;
        throw new ExcepcionNegocio($"La categoría «{t}» no es válida (Tránsito, Demanda o Municipales).");
    }

    private static EstadoReclamo Estado(string t)
    {
        if (t == null) return EstadoReclamo.EnAbordaje;
        string n = Norm(t);
        if (n is "1" or "enabordaje" or "abordaje") return EstadoReclamo.EnAbordaje;
        if (n is "2" or "verificando") return EstadoReclamo.Verificando;
        if (n is "3" or "solucionado") return EstadoReclamo.Solucionado;
        throw new ExcepcionNegocio($"El estado «{t}» no es válido (En abordaje, Verificando o Solucionado).");
    }

    // lotes

    private void ParsearLote(Contexto c, object[] x, FilaImportacion fila)
    {
        object V(string k) => Valor(c, x, k);
        var l = new Lote
        {
            NroLote = Texto(V("lote")), Fecha = Fecha(V("fecha"), "Fecha") ?? throw new ExcepcionNegocio("Falta la fecha."),
            CantTransito = Entero(V("transito"), "Tránsito") ?? 0, CantCompetencia = Entero(V("competencia"), "Competencia") ?? 0,
            CantPrescriptas = Entero(V("prescriptas"), "Prescriptas") ?? 0, Observaciones = Texto(V("obs"))
        };
        ServicioRegistros.ValidarLote(l);

        string clave = Norm(l.NroLote);
        if (c.Existentes.Contains(clave)) throw new ExcepcionNegocio("Ya existe un lote con ese número.");
        if (!c.Vistos.Add("lote|" + clave)) throw new ExcepcionNegocio("El N° de lote está repetido en el archivo.");
        fila.Lote = l;
        fila.Resumen = $"{l.Fecha:dd/MM/yyyy} · Lote {l.NroLote} · {l.Total} actas";
    }

    // guardar

    public ResultadoImportacion Importar(VistaPreviaImportacion vp, Action<int, int> progreso = null)
    {
        int idSector = vp.Sector.IdSector;
        Sesion.Exigir(idSector, Accion.Cargar);
        var res = new ResultadoImportacion();
        res.Errores.AddRange(vp.Filas.Where(f => !f.Valida));

        var listas = vp.Filas.Where(f => f.Valida).ToList();
        int hechas = 0;
        foreach (var f in listas)
        {
            try
            {
                switch (vp.Sector.Tipo)
                {
                    case TipoSector.Actas:
                        bool nueva = Datos.BuscarActa(f.Acta.NroCausa, f.Acta.IdReparticion, f.Acta.Anio) == null;
                        _registros.CargarMovimiento(idSector, f.Acta, f.Persona, f.Movimiento);
                        if (nueva) res.ActasNuevas++;
                        break;
                    case TipoSector.Reclamos: _registros.CargarReclamo(idSector, f.Reclamo); break;
                    case TipoSector.Lotes: _registros.CargarLote(idSector, f.Lote); break;
                }
                res.Importadas++;
            }
            catch (ExcepcionNegocio ex) { f.Error = ex.Message; res.Errores.Add(f); }
            progreso?.Invoke(++hechas, listas.Count);
        }
        res.Errores.Sort((a, b) => a.Numero.CompareTo(b.Numero));

        Registrar("Importacion", idSector, idSector, "Importación",
            $"{res.Importadas} filas · {Path.GetFileName(vp.Archivo)}" + (res.Errores.Count > 0 ? $" · {res.Errores.Count} con error" : ""));
        return res;
    }

    // informe de errores 

    public void GuardarInforme(VistaPreviaImportacion vp, IEnumerable<FilaImportacion> errores, string ruta)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", new[] { "Fila", "Error" }.Concat(vp.Encabezados).Select(Celda)));
        foreach (var f in errores.OrderBy(e => e.Numero))
            sb.AppendLine(string.Join(";", new[] { f.Numero.ToString(), f.Error }.Concat(f.Original).Select(Celda)));
        Escribir(ruta, sb.ToString());
    }

    private static void Escribir(string ruta, string texto)
    {
        try { File.WriteAllText(ruta, texto, new UTF8Encoding(true)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionNegocio($"No se pudo guardar el archivo: {ex.Message}", ex);
        }
    }

    private static string Celda(string s)
    {
        s ??= "";
        return s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    // lectura de valores

    // sin mayúsculas, acentos, espacios ni signos: «N° de causa» = «nro. de causa»... (solo lo que coincide por texto)
    internal static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        foreach (char ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is 'º' or 'ª' or '°') continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string Texto(object o)
    {
        string s = o switch
        {
            null or DBNull => null,
            string t => t.Trim(),
            double d => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString(Formatos.Cultura),
            DateTime f => f.TimeOfDay == TimeSpan.Zero ? f.ToString("dd/MM/yyyy") : f.ToString("dd/MM/yyyy HH:mm"),
            bool b => b ? "Sí" : "No",
            _ => Convert.ToString(o, CultureInfo.InvariantCulture)?.Trim()
        };
        if (string.IsNullOrEmpty(s)) return null;
        // el exportador antepone ' a lo que parece una fórmula: se lo saca
        if (s.Length > 1 && s[0] == '\'' && "=+-@".Contains(s[1])) s = s[1..];
        return s;
    }

    private static readonly string[] FormatosFecha =
    [
        "d/M/yyyy", "d/M/yy", "d-M-yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy/MM/dd",
        "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"
    ];

    private static DateTime? Fecha(object o, string campo)
    {
        switch (o)
        {
            case null or DBNull: return null;
            case DateTime f: return f.Date;
            case double d when d > 1 && d < 2958466: return DateTime.FromOADate(d).Date;   // fecha de Excel guardada como número
        }
        string t = Texto(o);
        if (t == null) return null;
        if (DateTime.TryParseExact(t, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var r)) return r.Date;
        throw new ExcepcionNegocio($"{campo}: «{t}» no es una fecha válida (use dd/mm/aaaa).");
    }

    private static int? Entero(object o, string campo)
    {
        if (o is double d) return d == Math.Floor(d) ? (int)d : throw new ExcepcionNegocio($"{campo}: «{d}» tiene que ser un número entero.");
        string t = Texto(o);
        if (t == null) return null;
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n
            : throw new ExcepcionNegocio($"{campo}: «{t}» tiene que ser un número entero.");
    }

    private static decimal? Monto(object o, string campo)
    {
        if (o is double d) return Math.Round((decimal)d, 2);
        string t = Texto(o)?.Replace("$", "").Replace(" ", "");
        if (string.IsNullOrEmpty(t)) return null;
        decimal r;
        bool ok;
        if (t.Contains(',') && t.Contains('.')) ok = decimal.TryParse(t, NumberStyles.Number, Formatos.Cultura, out r);         
        else if (t.Contains(',')) ok = decimal.TryParse(t, NumberStyles.Number, Formatos.Cultura, out r);                          
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d{1,3}(\.\d{3})+$"))                                         
            ok = decimal.TryParse(t, NumberStyles.Number, Formatos.Cultura, out r);
        else ok = decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out r);                                    
        return ok ? Math.Round(r, 2) : throw new ExcepcionNegocio($"{campo}: «{t}» no es un monto válido.");
    }

    private static bool Booleano(object o, string campo)
    {
        if (o is bool b) return b;
        string t = Norm(Texto(o));
        if (t is "" or "no" or "n" or "0" or "false" or "falso") return false;
        if (t is "si" or "s" or "1" or "true" or "verdadero" or "x") return true;
        throw new ExcepcionNegocio($"{campo}: «{Texto(o)}» tiene que ser Sí o No.");
    }
}
