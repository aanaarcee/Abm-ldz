using System.Data;
using System.Text.RegularExpressions;
using Npgsql;

namespace Oficina.Datos.Postgres;

// base común de las clases de datos: abre la conexión con PostgreSQL, ejecuta comandos y traduce los errores de la base
public abstract class ConexionPg
{
    private readonly string _cadena;

    protected ConexionPg(string cadenaConexion) => _cadena = cadenaConexion;

    protected NpgsqlConnection Abrir()
    {
        var cn = new NpgsqlConnection(_cadena);
        cn.Open();
        return cn;
    }

    protected static NpgsqlParameter P(string nombre, object valor) => new(nombre, valor ?? DBNull.Value);

    protected List<T> Leer<T>(string sql, Func<NpgsqlDataReader, T> mapear, params NpgsqlParameter[] parametros)
    {
        using var cn = Abrir();
        using var cmd = Comando(cn, null, sql, parametros);
        using var r = cmd.ExecuteReader();
        var lista = new List<T>();
        while (r.Read()) lista.Add(mapear(r));
        return lista;
    }

    protected T LeerUno<T>(string sql, Func<NpgsqlDataReader, T> mapear, params NpgsqlParameter[] parametros) where T : class
        => Leer(sql, mapear, parametros).FirstOrDefault();

    protected object Escalar(string sql, params NpgsqlParameter[] parametros)
    {
        using var cn = Abrir();
        using var cmd = Comando(cn, null, sql, parametros);
        return Traducir(() => cmd.ExecuteScalar());
    }

    protected int Ejecutar(string sql, params NpgsqlParameter[] parametros)
    {
        using var cn = Abrir();
        using var cmd = Comando(cn, null, sql, parametros);
        return Traducir(() => cmd.ExecuteNonQuery());
    }

    // da de alta y devuelve el Id nuevo (el SQL tiene que terminar en el INSERT).
    protected int Insertar(string sqlInsert, params NpgsqlParameter[] parametros)
    {
        string tabla = Regex.Match(sqlInsert, @"INSERT\s+INTO\s+(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
        return Convert.ToInt32(Escalar(sqlInsert + " RETURNING Id" + tabla, parametros));
    }

    protected static NpgsqlCommand Comando(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, params NpgsqlParameter[] parametros)
    {
        var cmd = new NpgsqlCommand(sql, cn, tx) { CommandType = CommandType.Text };
        cmd.Parameters.AddRange(parametros);
        return cmd;
    }

    // errores sobre cosas repetidas
    protected static T Traducir<T>(Func<T> accion)
    {
        try { return accion(); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // postgreSQL guarda los nombres de las restricciones en minúscula
            string m = ex.ConstraintName ?? ex.Message;
            bool Es(string nombre) => m.Contains(nombre, StringComparison.OrdinalIgnoreCase);
            string texto = Es("UQ_Acta_Clave") ? "Ya existe un acta con ese N° de causa, repartición y año."
                : Es("UQ_Acta_NroApremio") ? "Ya existe un acta con ese N° de apremio."
                : Es("UQ_Reclamo") ? "Ya existe un reclamo con ese N° de caso."
                : Es("UQ_Lote") ? "Ya existe un lote con ese número."
                : Es("UQ_Usuario") ? "Ya existe una cuenta con ese nombre."
                : Es("UQ_Rol") ? "Ya existe un rol con ese nombre."
                : Es("UQ_Sector") ? "Ya existe un sector con ese nombre o clave."
                : Es("UQ_Persona") ? "Ya existe una persona con ese documento."
                : Es("UQ_Reparticion") ? "Ya existe una repartición con ese número o ese nombre."
                : Es("UQ_Juzgado") ? "Ya existe un juzgado con ese nombre."
                : "Ya existe un registro con esos datos.";
            throw new InvalidOperationException(texto, ex);
        }
        // un remito pertenece a una sola acta (EX_Movimiento_RemitoUnaActa)
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            throw new InvalidOperationException("Ese remito ya está anotado en otra acta. Un remito pertenece a una sola acta.", ex);
        }
        // reglas de la base que la aplicación también valida antes: si se llega acá, se avisa igual con un texto claro
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            string m = ex.ConstraintName ?? ex.Message;
            bool Es(string nombre) => m.Contains(nombre, StringComparison.OrdinalIgnoreCase);
            string texto = Es("CK_Persona_Documento") ? "El documento va solo con números: DNI de 7 u 8, CUIT de 11."
                : Es("CK_Acta_Archivo") ? "Para archivar el acta hacen falta el lote, el motivo y la fecha."
                : Es("CK_Acta_MotivoArchivo") ? "El motivo de archivo es Sentencia definitiva o Pagada."
                : Es("Montos") ? "Los montos no pueden ser negativos."
                : Es("CK_Reclamo_Verificacion") ? "Un reclamo verificado necesita la fecha de verificación y el inspector."
                : Es("CK_Reclamo_Categoria") ? "La categoría del reclamo no es válida."
                : "Los datos no cumplen una regla de la base: " + ex.MessageText;
            throw new InvalidOperationException(texto, ex);
        }
    }
}

// forma de leer los datos para que sea mas comodo
internal static class LectorExtensiones
{
    public static string Texto(this NpgsqlDataReader r, string c) => r[c] is DBNull ? null : Convert.ToString(r[c]);
    public static int Entero(this NpgsqlDataReader r, string c) => Convert.ToInt32(r[c]);
    public static int? EnteroN(this NpgsqlDataReader r, string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c]);
    public static long Largo(this NpgsqlDataReader r, string c) => Convert.ToInt64(r[c]);
    public static decimal? DecimalN(this NpgsqlDataReader r, string c) => r[c] is DBNull ? null : Convert.ToDecimal(r[c]);
    // las columnas date vuelven como DateOnly en Npgsql; las timestamp, como DateTime
    private static DateTime AFecha(object v) => v is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : Convert.ToDateTime(v);
    public static DateTime Fecha(this NpgsqlDataReader r, string c) => AFecha(r[c]);
    public static DateTime? FechaN(this NpgsqlDataReader r, string c) => r[c] is DBNull ? null : AFecha(r[c]);
    public static bool Logico(this NpgsqlDataReader r, string c) => Convert.ToBoolean(r[c]);
    public static byte[] Bytes(this NpgsqlDataReader r, string c) => (byte[])r[c];
}
