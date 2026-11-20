using System.Data;
using Npgsql;
using NpgsqlTypes;
using Oficina.Entidades;

namespace Oficina.Datos.Postgres;

public class PgDatosRegistros : ConexionPg, IDatosRegistros
{
    public PgDatosRegistros(string cadenaConexion) : base(cadenaConexion) { }

    private const string NombrePersona =
        "CASE WHEN p.IdPersona IS NULL THEN NULL WHEN COALESCE(p.Nombre, '') = '' THEN p.Apellido ELSE p.Apellido || ', ' || p.Nombre END";

    private static string ListaIds(IEnumerable<int> ids) => string.Join(",", ids);

    private static void Auditoria(RegistroAuditado x, NpgsqlDataReader r)
    {
        x.Eliminado = r.Logico("Eliminado");
        x.IdUsuarioAlta = r.Entero("IdUsuarioAlta");
        x.FechaAlta = r.Fecha("FechaAlta");
        x.IdUsuarioModif = r.EnteroN("IdUsuarioModif");
        x.FechaModif = r.FechaN("FechaModif");
        x.Revision = Convert.ToUInt32(r["Revision"]);   // xmin de la fila: cambia cada vez que alguien la guarda
    }

    private static NpgsqlParameter[] ParametrosAuditoria(RegistroAuditado x) =>
    [
        P("@Eliminado", x.Eliminado), P("@UsuarioAlta", x.IdUsuarioAlta), P("@FechaAlta", x.FechaAlta),
        P("@UsuarioModif", x.IdUsuarioModif), P("@FechaModif", x.FechaModif)
    ];

    // listas

    public List<Reparticion> ListarReparticiones() => Leer("SELECT * FROM Reparticion ORDER BY Codigo",
        r => new Reparticion { IdReparticion = r.Entero("IdReparticion"), Codigo = r.Entero("Codigo"), Nombre = r.Texto("Nombre") });

    public void GuardarReparticion(Reparticion reparticion)
    {
        if (reparticion.IdReparticion == 0)
            reparticion.IdReparticion = Insertar("INSERT INTO Reparticion (Codigo, Nombre) VALUES (@Codigo, @Nombre)",
                P("@Codigo", reparticion.Codigo), P("@Nombre", reparticion.Nombre));
        else
            Ejecutar("UPDATE Reparticion SET Codigo = @Codigo, Nombre = @Nombre WHERE IdReparticion = @Id",
                P("@Codigo", reparticion.Codigo), P("@Nombre", reparticion.Nombre), P("@Id", reparticion.IdReparticion));
    }

    public List<Juzgado> ListarJuzgados() => Leer("SELECT * FROM Juzgado ORDER BY Nombre",
        r => new Juzgado { IdJuzgado = r.Entero("IdJuzgado"), Nombre = r.Texto("Nombre") });

    public void GuardarJuzgado(Juzgado juzgado)
    {
        if (juzgado.IdJuzgado == 0)
            juzgado.IdJuzgado = Insertar("INSERT INTO Juzgado (Nombre) VALUES (@Nombre)", P("@Nombre", juzgado.Nombre));
        else
            Ejecutar("UPDATE Juzgado SET Nombre = @Nombre WHERE IdJuzgado = @Id", P("@Nombre", juzgado.Nombre), P("@Id", juzgado.IdJuzgado));
    }

    // personas

    private static Persona MapPersona(NpgsqlDataReader r) => new()
    {
        IdPersona = r.Entero("IdPersona"), Apellido = r.Texto("Apellido"), Nombre = r.Texto("Nombre"),
        TipoDocumento = r.Texto("TipoDocumento"), NroDocumento = r.Texto("NroDocumento")
    };

    public Persona ObtenerPersona(int idPersona) => LeerUno("SELECT * FROM Persona WHERE IdPersona = @Id", MapPersona, P("@Id", idPersona));

    public Persona BuscarPersonaPorDocumento(string tipoDocumento, string nroDocumento) =>
        LeerUno("SELECT * FROM Persona WHERE TipoDocumento = @Tipo AND NroDocumento = @Nro", MapPersona, P("@Tipo", tipoDocumento), P("@Nro", nroDocumento));

    public void GuardarPersona(Persona persona)
    {
        var ps = new[] { P("@Apellido", persona.Apellido), P("@Nombre", persona.Nombre), P("@Tipo", persona.TipoDocumento), P("@Nro", persona.NroDocumento) };
        if (persona.IdPersona == 0)
            persona.IdPersona = Insertar("INSERT INTO Persona (Apellido, Nombre, TipoDocumento, NroDocumento) VALUES (@Apellido, @Nombre, @Tipo, @Nro)", ps);
        else
            Ejecutar("UPDATE Persona SET Apellido = @Apellido, Nombre = @Nombre, TipoDocumento = @Tipo, NroDocumento = @Nro WHERE IdPersona = @Id",
                [.. ps, P("@Id", persona.IdPersona)]);
    }

    // actas

    private const string SelectActa = $"""
        SELECT a.*, a.xmin AS Revision, CONCAT(r.Codigo, ' · ', r.Nombre) AS NombreReparticion, j.Nombre AS NombreJuzgado, {NombrePersona} AS NombrePersona, p.NroDocumento AS Documento
        FROM Acta a
        JOIN Reparticion r ON r.IdReparticion = a.IdReparticion
        LEFT JOIN Juzgado j ON j.IdJuzgado = a.IdJuzgado
        LEFT JOIN Persona p ON p.IdPersona = a.IdPersona
        """;

    private static Acta MapActa(NpgsqlDataReader r)
    {
        var a = new Acta
        {
            IdActa = r.Entero("IdActa"), NroCausa = r.Texto("NroCausa"), IdReparticion = r.Entero("IdReparticion"), Anio = r.Entero("Anio"),
            NroActa = r.Texto("NroActa"), IdJuzgado = r.EnteroN("IdJuzgado"), IdPersona = r.EnteroN("IdPersona"), Dominio = r.Texto("Dominio"),
            FechaActa = r.Fecha("FechaActa"),
            SumaReclamada = r.DecimalN("SumaReclamada"), GastosAdministrativos = r.DecimalN("GastosAdministrativos"), Observaciones = r.Texto("Observaciones"), Version = r.Entero("Version"),
            IdLote = r.EnteroN("IdLote"), MotivoArchivo = (MotivoArchivo?)r.EnteroN("MotivoArchivo"), FechaArchivo = r.FechaN("FechaArchivo"),
            NroApremio = r.Texto("NroApremio"),
            Reparticion = r.Texto("NombreReparticion"), Juzgado = r.Texto("NombreJuzgado"), Persona = r.Texto("NombrePersona"), Documento = r.Texto("Documento")
        };
        Auditoria(a, r);
        return a;
    }

    public Acta ObtenerActa(int idActa) => LeerUno(SelectActa + " WHERE a.IdActa = @Id", MapActa, P("@Id", idActa));

    public Acta BuscarActa(string nroCausa, int idReparticion, int anio) =>
        LeerUno(SelectActa + " WHERE a.NroCausa = @Causa AND a.IdReparticion = @Rep AND a.Anio = @Anio", MapActa,
            P("@Causa", nroCausa), P("@Rep", idReparticion), P("@Anio", anio));

    public void GuardarActa(Acta acta)
    {
        NpgsqlParameter[] ps =
        [
            P("@Causa", acta.NroCausa), P("@Rep", acta.IdReparticion), P("@Anio", acta.Anio), P("@NroActa", acta.NroActa),
            P("@Juzgado", acta.IdJuzgado), P("@Persona", acta.IdPersona), P("@Dominio", acta.Dominio), P("@FechaActa", acta.FechaActa.Date),
            P("@SumaReclamada", acta.SumaReclamada), P("@GastosAdministrativos", acta.GastosAdministrativos), P("@Obs", acta.Observaciones), P("@Version", acta.Version),
            P("@Lote", acta.IdLote), P("@MotivoArchivo", (byte?)acta.MotivoArchivo), P("@FechaArchivo", acta.FechaArchivo?.Date), P("@Apremio", acta.NroApremio),
            .. ParametrosAuditoria(acta)
        ];
        if (acta.IdActa == 0)
            acta.IdActa = Insertar("""
                INSERT INTO Acta (NroCausa, IdReparticion, Anio, NroActa, IdJuzgado, IdPersona, Dominio, FechaActa, SumaReclamada, GastosAdministrativos, Observaciones,
                                  Version, IdLote, MotivoArchivo, FechaArchivo, NroApremio, Eliminado, IdUsuarioAlta, FechaAlta, IdUsuarioModif, FechaModif)
                VALUES (@Causa, @Rep, @Anio, @NroActa, @Juzgado, @Persona, @Dominio, @FechaActa, @SumaReclamada, @GastosAdministrativos, @Obs,
                        @Version, @Lote, @MotivoArchivo, @FechaArchivo, @Apremio, @Eliminado, @UsuarioAlta, @FechaAlta, @UsuarioModif, @FechaModif)
                """, ps);
        else
            Actualizar("""
                UPDATE Acta SET NroCausa = @Causa, IdReparticion = @Rep, Anio = @Anio, NroActa = @NroActa, IdJuzgado = @Juzgado,
                    IdPersona = @Persona, Dominio = @Dominio, FechaActa = @FechaActa, SumaReclamada = @SumaReclamada, GastosAdministrativos = @GastosAdministrativos, Observaciones = @Obs, Version = @Version,
                    IdLote = @Lote, MotivoArchivo = @MotivoArchivo, FechaArchivo = @FechaArchivo, NroApremio = @Apremio,
                    Eliminado = @Eliminado, IdUsuarioModif = @UsuarioModif, FechaModif = @FechaModif
                WHERE IdActa = @Id
                """, acta.Revision, [.. ps, P("@Id", acta.IdActa)]);
    }

    public List<ActaVersion> ListarVersiones(int idActa) => Leer($"""
        SELECT v.*, j.Nombre AS NombreJuzgado, {NombrePersona} AS NombrePersona
        FROM ActaVersion v
        LEFT JOIN Juzgado j ON j.IdJuzgado = v.IdJuzgado
        LEFT JOIN Persona p ON p.IdPersona = v.IdPersona
        WHERE v.IdActa = @Id ORDER BY v.NroVersion
        """, r => new ActaVersion
    {
        IdActaVersion = r.Entero("IdActaVersion"), IdActa = r.Entero("IdActa"), NroVersion = r.Entero("NroVersion"), NroActa = r.Texto("NroActa"),
        IdJuzgado = r.EnteroN("IdJuzgado"), IdPersona = r.EnteroN("IdPersona"), Dominio = r.Texto("Dominio"), FechaActa = r.Fecha("FechaActa"), SumaReclamada = r.DecimalN("SumaReclamada"),
        GastosAdministrativos = r.DecimalN("GastosAdministrativos"), Observaciones = r.Texto("Observaciones"), Motivo = r.Texto("Motivo"), IdUsuario = r.Entero("IdUsuario"),
        Fecha = r.Fecha("Fecha"), Juzgado = r.Texto("NombreJuzgado"), Persona = r.Texto("NombrePersona")
    }, P("@Id", idActa));

    public void AgregarVersion(ActaVersion v)
    {
        v.IdActaVersion = Insertar("""
            INSERT INTO ActaVersion (IdActa, NroVersion, NroActa, IdJuzgado, IdPersona, Dominio, FechaActa, SumaReclamada, GastosAdministrativos, Observaciones, Motivo, IdUsuario, Fecha)
            VALUES (@Acta, @Nro, @NroActa, @Juzgado, @Persona, @Dominio, @FechaActa, @SumaReclamada, @GastosAdministrativos, @Obs, @Motivo, @Usuario, @Fecha)
            """,
            P("@Acta", v.IdActa), P("@Nro", v.NroVersion), P("@NroActa", v.NroActa), P("@Juzgado", v.IdJuzgado), P("@Persona", v.IdPersona),
            P("@Dominio", v.Dominio),
            P("@FechaActa", v.FechaActa.Date), P("@SumaReclamada", v.SumaReclamada), P("@GastosAdministrativos", v.GastosAdministrativos), P("@Obs", v.Observaciones),
            P("@Motivo", v.Motivo), P("@Usuario", v.IdUsuario), P("@Fecha", v.Fecha));
    }

    // movimientos

    private const string SelectMovimiento = $"""
        SELECT m.*, m.xmin AS Revision, s.Nombre AS NombreSector, a.NroCausa, CONCAT(r.Codigo, ' · ', r.Nombre) AS NombreReparticion, a.Anio, a.NroActa,
               {NombrePersona} AS NombrePersona, p.NroDocumento AS Documento, a.NroApremio, a.SumaReclamada, a.GastosAdministrativos
        FROM Movimiento m
        JOIN Sector s      ON s.IdSector = m.IdSector
        JOIN Acta a        ON a.IdActa = m.IdActa
        JOIN Reparticion r ON r.IdReparticion = a.IdReparticion
        LEFT JOIN Persona p ON p.IdPersona = a.IdPersona
        """;

    private static Movimiento MapMovimiento(NpgsqlDataReader r)
    {
        var m = new Movimiento
        {
            IdMovimiento = r.Entero("IdMovimiento"), IdActa = r.Entero("IdActa"), IdSector = r.Entero("IdSector"), Fecha = r.Fecha("Fecha"),
            Detalle = r.Texto("Detalle"), Remito = r.Texto("Remito"), Observaciones = r.Texto("Observaciones"),
            Sector = r.Texto("NombreSector"), NroCausa = r.Texto("NroCausa"), Reparticion = r.Texto("NombreReparticion"), Anio = r.Entero("Anio"),
            NroActa = r.Texto("NroActa"), Persona = r.Texto("NombrePersona"), Documento = r.Texto("Documento"), NroApremio = r.Texto("NroApremio"),
            SumaReclamada = r.DecimalN("SumaReclamada"), GastosAdministrativos = r.DecimalN("GastosAdministrativos")
        };
        Auditoria(m, r);
        return m;
    }

    public Movimiento ObtenerMovimiento(int idMovimiento) => LeerUno(SelectMovimiento + " WHERE m.IdMovimiento = @Id", MapMovimiento, P("@Id", idMovimiento));

    public List<Movimiento> ListarMovimientos(int idSector) => Leer(SelectMovimiento +
        " WHERE m.IdSector = @Sector AND m.Eliminado = false AND a.Eliminado = false ORDER BY m.Fecha DESC, m.IdMovimiento DESC",
        MapMovimiento, P("@Sector", idSector));

    public List<Movimiento> ListarMovimientosDeActa(int idActa) => Leer(SelectMovimiento +
        " WHERE m.IdActa = @Acta AND m.Eliminado = false ORDER BY m.Fecha, m.IdMovimiento", MapMovimiento, P("@Acta", idActa));

    public void GuardarMovimiento(Movimiento m)
    {
        NpgsqlParameter[] ps =
        [
            P("@Acta", m.IdActa), P("@Sector", m.IdSector), P("@Fecha", m.Fecha.Date), P("@Detalle", m.Detalle),
            P("@Remito", m.Remito), P("@Obs", m.Observaciones), .. ParametrosAuditoria(m)
        ];
        if (m.IdMovimiento == 0)
            m.IdMovimiento = Insertar("""
                INSERT INTO Movimiento (IdActa, IdSector, Fecha, Detalle, Remito, Observaciones, Eliminado, IdUsuarioAlta, FechaAlta, IdUsuarioModif, FechaModif)
                VALUES (@Acta, @Sector, @Fecha, @Detalle, @Remito, @Obs, @Eliminado, @UsuarioAlta, @FechaAlta, @UsuarioModif, @FechaModif)
                """, ps);
        else
            Actualizar("""
                UPDATE Movimiento SET IdActa = @Acta, IdSector = @Sector, Fecha = @Fecha, Detalle = @Detalle, Remito = @Remito,
                    Observaciones = @Obs, Eliminado = @Eliminado, IdUsuarioModif = @UsuarioModif, FechaModif = @FechaModif
                WHERE IdMovimiento = @Id
                """, m.Revision, [.. ps, P("@Id", m.IdMovimiento)]);
    }

    public Dictionary<int, int> ContarMovimientosPorSector() => Leer("""
        SELECT m.IdSector, COUNT(*) AS Cantidad
        FROM Movimiento m JOIN Acta a ON a.IdActa = m.IdActa
        WHERE m.Eliminado = false AND a.Eliminado = false
        GROUP BY m.IdSector
        """, r => (Sector: r.Entero("IdSector"), Cantidad: r.Entero("Cantidad"))).ToDictionary(x => x.Sector, x => x.Cantidad);

    // reclamos

    private static Reclamo MapReclamo(NpgsqlDataReader r)
    {
        var x = new Reclamo
        {
            IdReclamo = r.Entero("IdReclamo"), NroCaso = r.Texto("NroCaso"), Fecha = r.Fecha("Fecha"),
            Origen = (OrigenReclamo?)r.EnteroN("Origen"), Categoria = (CategoriaReclamo?)r.EnteroN("Categoria"), Estado = (EstadoReclamo)r.Entero("Estado"), Ubicacion = r.Texto("Ubicacion"),
            Entrecalles = r.Texto("Entrecalles"), Delegacion = r.Texto("Delegacion"), Vehiculo = r.Texto("Vehiculo"), Denunciante = r.Texto("Denunciante"),
            Verificado = r.Logico("Verificado"), FechaVerificacion = r.FechaN("FechaVerificacion"), VerificadoPor = r.Texto("VerificadoPor"),
            NroActaInfraccion = r.Texto("NroActaInfraccion"), FechaNotificacion = r.FechaN("FechaNotificacion"), Observaciones = r.Texto("Observaciones")
        };
        Auditoria(x, r);
        return x;
    }

    public Reclamo ObtenerReclamo(int idReclamo) => LeerUno("SELECT *, xmin AS Revision FROM Reclamo WHERE IdReclamo = @Id", MapReclamo, P("@Id", idReclamo));

    public List<Reclamo> ListarReclamos() => Leer("SELECT *, xmin AS Revision FROM Reclamo WHERE Eliminado = false ORDER BY Fecha DESC, IdReclamo DESC", MapReclamo);

    public void GuardarReclamo(Reclamo x)
    {
        NpgsqlParameter[] ps =
        [
            P("@Caso", x.NroCaso), P("@Fecha", x.Fecha.Date), P("@Origen", (byte?)x.Origen), P("@Categoria", (byte?)x.Categoria), P("@Estado", (byte)x.Estado),
            P("@Ubicacion", x.Ubicacion), P("@Entrecalles", x.Entrecalles), P("@Delegacion", x.Delegacion), P("@Vehiculo", x.Vehiculo),
            P("@Denunciante", x.Denunciante), P("@Verificado", x.Verificado), P("@FechaVerif", x.FechaVerificacion?.Date),
            P("@VerifPor", x.VerificadoPor), P("@ActaInfr", x.NroActaInfraccion), P("@FechaNotif", x.FechaNotificacion?.Date),
            P("@Obs", x.Observaciones), .. ParametrosAuditoria(x)
        ];
        if (x.IdReclamo == 0)
            x.IdReclamo = Insertar("""
                INSERT INTO Reclamo (NroCaso, Fecha, Origen, Categoria, Estado, Ubicacion, Entrecalles, Delegacion, Vehiculo, Denunciante, Verificado,
                                     FechaVerificacion, VerificadoPor, NroActaInfraccion, FechaNotificacion, Observaciones,
                                     Eliminado, IdUsuarioAlta, FechaAlta, IdUsuarioModif, FechaModif)
                VALUES (@Caso, @Fecha, @Origen, @Categoria, @Estado, @Ubicacion, @Entrecalles, @Delegacion, @Vehiculo, @Denunciante, @Verificado,
                        @FechaVerif, @VerifPor, @ActaInfr, @FechaNotif, @Obs,
                        @Eliminado, @UsuarioAlta, @FechaAlta, @UsuarioModif, @FechaModif)
                """, ps);
        else
            Actualizar("""
                UPDATE Reclamo SET NroCaso = @Caso, Fecha = @Fecha, Origen = @Origen, Categoria = @Categoria, Estado = @Estado, Ubicacion = @Ubicacion,
                    Entrecalles = @Entrecalles, Delegacion = @Delegacion, Vehiculo = @Vehiculo, Denunciante = @Denunciante,
                    Verificado = @Verificado, FechaVerificacion = @FechaVerif, VerificadoPor = @VerifPor, NroActaInfraccion = @ActaInfr,
                    FechaNotificacion = @FechaNotif, Observaciones = @Obs, Eliminado = @Eliminado,
                    IdUsuarioModif = @UsuarioModif, FechaModif = @FechaModif
                WHERE IdReclamo = @Id
                """, x.Revision, [.. ps, P("@Id", x.IdReclamo)]);
    }

    // lotes

    private static Lote MapLote(NpgsqlDataReader r)
    {
        var x = new Lote
        {
            IdLote = r.Entero("IdLote"), NroLote = r.Texto("NroLote"), Fecha = r.Fecha("Fecha"), CantTransito = r.Entero("CantTransito"),
            CantCompetencia = r.Entero("CantCompetencia"), CantPrescriptas = r.Entero("CantPrescriptas"), Observaciones = r.Texto("Observaciones")
        };
        Auditoria(x, r);
        return x;
    }

    public Lote ObtenerLote(int idLote) => LeerUno("SELECT *, xmin AS Revision FROM Lote WHERE IdLote = @Id", MapLote, P("@Id", idLote));

    public List<Lote> ListarLotes() => Leer("SELECT *, xmin AS Revision FROM Lote WHERE Eliminado = false ORDER BY Fecha DESC, IdLote DESC", MapLote);

    public List<Acta> ListarActasDeLote(int idLote) =>
        Leer(SelectActa + " WHERE a.IdLote = @Lote ORDER BY a.FechaArchivo DESC, a.IdActa DESC", MapActa, P("@Lote", idLote));

    public void GuardarLote(Lote x)
    {
        NpgsqlParameter[] ps =
        [
            P("@Nro", x.NroLote), P("@Fecha", x.Fecha.Date), P("@Transito", x.CantTransito), P("@Competencia", x.CantCompetencia),
            P("@Prescriptas", x.CantPrescriptas), P("@Obs", x.Observaciones), .. ParametrosAuditoria(x)
        ];
        if (x.IdLote == 0)
            x.IdLote = Insertar("""
                INSERT INTO Lote (NroLote, Fecha, CantTransito, CantCompetencia, CantPrescriptas, Observaciones,
                                  Eliminado, IdUsuarioAlta, FechaAlta, IdUsuarioModif, FechaModif)
                VALUES (@Nro, @Fecha, @Transito, @Competencia, @Prescriptas, @Obs, @Eliminado, @UsuarioAlta, @FechaAlta, @UsuarioModif, @FechaModif)
                """, ps);
        else
            Actualizar("""
                UPDATE Lote SET NroLote = @Nro, Fecha = @Fecha, CantTransito = @Transito, CantCompetencia = @Competencia,
                    CantPrescriptas = @Prescriptas, Observaciones = @Obs, Eliminado = @Eliminado, IdUsuarioModif = @UsuarioModif, FechaModif = @FechaModif
                WHERE IdLote = @Id
                """, x.Revision, [.. ps, P("@Id", x.IdLote)]);
    }

    // historial


    private static EntradaHistorial MapHistorial(NpgsqlDataReader r) => new()
    {
        IdHistorial = r.Largo("IdHistorial"), Entidad = r.Texto("Entidad"), IdEntidad = r.Entero("IdEntidad"), IdSector = r.EnteroN("IdSector"),
        Accion = r.Texto("Accion"), Detalle = r.Texto("Detalle"), IdUsuario = r.Entero("IdUsuario"), Fecha = r.Fecha("Fecha"),
        Usuario = r.Texto("NombreUsuario"), Sector = r.Texto("NombreSector")
    };

    private const string SelectHistorial = """
        SELECT h.*, u.NombreUsuario, s.Nombre AS NombreSector
        FROM Historial h
        JOIN Usuario u ON u.IdUsuario = h.IdUsuario
        LEFT JOIN Sector s ON s.IdSector = h.IdSector
        """;

    public void AgregarHistorial(EntradaHistorial h)
    {
        h.IdHistorial = Convert.ToInt64(Escalar("""
            INSERT INTO Historial (Entidad, IdEntidad, IdSector, Accion, Detalle, IdUsuario, Fecha)
            VALUES (@Entidad, @IdEntidad, @Sector, @Accion, @Detalle, @Usuario, @Fecha)
            RETURNING IdHistorial
            """,
            P("@Entidad", h.Entidad), P("@IdEntidad", h.IdEntidad), P("@Sector", h.IdSector), P("@Accion", h.Accion),
            P("@Detalle", Recortar(h.Detalle, 400)), P("@Usuario", h.IdUsuario), P("@Fecha", h.Fecha)));
    }

    private static string Recortar(string s, int largo) => s == null || s.Length <= largo ? s : s[..(largo - 1)] + "…";

    public List<EntradaHistorial> ListarHistorial(string entidad, int idEntidad) => Leer(SelectHistorial +
        " WHERE h.Entidad = @Entidad AND h.IdEntidad = @Id ORDER BY h.Fecha DESC, h.IdHistorial DESC",
        MapHistorial, P("@Entidad", entidad), P("@Id", idEntidad));

    public List<EntradaHistorial> ListarUltimosCambios(IEnumerable<int> sectores, int cantidad) =>
        Leer(SelectHistorial + """
             WHERE h.IdSector = ANY (string_to_array(@Sectores, ',')::int[])
             ORDER BY h.Fecha DESC, h.IdHistorial DESC
             LIMIT @Cantidad
            """, MapHistorial, P("@Cantidad", cantidad), P("@Sectores", ListaIds(sectores)));

    // busqueda

    public List<ResultadoBusqueda> Buscar(string texto, IEnumerable<int> sectores, int? idSector = null)
    {
        using var cn = Abrir();
        // en PostgreSQL el procedimiento es una función: se la llama con SELECT
        using var cmd = new NpgsqlCommand("SELECT * FROM sp_BuscarGeneral(@Texto, @Sectores, @IdSector)", cn);
        cmd.Parameters.Add(new NpgsqlParameter("@Texto", NpgsqlDbType.Text) { Value = texto?.Trim() ?? "" });
        cmd.Parameters.Add(new NpgsqlParameter("@Sectores", NpgsqlDbType.Text) { Value = ListaIds(sectores) });
        cmd.Parameters.Add(new NpgsqlParameter("@IdSector", NpgsqlDbType.Integer) { Value = (object)idSector ?? DBNull.Value });
        using var r = cmd.ExecuteReader();
        var lista = new List<ResultadoBusqueda>();
        while (r.Read())
            lista.Add(new ResultadoBusqueda
            {
                Tipo = r.Texto("Tipo"), IdSector = r.Entero("IdSector"), Sector = r.Texto("Sector"), Id = r.Entero("Id"),
                IdActa = r.EnteroN("IdActa"), Titulo = r.Texto("Titulo"), Detalle = r.Texto("Detalle"), Fecha = r.Fecha("Fecha")
            });
        return lista;
    }
}
