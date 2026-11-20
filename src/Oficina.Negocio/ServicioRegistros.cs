using Oficina.Datos;
using Oficina.Entidades;

namespace Oficina.Negocio;

// todo lo que ve una ficha de acta
public class FichaActa
{
    public Acta Acta { get; set; }
    public Persona Persona { get; set; }
    public List<Movimiento> Recorrido { get; set; }
    public List<ActaVersion> Versiones { get; set; }
    public List<FilaComparacion> Comparacion { get; set; }
    public List<EntradaHistorial> Historial { get; set; }
    public bool PuedeModificar { get; set; }
    public bool PuedeCargarApremio { get; set; }
    public string NroLote { get; set; }          // lote donde se archivó, si está terminada

    public ActaVersion Original => Versiones.FirstOrDefault();
}

public class FilaComparacion
{
    public string Campo { get; set; }
    public string Original { get; set; }
    public string Actual { get; set; }
    public bool Cambio => Original != Actual;
}

// alta, modificación y baja de lo que anota cada sector. Valida, controla permisos y escribe el historial
public class ServicioRegistros : ServicioBase
{
    public ServicioRegistros(Sesion sesion, IDatosRegistros datos) : base(sesion, datos) { }

    public List<Reparticion> Reparticiones() => Datos.ListarReparticiones();
    public List<Juzgado> Juzgados() => Datos.ListarJuzgados();

    //  grillas por sector (con el buscador propio del sector)

    public List<Movimiento> Movimientos(int idSector, string filtro = null)
    {
        Sesion.Exigir(idSector, Accion.Leer);
        var t = filtro?.Trim();
        return Datos.ListarMovimientos(idSector).Where(m => string.IsNullOrEmpty(t)
            || m.Anio.ToString() == t
            || TextoBusqueda.AlgunoContiene(t, m.NroCausa, m.Reparticion, m.NroActa, m.NroApremio, m.Persona, m.Documento, m.Detalle, m.Remito, m.Observaciones)).ToList();
    }

    public List<Reclamo> Reclamos(int idSector, string filtro = null)
    {
        SectorDe(idSector, TipoSector.Reclamos, Accion.Leer);
        var t = filtro?.Trim();
        return Datos.ListarReclamos().Where(r => string.IsNullOrEmpty(t)
            || TextoBusqueda.AlgunoContiene(t, r.NroCaso, r.Ubicacion, r.Entrecalles, r.Delegacion, r.Vehiculo, r.Denunciante,
                                           r.VerificadoPor, r.NroActaInfraccion, r.Observaciones, r.TextoOrigen, r.TextoCategoria, r.TextoEstado)).ToList();
    }

    public List<Lote> Lotes(int idSector, string filtro = null)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Leer);
        var t = filtro?.Trim();
        return Datos.ListarLotes().Where(l => string.IsNullOrEmpty(t) || TextoBusqueda.AlgunoContiene(t, l.NroLote, l.Observaciones)).ToList();
    }

    // actas y movimientos

    // Sugerencias que no nombran sectores (por ahora el movimiento es texto libre)
    private static readonly string[] SugerenciasGenerales =
    [
        "Ingreso de acta", "Recepción de acta por remito", "Control de plazos", "Pedido de informe", "Auditoría de acta",
        "Observada", "Sentencia dictada", "Notificación de sentencia", "Apelación presentada",
        "Inicio de juicio de apremio", "Plan de pago acordado", "Embargo trabado", "Archivo"
    ];

 
    /// sugerencias para el campo Movimiento: las generales, "Pase a …" solo hacia sectores que la cuenta ve,
    // y los movimientos que ya se usaron en este sector.(no entendi nada)
   
    public List<string> SugerenciasDeMovimiento(int idSector)
    {
        Sesion.Exigir(idSector, Accion.Leer);
        var pases = Sesion.SectoresVisibles.Where(s => s.Tipo == TipoSector.Actas && s.IdSector != idSector).Select(s => $"Pase a {s.Nombre}");
        var usados = Datos.ListarMovimientos(idSector).Select(m => m.Detalle);
        return SugerenciasGenerales.Concat(pases).Concat(usados)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).ToList();
    }

    // Para el formulario de carga: si el acta ya existe, se completan sus datos solos
    public Acta BuscarActa(string nroCausa, int idReparticion, int anio)
        => string.IsNullOrWhiteSpace(nroCausa) || idReparticion == 0 || anio == 0 ? null : Datos.BuscarActa(nroCausa.Trim(), idReparticion, anio);

    public Movimiento ObtenerMovimiento(int idMovimiento)
    {
        var m = Datos.ObtenerMovimiento(idMovimiento) ?? throw new ExcepcionNegocio("El movimiento no existe.");
        Sesion.Exigir(m.IdSector, Accion.Leer);
        return m;
    }


    /// Anota un movimiento en el sector
    /// si el acta no existia la da de alta (con la persona si se pasa) y registra el movimiento. Si ya existia solo registra el movimiento.
    public Movimiento CargarMovimiento(int idSector, Acta acta, Persona persona, Movimiento movimiento)
    {
        Sesion.Exigir(idSector, Accion.Cargar);
        if (Sesion.Sector(idSector).Tipo != TipoSector.Actas) throw new ExcepcionNegocio("En este sector no se anotan actas.");
        ValidarClave(acta);
        ValidarMovimiento(movimiento);

        var existente = Datos.BuscarActa(acta.NroCausa, acta.IdReparticion, acta.Anio);
        if (existente == null)
        {
            ValidarActa(acta);
            ValidarPersona(persona);
        }
        else if (existente.Eliminado) throw new ExcepcionNegocio("Esa acta está dada de baja.");
        else if (existente.Archivada) throw new ExcepcionNegocio(ActaArchivada(existente));

        // el alta del acta (persona, acta, versión original) y la del movimiento van juntas: o se guarda todo o nada,
        // así nunca queda un acta sin movimiento (que no aparecería en ningún sector)
        Operacion(() =>
        {
            if (existente == null)
            {
                acta.IdPersona = ResolverPersona(persona, null, idSector);
                acta.Version = 1;
                acta.Eliminado = false;
                acta.IdUsuarioAlta = Sesion.IdUsuario;
                acta.FechaAlta = DateTime.Now;
                acta.IdUsuarioModif = null;
                acta.FechaModif = null;
                Datos.GuardarActa(acta);
                Datos.AgregarVersion(ActaVersion.Desde(acta, 1, "Acta original", Sesion.IdUsuario, acta.FechaAlta));
                Registrar("Acta", acta.IdActa, idSector, "Alta", $"Causa {acta.NroCausa} · acta {acta.NroActa}");
            }
            else acta = existente;

            movimiento.IdMovimiento = 0;
            movimiento.IdActa = acta.IdActa;
            movimiento.IdSector = idSector;
            movimiento.Eliminado = false;
            movimiento.IdUsuarioAlta = Sesion.IdUsuario;
            movimiento.FechaAlta = DateTime.Now;
            Datos.GuardarMovimiento(movimiento);
            Registrar("Movimiento", movimiento.IdMovimiento, idSector, "Alta", $"Causa {acta.NroCausa}: {movimiento.Detalle}");
        });
        return Datos.ObtenerMovimiento(movimiento.IdMovimiento);
    }

    public void ModificarMovimiento(Movimiento cambios)
    {
        var actual = Datos.ObtenerMovimiento(cambios.IdMovimiento) ?? throw new ExcepcionNegocio("El movimiento no existe.");
        Sesion.Exigir(actual.IdSector, Accion.Modificar);
        if (actual.Eliminado) throw new ExcepcionNegocio("El movimiento está dado de baja.");
        ExigirSinCambiosAjenos(cambios, actual);
        ExigirNoArchivada(actual.IdActa);
        ValidarMovimiento(cambios);

        var detalle = new Cambios()
            .Campo("Fecha", actual.Fecha, cambios.Fecha).Campo("Movimiento", actual.Detalle, Limpio(cambios.Detalle))
            .Campo("Remito", actual.Remito, Limpio(cambios.Remito)).Campo("Observaciones", actual.Observaciones, Limpio(cambios.Observaciones));
        if (!detalle.Hay) return;

        actual.Fecha = cambios.Fecha.Date;
        actual.Detalle = Limpio(cambios.Detalle);
        actual.Remito = Limpio(cambios.Remito);
        actual.Observaciones = Limpio(cambios.Observaciones);
        actual.IdUsuarioModif = Sesion.IdUsuario;
        actual.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarMovimiento(actual);
            Registrar("Movimiento", actual.IdMovimiento, actual.IdSector, "Modificación", $"Causa {actual.NroCausa}: {detalle}");
        });
    }

    // baja logica
    public void EliminarMovimiento(int idMovimiento, string motivo)
    {
        var m = Datos.ObtenerMovimiento(idMovimiento) ?? throw new ExcepcionNegocio("El movimiento no existe.");
        Sesion.Exigir(m.IdSector, Accion.Eliminar);
        if (m.Eliminado) return;
        ExigirNoArchivada(m.IdActa);
        m.Eliminado = true;
        m.IdUsuarioModif = Sesion.IdUsuario;
        m.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarMovimiento(m);
            Registrar("Movimiento", m.IdMovimiento, m.IdSector, "Baja", $"Causa {m.NroCausa}: {m.Detalle}" + (Limpio(motivo) is { } x ? $" · Motivo: {x}" : ""));
        });
    }

    public FichaActa ObtenerFicha(int idActa)
    {
        const string ActaNoDisponible = "El acta no existe o no está disponible.";
        var acta = Datos.ObtenerActa(idActa) ?? throw new ExcepcionNegocio(ActaNoDisponible);
        var todos = Datos.ListarMovimientosDeActa(idActa);
        if (!Sesion.EsAdministrador && !todos.Any(m => Sesion.Puede(m.IdSector, Accion.Leer)))
            throw new ExcepcionNegocio(ActaNoDisponible);

        // historial y recorridos, solo ven lo que pueden ver segun sus permisos
        var recorrido = todos.Where(m => Sesion.Puede(m.IdSector, Accion.Leer)).ToList();
        var versiones = Datos.ListarVersiones(idActa);
        // también las correcciones del nombre de la persona (se hacen desde cualquiera de sus actas y valen para todas)
        var historial = Sesion.Filtrar(Datos.ListarHistorial("Acta", idActa)
            .Concat(recorrido.SelectMany(m => Datos.ListarHistorial("Movimiento", m.IdMovimiento)))
            .Concat(acta.IdPersona is int idPer ? Datos.ListarHistorial("Persona", idPer) : []))
            .OrderByDescending(h => h.Fecha).ThenByDescending(h => h.IdHistorial).ToList();

        return new FichaActa
        {
            Acta = acta, Recorrido = recorrido, Versiones = versiones, Historial = historial,
            Persona = acta.IdPersona.HasValue ? Datos.ObtenerPersona(acta.IdPersona.Value) : null,
            Comparacion = Comparar(versiones.FirstOrDefault(), acta),
            PuedeModificar = !acta.Archivada && !acta.Eliminado && PuedeModificarActa(acta.IdActa, todos),
            PuedeCargarApremio = !acta.Archivada && !acta.Eliminado && SectorApremio() != null,
            NroLote = acta.IdLote.HasValue ? Datos.ObtenerLote(acta.IdLote.Value)?.NroLote : null
        };
    }

    private bool PuedeModificarActa(int idActa, List<Movimiento> movimientos = null)
        => Sesion.EsAdministrador || (movimientos ?? Datos.ListarMovimientosDeActa(idActa)).Any(m => Sesion.Puede(m.IdSector, Accion.Modificar));

    private static List<FilaComparacion> Comparar(ActaVersion original, Acta actual)
    {
        if (original == null) return new();
        FilaComparacion F(string campo, object o, object a) => new() { Campo = campo, Original = Cambios.Formato(o), Actual = Cambios.Formato(a) };
        return
        [
            F("N° de acta", original.NroActa, actual.NroActa),
            F("Fecha del acta", original.FechaActa, actual.FechaActa),
            F("Juzgado", original.Juzgado, actual.Juzgado),
            F("Persona", original.Persona, actual.Persona),
            F("Dominio", original.Dominio, actual.Dominio),
            F("Suma reclamada", original.SumaReclamada, actual.SumaReclamada),
            F("Gastos administrativos", original.GastosAdministrativos, actual.GastosAdministrativos),
            F("Observaciones", original.Observaciones, actual.Observaciones)
        ];
    }

    // cambia los datos del acta y guarda los nuevos
   
    public void ModificarActa(Acta cambios, Persona persona, string motivo)
    {
        var actual = Datos.ObtenerActa(cambios.IdActa) ?? throw new ExcepcionNegocio(Sesion.NoDisponible);
        var movimientos = Datos.ListarMovimientosDeActa(actual.IdActa);
        if (!PuedeModificarActa(actual.IdActa, movimientos))
            throw new ExcepcionNegocio(Sesion.EsAdministrador || movimientos.Any(m => Sesion.Puede(m.IdSector, Accion.Leer))
                ? Sesion.AccionNoDisponible : Sesion.NoDisponible);
        if (actual.Archivada) throw new ExcepcionNegocio(ActaArchivada(actual));
        ExigirSinCambiosAjenos(cambios, actual);
        Requerido(motivo, "el motivo del cambio");
        Largo(motivo, 200, "El motivo");
        ValidarActa(cambios);
        ValidarPersona(persona);

        // todo en una transacción: si no hay cambios o algo falla, tampoco queda guardada la persona
        int? sector = movimientos.Select(m => m.IdSector).FirstOrDefault(s => Sesion.Puede(s, Accion.Modificar));
        if (sector == 0) sector = null;
        Operacion(() =>
        {
            int? idPersona = ResolverPersona(persona, actual.IdPersona, sector);
            string juzgadoNuevo = Datos.ListarJuzgados().FirstOrDefault(j => j.IdJuzgado == cambios.IdJuzgado)?.Nombre;
            string personaNueva = idPersona == null ? null : Datos.ObtenerPersona(idPersona.Value)?.NombreCompleto;
            var detalle = new Cambios()
                .Campo("N° de acta", actual.NroActa, cambios.NroActa.Trim()).Campo("Fecha del acta", actual.FechaActa, cambios.FechaActa.Date)
                .Campo("Juzgado", actual.Juzgado, juzgadoNuevo).Campo("Persona", actual.Persona, personaNueva)
                .Campo("Dominio", actual.Dominio, cambios.Dominio)
                .Campo("Suma reclamada", actual.SumaReclamada, cambios.SumaReclamada).Campo("Gastos administrativos", actual.GastosAdministrativos, cambios.GastosAdministrativos)
                .Campo("Observaciones", actual.Observaciones, Limpio(cambios.Observaciones));
            if (!detalle.Hay) throw new ExcepcionNegocio("No hay cambios para guardar.");

            actual.NroActa = cambios.NroActa.Trim();
            actual.FechaActa = cambios.FechaActa.Date;
            actual.IdJuzgado = cambios.IdJuzgado;
            actual.IdPersona = idPersona;
            actual.Dominio = cambios.Dominio;
            actual.SumaReclamada = cambios.SumaReclamada;
            actual.GastosAdministrativos = cambios.GastosAdministrativos;
            actual.Observaciones = Limpio(cambios.Observaciones);
            actual.Version++;
            actual.IdUsuarioModif = Sesion.IdUsuario;
            actual.FechaModif = DateTime.Now;
            Datos.GuardarActa(actual);   // con la revisión que se leyó: si otro la cambió recién, no se guarda
            Datos.AgregarVersion(ActaVersion.Desde(actual, actual.Version, motivo.Trim(), Sesion.IdUsuario, actual.FechaModif.Value));
            Registrar("Acta", actual.IdActa, sector, "Modificación",
                $"Versión {actual.Version} · {motivo.Trim()} · {detalle}");
        });
    }

    /// La persona con ese documento, si ya está cargada (para completar el nombre en los formularios). El documento identifica
    /// a la persona: el mismo DNI o CUIT es siempre la misma persona, en todas sus actas.
    public Persona PersonaPorDocumento(string tipoDocumento, string nroDocumento)
    {
        var nro = Limpio(nroDocumento);
        return nro == null || string.IsNullOrEmpty(tipoDocumento) ? null : Datos.BuscarPersonaPorDocumento(tipoDocumento, nro);
    }

    // Devuelve el id de la persona. El documento la identifica: si ya existe, es esa persona, y si el nombre vino
    // distinto se corrige (en todas sus actas) y queda en el historial. Sin documento no se puede saber si es la misma:
    // se usa la del acta si no cambió nada, o se crea una nueva.
    private int? ResolverPersona(Persona p, int? idActual, int? idSector)
    {
        if (p == null || string.IsNullOrWhiteSpace(p.Apellido)) return null;
        p.Apellido = p.Apellido.Trim();
        p.Nombre = Limpio(p.Nombre);
        p.NroDocumento = Limpio(p.NroDocumento);
        var actual = idActual == null ? null : Datos.ObtenerPersona(idActual.Value);
        if (actual != null && actual.Apellido == p.Apellido && actual.Nombre == p.Nombre
            && actual.TipoDocumento == p.TipoDocumento && actual.NroDocumento == p.NroDocumento)
            return actual.IdPersona;
        if (p.NroDocumento != null)
        {
            var existente = Datos.BuscarPersonaPorDocumento(p.TipoDocumento, p.NroDocumento);
            if (existente != null)
            {
                if (existente.Apellido != p.Apellido || existente.Nombre != p.Nombre)
                {
                    string antes = existente.NombreCompleto;
                    existente.Apellido = p.Apellido;
                    existente.Nombre = p.Nombre;
                    Datos.GuardarPersona(existente);
                    Registrar("Persona", existente.IdPersona, idSector, "Modificación",
                        $"{existente.TipoDocumento} {existente.NroDocumento}: {antes} → {existente.NombreCompleto} (se corrige en todas sus actas)");
                }
                return existente.IdPersona;
            }
        }
        p.IdPersona = 0;
        Datos.GuardarPersona(p);
        return p.IdPersona;
    }

    internal static void ValidarClave(Acta a)
    {
        Requerido(a.NroCausa, "el N° de causa");
        Largo(a.NroCausa.Trim(), 20, "El N° de causa");
        a.NroCausa = a.NroCausa.Trim();
        if (a.IdReparticion == 0) throw new ExcepcionNegocio("Falta elegir la repartición.");
        if (a.Anio < 1990 || a.Anio > DateTime.Today.Year + 1) throw new ExcepcionNegocio("El año del acta no es válido.");
    }

    public const decimal MontoMaximo = 9_999_999_999.99m;

    internal static void ValidarActa(Acta a)
    {
        Requerido(a.NroActa, "el N° de acta");
        Largo(a.NroActa.Trim(), 20, "El N° de acta");
        Largo(a.Observaciones, 500, "Las observaciones del acta");
        // el dominio se guarda siempre igual (sin espacios ni guiones, en mayúsculas) para que la búsqueda lo encuentre
        a.Dominio = Limpio(a.Dominio)?.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (a.Dominio != null && (a.Dominio.Length < 6 || a.Dominio.Length > 10 || !a.Dominio.All(char.IsLetterOrDigit)))
            throw new ExcepcionNegocio("El dominio va con letras y números, por ejemplo AB123CD o ABC123.");
        if (a.FechaActa.Date > DateTime.Today) throw new ExcepcionNegocio("La fecha del acta no puede ser futura.");
        if (a.SumaReclamada < 0 || a.GastosAdministrativos < 0) throw new ExcepcionNegocio("Los montos no pueden ser negativos.");
        // la base guarda montos de hasta 10 cifras enteras con 2 decimales (DECIMAL(12,2))
        if (a.SumaReclamada > MontoMaximo || a.GastosAdministrativos > MontoMaximo)
            throw new ExcepcionNegocio($"Los montos no pueden pasar de {MontoMaximo.ToString("N2", Formatos.Cultura)}.");
    }

    internal static void ValidarPersona(Persona p)
    {
        if (p == null || string.IsNullOrWhiteSpace(p.Apellido)) return;
        Largo(p.Apellido.Trim(), 80, "El apellido");
        Largo(p.Nombre?.Trim(), 80, "El nombre");
        var doc = Limpio(p.NroDocumento);
        if (doc == null) return;
        if (!doc.All(char.IsDigit)) throw new ExcepcionNegocio("El documento va solo con números, sin puntos ni guiones.");
        if (p.TipoDocumento == "DNI" && (doc.Length < 7 || doc.Length > 8)) throw new ExcepcionNegocio("El DNI tiene que tener 7 u 8 números.");
        if (p.TipoDocumento == "CUIT" && doc.Length != 11) throw new ExcepcionNegocio("El CUIT tiene que tener 11 números.");
    }

    internal static void ValidarMovimiento(Movimiento m)
    {
        Requerido(m.Detalle, "el movimiento");
        Largo(m.Detalle.Trim(), 100, "El movimiento");
        Largo(Limpio(m.Remito), 30, "El remito");
        Largo(m.Observaciones, 500, "Las observaciones");
        if (m.Fecha.Date > DateTime.Today) throw new ExcepcionNegocio("La fecha del movimiento no puede ser futura.");
        m.Detalle = m.Detalle.Trim();
    }

    // actas en lotes
    // El acta termina cuando se archiva en un lote, cuando se archiva queda en solo lectura

    private string ActaArchivada(Acta a)
        => $"El acta está archivada en el lote {Datos.ObtenerLote(a.IdLote ?? 0)?.NroLote ?? "?"} ({a.TextoMotivoArchivo?.ToLowerInvariant()}): está terminada y no admite cambios.";

    private void ExigirNoArchivada(int idActa)
    {
        var a = Datos.ObtenerActa(idActa);
        if (a?.Archivada == true) throw new ExcepcionNegocio(ActaArchivada(a));
    }

    // un registro dado de baja ya no se ve ni se modifica: queda solo para el historial
    private static T Vigente<T>(T registro, string que) where T : RegistroAuditado
        => registro is { Eliminado: false } ? registro : throw new ExcepcionNegocio($"{que} no existe o fue dado de baja.");

    // el sector tiene que existir para la cuenta, tener el permiso y ser del tipo pedido
    private Sector SectorDe(int idSector, TipoSector tipo, Accion accion)
    {
        Sesion.Exigir(idSector, accion);
        var s = Sesion.Sector(idSector);
        if (s.Tipo != tipo) throw new ExcepcionNegocio(Sesion.AccionNoDisponible);
        return s;
    }

    public List<Acta> ActasDelLote(int idSector, int idLote)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Leer);
        return Datos.ListarActasDeLote(idLote);
    }

    // archiva en el lote el acta que se identifica por causa + repartición + año.
    public Acta ArchivarActa(int idSector, int idLote, string nroCausa, int idReparticion, int anio, MotivoArchivo? motivo, DateTime fecha)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Modificar);
        var lote = Datos.ObtenerLote(idLote);
        if (lote == null || lote.Eliminado) throw new ExcepcionNegocio("El lote no existe.");
        var clave = new Acta { NroCausa = nroCausa, IdReparticion = idReparticion, Anio = anio };
        ValidarClave(clave);
        if (motivo is not { } m || !Enum.IsDefined(m)) throw new ExcepcionNegocio("Falta elegir el motivo: sentencia definitiva o pagada.");
        if (fecha.Date > DateTime.Today) throw new ExcepcionNegocio("La fecha de archivo no puede ser futura.");

        var acta = Datos.BuscarActa(clave.NroCausa, clave.IdReparticion, clave.Anio)
                   ?? throw new ExcepcionNegocio("No hay ningún acta con ese N° de causa, repartición y año.");
        if (acta.Eliminado) throw new ExcepcionNegocio("Esa acta está dada de baja.");
        if (acta.Archivada) throw new ExcepcionNegocio(ActaArchivada(acta));

        acta.IdLote = idLote;
        acta.MotivoArchivo = m;
        acta.FechaArchivo = fecha.Date;
        acta.IdUsuarioModif = Sesion.IdUsuario;
        acta.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarActa(acta);
            Registrar("Acta", acta.IdActa, idSector, "Archivo", $"Causa {acta.NroCausa} · lote {lote.NroLote} · {acta.TextoMotivoArchivo} · {fecha:dd/MM/yyyy}");
        });
        return acta;
    }

    //saca el acta del lote (por ejemplo, si se archivó por error). Solo el Administrador.
    public void QuitarDelLote(int idSector, int idActa, string motivo)
    {
        Sesion.ExigirAdministrador();
        var acta = Datos.ObtenerActa(idActa) ?? throw new ExcepcionNegocio(Sesion.NoDisponible);
        if (!acta.Archivada) return;
        string lote = Datos.ObtenerLote(acta.IdLote.Value)?.NroLote;
        acta.IdLote = null;
        acta.MotivoArchivo = null;
        acta.FechaArchivo = null;
        acta.IdUsuarioModif = Sesion.IdUsuario;
        acta.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarActa(acta);
            Registrar("Acta", acta.IdActa, idSector, "Modificación",
                $"Causa {acta.NroCausa} · se sacó del lote {lote}" + (Limpio(motivo) is { } x ? $" · Motivo: {x}" : ""));
        });
    }

    // apremio
    // Apremio le da a cada acta un número propio q lo carga solo el sector Apremio

    public const string ClaveApremio = "APR";

    private Sector SectorApremio()
    {
        var s = Sesion.SectoresVisibles.FirstOrDefault(x => x.Clave == ClaveApremio);
        return s != null && Sesion.Puede(s.IdSector, Accion.Modificar) ? s : null;
    }

    public void CambiarNroApremio(int idActa, string nro)
    {
        var apremio = SectorApremio() ?? throw new ExcepcionNegocio(Sesion.AccionNoDisponible);
        var acta = Datos.ObtenerActa(idActa) ?? throw new ExcepcionNegocio(Sesion.NoDisponible);
        if (acta.Eliminado) throw new ExcepcionNegocio("Esa acta está dada de baja.");
        if (acta.Archivada) throw new ExcepcionNegocio(ActaArchivada(acta));
        nro = Limpio(nro);
        Largo(nro, 20, "El N° de apremio");
        var detalle = new Cambios().Campo("N° de apremio", acta.NroApremio, nro);
        if (!detalle.Hay) return;
        acta.NroApremio = nro;
        acta.IdUsuarioModif = Sesion.IdUsuario;
        acta.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarActa(acta);
            Registrar("Acta", acta.IdActa, apremio.IdSector, "Modificación", $"Causa {acta.NroCausa} · {detalle}");
        });
    }

    // reclamos

    public Reclamo ObtenerReclamo(int idSector, int idReclamo)
    {
        SectorDe(idSector, TipoSector.Reclamos, Accion.Leer);
        return Vigente(Datos.ObtenerReclamo(idReclamo), "El reclamo");
    }

    public void CargarReclamo(int idSector, Reclamo r)
    {
        SectorDe(idSector, TipoSector.Reclamos, Accion.Cargar);
        ValidarReclamo(r);
        r.IdReclamo = 0;
        r.Eliminado = false;
        r.IdUsuarioAlta = Sesion.IdUsuario;
        r.FechaAlta = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarReclamo(r);
            Registrar("Reclamo", r.IdReclamo, idSector, "Alta", $"Caso {r.NroCaso} · {r.Ubicacion}");
        });
    }

    public void ModificarReclamo(int idSector, Reclamo cambios)
    {
        SectorDe(idSector, TipoSector.Reclamos, Accion.Modificar);
        var actual = Vigente(Datos.ObtenerReclamo(cambios.IdReclamo), "El reclamo");
        ExigirSinCambiosAjenos(cambios, actual);
        ValidarReclamo(cambios);
        var detalle = new Cambios()
            .Campo("N° de caso", actual.NroCaso, cambios.NroCaso).Campo("Fecha de ingreso", actual.Fecha, cambios.Fecha)
            .Campo("Origen", actual.TextoOrigen, cambios.TextoOrigen).Campo("Categoría", actual.TextoCategoria, cambios.TextoCategoria)
            .Campo("Estado", actual.TextoEstado, cambios.TextoEstado)
            .Campo("Dirección", actual.Ubicacion, cambios.Ubicacion).Campo("Entrecalles", actual.Entrecalles, cambios.Entrecalles)
            .Campo("Delegación", actual.Delegacion, cambios.Delegacion).Campo("Vehículo", actual.Vehiculo, cambios.Vehiculo)
            .Campo("Denunciante", actual.Denunciante, cambios.Denunciante)
            .Campo("Verificado", actual.Verificado, cambios.Verificado).Campo("Primera verificación", actual.FechaVerificacion, cambios.FechaVerificacion)
            .Campo("Inspector", actual.VerificadoPor, cambios.VerificadoPor).Campo("Acta de infracción", actual.NroActaInfraccion, cambios.NroActaInfraccion)
            .Campo("Cédula de notificación", actual.FechaNotificacion, cambios.FechaNotificacion)
            .Campo("Observaciones", actual.Observaciones, cambios.Observaciones);
        if (!detalle.Hay) return;
        cambios.Eliminado = actual.Eliminado;
        cambios.IdUsuarioAlta = actual.IdUsuarioAlta;
        cambios.FechaAlta = actual.FechaAlta;
        cambios.IdUsuarioModif = Sesion.IdUsuario;
        cambios.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarReclamo(cambios);
            Registrar("Reclamo", cambios.IdReclamo, idSector, "Modificación", $"Caso {cambios.NroCaso}: {detalle}");
        });
    }

    public void EliminarReclamo(int idSector, int idReclamo, string motivo)
    {
        SectorDe(idSector, TipoSector.Reclamos, Accion.Eliminar);
        var r = Datos.ObtenerReclamo(idReclamo) ?? throw new ExcepcionNegocio("El reclamo no existe.");
        if (r.Eliminado) return;   // ya estaba dado de baja
        r.Eliminado = true;
        r.IdUsuarioModif = Sesion.IdUsuario;
        r.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarReclamo(r);
            Registrar("Reclamo", r.IdReclamo, idSector, "Baja", $"Caso {r.NroCaso}" + (Limpio(motivo) is { } x ? $" · Motivo: {x}" : ""));
        });
    }

    internal static void ValidarReclamo(Reclamo r)
    {
        Requerido(r.NroCaso, "el N° de caso");
        Requerido(r.Ubicacion, "la dirección");
        r.NroCaso = r.NroCaso.Trim();
        r.Ubicacion = r.Ubicacion.Trim();
        r.Entrecalles = Limpio(r.Entrecalles);
        r.Delegacion = Limpio(r.Delegacion);
        r.Vehiculo = Limpio(r.Vehiculo);
        r.Denunciante = Limpio(r.Denunciante);
        r.VerificadoPor = Limpio(r.VerificadoPor);
        r.NroActaInfraccion = Limpio(r.NroActaInfraccion);
        r.Observaciones = Limpio(r.Observaciones);
        Largo(r.NroCaso, 20, "El N° de caso");
        Largo(r.Ubicacion, 150, "La dirección");
        Largo(r.Entrecalles, 150, "Las entrecalles");
        Largo(r.Delegacion, 80, "La delegación");
        Largo(r.Vehiculo, 100, "El vehículo");
        Largo(r.Denunciante, 120, "El denunciante");
        Largo(r.VerificadoPor, 80, "El inspector");
        Largo(r.NroActaInfraccion, 20, "El N° de acta de infracción");
        Largo(r.Observaciones, 500, "Las observaciones");
        if (r.Origen is { } o && !Enum.IsDefined(o)) throw new ExcepcionNegocio("El origen del reclamo no es válido.");
        if (r.Categoria is { } cat && !Enum.IsDefined(cat)) throw new ExcepcionNegocio("La categoría del reclamo no es válida (Tránsito, Demanda o Municipales).");
        if (!Enum.IsDefined(r.Estado)) throw new ExcepcionNegocio("El estado del reclamo no es válido.");
        if (r.Fecha.Date > DateTime.Today) throw new ExcepcionNegocio("La fecha del reclamo no puede ser futura.");
        if (r.FechaNotificacion?.Date < r.Fecha.Date) throw new ExcepcionNegocio("La cédula de notificación no puede ser anterior al reclamo.");
        if (r.Verificado)
        {
            if (r.FechaVerificacion == null) throw new ExcepcionNegocio("Falta la fecha de la primera verificación.");
            if (r.FechaVerificacion.Value.Date < r.Fecha.Date) throw new ExcepcionNegocio("La verificación no puede ser anterior al reclamo.");
            Requerido(r.VerificadoPor, "el inspector que fue a verificar");
        }
        else
        {
            r.FechaVerificacion = null;
            r.VerificadoPor = null;
        }
    }

    // lotes

    public Lote ObtenerLote(int idSector, int idLote)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Leer);
        return Vigente(Datos.ObtenerLote(idLote), "El lote");
    }

    public void CargarLote(int idSector, Lote l)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Cargar);
        ValidarLote(l);
        l.IdLote = 0;
        l.Eliminado = false;
        l.IdUsuarioAlta = Sesion.IdUsuario;
        l.FechaAlta = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarLote(l);
            Registrar("Lote", l.IdLote, idSector, "Alta", $"Lote {l.NroLote} · {l.Total} actas");
        });
    }

    public void ModificarLote(int idSector, Lote cambios)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Modificar);
        var actual = Vigente(Datos.ObtenerLote(cambios.IdLote), "El lote");
        ExigirSinCambiosAjenos(cambios, actual);
        ValidarLote(cambios);
        var detalle = new Cambios()
            .Campo("N° de lote", actual.NroLote, cambios.NroLote).Campo("Fecha", actual.Fecha, cambios.Fecha)
            .Campo("Tránsito", actual.CantTransito, cambios.CantTransito).Campo("Competencia", actual.CantCompetencia, cambios.CantCompetencia)
            .Campo("Prescriptas", actual.CantPrescriptas, cambios.CantPrescriptas).Campo("Observaciones", actual.Observaciones, cambios.Observaciones);
        if (!detalle.Hay) return;
        cambios.Eliminado = actual.Eliminado;
        cambios.IdUsuarioAlta = actual.IdUsuarioAlta;
        cambios.FechaAlta = actual.FechaAlta;
        cambios.IdUsuarioModif = Sesion.IdUsuario;
        cambios.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarLote(cambios);
            Registrar("Lote", cambios.IdLote, idSector, "Modificación", $"Lote {cambios.NroLote}: {detalle}");
        });
    }

    public void EliminarLote(int idSector, int idLote, string motivo)
    {
        SectorDe(idSector, TipoSector.Lotes, Accion.Eliminar);
        var l = Datos.ObtenerLote(idLote) ?? throw new ExcepcionNegocio("El lote no existe.");
        if (l.Eliminado) return;   // ya estaba dado de baja
        l.Eliminado = true;
        l.IdUsuarioModif = Sesion.IdUsuario;
        l.FechaModif = DateTime.Now;
        Operacion(() =>
        {
            Datos.GuardarLote(l);
            Registrar("Lote", l.IdLote, idSector, "Baja", $"Lote {l.NroLote}" + (Limpio(motivo) is { } x ? $" · Motivo: {x}" : ""));
        });
    }

    internal static void ValidarLote(Lote l)
    {
        Requerido(l.NroLote, "el N° de lote");
        l.NroLote = l.NroLote.Trim();
        l.Observaciones = Limpio(l.Observaciones);
        Largo(l.NroLote, 20, "El N° de lote");
        Largo(l.Observaciones, 500, "Las observaciones");
        if (l.CantTransito < 0 || l.CantCompetencia < 0 || l.CantPrescriptas < 0) throw new ExcepcionNegocio("Las cantidades no pueden ser negativas.");
        if (l.Total == 0) throw new ExcepcionNegocio("El lote no tiene actas.");
        if (l.Fecha.Date > DateTime.Today) throw new ExcepcionNegocio("La fecha del lote no puede ser futura.");
    }
}
