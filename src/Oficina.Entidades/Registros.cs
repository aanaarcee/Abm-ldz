namespace Oficina.Entidades;

// la reparticion es el tipo de acta, con el numero que usa la oficina (62 fotomultas, 25 retencion de licencia...)
public class Reparticion
{
    public int IdReparticion { get; set; }
    public int Codigo { get; set; }
    public string Nombre { get; set; }
    public override string ToString() => $"{Codigo} · {Nombre}";
}

public class Juzgado
{
    public int IdJuzgado { get; set; }
    public string Nombre { get; set; }
    public override string ToString() => Nombre;
}

public class Persona
{
    public int IdPersona { get; set; }
    public string Apellido { get; set; }
    public string Nombre { get; set; }
    public string TipoDocumento { get; set; } = "DNI";
    public string NroDocumento { get; set; }

    public string NombreCompleto => string.IsNullOrWhiteSpace(Nombre) ? Apellido : $"{Apellido}, {Nombre}";
    public override string ToString() => NombreCompleto;
}

// campos comunes a actas, movimientos, reclamos y lotes
public abstract class RegistroAuditado
{
    public bool Eliminado { get; set; }
    public int IdUsuarioAlta { get; set; }
    public DateTime FechaAlta { get; set; }
    public int? IdUsuarioModif { get; set; }
    public DateTime? FechaModif { get; set; }

    // marca de la base que cambia cada vez que se guarda la fila (xmin en PostgreSQL). Sirve para saber si otra
    // persona lo modificó desde que se abrió: al guardar con una revisión vieja, la base no lo acepta. 0 = no controlar.
    public uint Revision { get; set; }

    public DateTime UltimaModificacion => FechaModif ?? FechaAlta;
}


// registro de act y como se define

public class Acta : RegistroAuditado
{
    public int IdActa { get; set; }
    public string NroCausa { get; set; }
    public int IdReparticion { get; set; }
    public int Anio { get; set; }
    public string NroActa { get; set; }
    public int? IdJuzgado { get; set; }
    public int? IdPersona { get; set; }
    public string Dominio { get; set; }
    public DateTime FechaActa { get; set; }
    public decimal? SumaReclamada { get; set; }          // lo que se intima a pagar
    public decimal? GastosAdministrativos { get; set; }  // lo que se agrega a cada infraccion
    public string Observaciones { get; set; }
    public int Version { get; set; } = 1;

    // archivo: con lote, el acta está terminada (P12, P13). Los tres van juntos o ninguno.
    public int? IdLote { get; set; }
    public MotivoArchivo? MotivoArchivo { get; set; }
    public DateTime? FechaArchivo { get; set; }
    public string NroApremio { get; set; }               // número propio que le da Apremio (P29)

    public bool Archivada => IdLote.HasValue;
    public string TextoMotivoArchivo => MotivoArchivo switch
    {
        Entidades.MotivoArchivo.SentenciaDefinitiva => "Sentencia definitiva",
        Entidades.MotivoArchivo.Pagada => "Pagada",
        _ => null
    };

    // datos para mostrar, completados por la capa de datos
    public string Reparticion { get; set; }
    public string Juzgado { get; set; }
    public string Persona { get; set; }
    public string Documento { get; set; }

    public string Clave => $"Causa {NroCausa} · {Reparticion} · {Anio}";

    public Acta Copiar() => (Acta)MemberwiseClone();
}

// version guardada del acta, para ver cambios y auditar
public class ActaVersion
{
    public int IdActaVersion { get; set; }
    public int IdActa { get; set; }
    public int NroVersion { get; set; }
    public string NroActa { get; set; }
    public int? IdJuzgado { get; set; }
    public int? IdPersona { get; set; }
    public string Dominio { get; set; }
    public DateTime FechaActa { get; set; }
    public decimal? SumaReclamada { get; set; }
    public decimal? GastosAdministrativos { get; set; }
    public string Observaciones { get; set; }
    public string Motivo { get; set; }
    public int IdUsuario { get; set; }
    public DateTime Fecha { get; set; }

    public string Juzgado { get; set; }
    public string Persona { get; set; }

    public static ActaVersion Desde(Acta a, int nroVersion, string motivo, int idUsuario, DateTime fecha) => new()
    {
        IdActa = a.IdActa, NroVersion = nroVersion, NroActa = a.NroActa, IdJuzgado = a.IdJuzgado, IdPersona = a.IdPersona, Dominio = a.Dominio,
        FechaActa = a.FechaActa, SumaReclamada = a.SumaReclamada, GastosAdministrativos = a.GastosAdministrativos, Observaciones = a.Observaciones,
        Motivo = motivo, IdUsuario = idUsuario, Fecha = fecha
    };
}

// lo que un sector anota de cada acta (forma de auditar los cambios)
public class Movimiento : RegistroAuditado
{
    public int IdMovimiento { get; set; }
    public int IdActa { get; set; }
    public int IdSector { get; set; }
    public DateTime Fecha { get; set; }
    public string Detalle { get; set; }
    public string Remito { get; set; }
    public string Observaciones { get; set; }

    // datos del acta para mostrar en la grilla del sector
    public string Sector { get; set; }
    public string NroCausa { get; set; }
    public string Reparticion { get; set; }
    public int Anio { get; set; }
    public string NroActa { get; set; }
    public string Persona { get; set; }
    public string Documento { get; set; }
    public string NroApremio { get; set; }
    public decimal? SumaReclamada { get; set; }
    public decimal? GastosAdministrativos { get; set; }
}

public class Lote : RegistroAuditado
{
    public int IdLote { get; set; }
    public string NroLote { get; set; }
    public DateTime Fecha { get; set; }
    public int CantTransito { get; set; }
    public int CantCompetencia { get; set; }
    public int CantPrescriptas { get; set; }
    public string Observaciones { get; set; }

    public int Total => CantTransito + CantCompetencia + CantPrescriptas;
}

// por donde entra un reclamo (los numeros son los que se guardan en la base)
public enum OrigenReclamo : byte { Transito = 1, GestionDeReclamos = 2 }

public enum EstadoReclamo : byte { EnAbordaje = 1, Verificando = 2, Solucionado = 3 }

// de qué tipo es el reclamo (P20)
public enum CategoriaReclamo : byte { Transito = 1, Demanda = 2, Municipales = 3 }

// por qué se archivó un acta: no hay otra forma de terminar (P12)
public enum MotivoArchivo : byte { SentenciaDefinitiva = 1, Pagada = 2 }

// avisos de los reclamos de los vecinos, que no son actas ni movimientos
public class Reclamo : RegistroAuditado
{
    public int IdReclamo { get; set; }
    public string NroCaso { get; set; }
    public DateTime Fecha { get; set; }
    public OrigenReclamo? Origen { get; set; }
    public CategoriaReclamo? Categoria { get; set; }
    public EstadoReclamo Estado { get; set; } = EstadoReclamo.EnAbordaje;
    public string Ubicacion { get; set; }
    public string Entrecalles { get; set; }
    public string Delegacion { get; set; }
    public string Vehiculo { get; set; }
    public string Denunciante { get; set; }
    public bool Verificado { get; set; }
    public DateTime? FechaVerificacion { get; set; }   // primera verificacion: desde ahi corre el plazo
    public string VerificadoPor { get; set; }          // inspector
    public string NroActaInfraccion { get; set; }
    public DateTime? FechaNotificacion { get; set; }   // cedula de notificacion
    public string Observaciones { get; set; }

    public string TextoOrigen => Origen switch
    {
        OrigenReclamo.Transito => "Tránsito",
        OrigenReclamo.GestionDeReclamos => "Gestión de reclamos",
        _ => null
    };

    public string TextoCategoria => Categoria switch
    {
        CategoriaReclamo.Transito => "Tránsito",
        CategoriaReclamo.Demanda => "Demanda",
        CategoriaReclamo.Municipales => "Municipales",
        _ => null
    };

    public string TextoEstado => Estado switch
    {
        EstadoReclamo.Verificando => "Verificando",
        EstadoReclamo.Solucionado => "Solucionado",
        _ => "En abordaje"
    };
}

public class EntradaHistorial
{
    public long IdHistorial { get; set; }
    public string Entidad { get; set; }
    public int IdEntidad { get; set; }
    public int? IdSector { get; set; }
    public string Accion { get; set; }
    public string Detalle { get; set; }
    public int IdUsuario { get; set; }
    public DateTime Fecha { get; set; }

    public string Usuario { get; set; }
    public string Sector { get; set; }
}

// fila de la busqueda general
public class ResultadoBusqueda
{
    public string Tipo { get; set; }          // acta, Reclamo, Lote
    public int IdSector { get; set; }
    public string Sector { get; set; }
    public int Id { get; set; }               // IdMovimiento, IdReclamo o IdLote
    public int? IdActa { get; set; }
    public string Titulo { get; set; }
    public string Detalle { get; set; }
    public DateTime Fecha { get; set; }
}
