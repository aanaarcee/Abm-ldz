using Oficina.Datos;
using Oficina.Datos.Memoria;
using Oficina.Datos.Postgres;

namespace Oficina.Negocio;


// la capa negocio se encarga de coordinar la interfaz con la capa de datos, se podria llamar capa logica, pero no se es raro xd
// la interfaz solo habla con esta clase y con los servicios, nunca con la capa de datos.

public class Aplicacion
{
    private readonly IDatosSeguridad _seguridad;
    private readonly IDatosRegistros _registros;
    private readonly Func<bool> _crearBase;
    private readonly bool _cargarEjemplo;

    public string Origen { get; }
    // modo prueba (--memoria): nada se guarda, la interfaz lo avisa siempre
    public bool EsPrueba => _seguridad is DatosEnMemoria;
    // verdadero si en Preparar() se creó la base porque no existía
    public bool BaseCreada { get; private set; }

    private Aplicacion(IDatosSeguridad seguridad, IDatosRegistros registros, string origen, Func<bool> crearBase = null, bool cargarEjemplo = true)
    {
        _cargarEjemplo = cargarEjemplo;
        _seguridad = seguridad;
        _registros = registros;
        Origen = origen;
        _crearBase = crearBase;
    }

    // crea la aplicación con PostgreSQL. Cadena: Host=...;Username=...;Password=... (si no dice Database, se usa oficinaregistros).
    // por defecto solo se comprueba que la base esté lista (y se aplican las migraciones pendientes); NO se cargan datos de ejemplo.
    // con crearBase = true (la app abierta con --crear-base) se crea o actualiza la base y se cargan los datos de ejemplo si está vacía.
    public static Aplicacion ConPostgres(string cadenaConexion, bool crearBase = false)
    {
        var cadena = CreadorBasePg.Completar(cadenaConexion);
        return new(new PgDatosSeguridad(cadena), new PgDatosRegistros(cadena), "PostgreSQL", () => CreadorBasePg.AsegurarBase(cadena, crearBase), crearBase);
    }

    public static Aplicacion EnMemoria()
    {
        var datos = new DatosEnMemoria();
        return new(datos, datos, "memoria (prueba, no se guarda)");
    }

 // crea la base si no existe
    public bool Preparar()
    {
        if (_crearBase != null) BaseCreada = _crearBase();
        return _cargarEjemplo && DatosDeEjemplo.CargarSiHaceFalta(_seguridad, _registros);
    }

    public Sesion Ingresar(string usuario, string clave) => Sesion.Ingresar(_seguridad, usuario, clave);
    public Sesion Recargar(Sesion sesion) => Sesion.Recargar(_seguridad, sesion);

    public ServicioRegistros Registros(Sesion s) => new(s, _registros);
    public ServicioBusqueda Busqueda(Sesion s) => new(s, _registros);
    public ExportadorCsv Exportador(Sesion s) => new(s, _registros);
    public ServicioImportacion Importador(Sesion s) => new(s, _registros);
    public ServicioAdministracion Administracion(Sesion s) => new(s, _seguridad, _registros);
}
