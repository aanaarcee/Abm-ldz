using System.Data;
using System.Text.RegularExpressions;
using Npgsql;

namespace Oficina.Datos.Postgres;

// datos para la conexion con sql (la idea es poder probar de que forma podrian conectarse de forma remota a la base de datos. podriamos probar subiendola a un servidor)
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

    // Da de alta y devuelve el Id nuevo (el SQL tiene que terminar en el INSERT).
    // Todas las tablas tienen su clave con el nombre Id + tabla (Acta -> IdActa), así que alcanza con RETURNING.
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
            // PostgreSQL guarda los nombres de las restricciones en minúscula
            string m = ex.ConstraintName ?? ex.Message;
            bool Es(string nombre) => m.Contains(nombre, StringComparison.OrdinalIgnoreCase);
            string texto = Es("UQ_Acta_Clave") ? "Ya existe un acta con ese N° de causa, repartición y año."
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
