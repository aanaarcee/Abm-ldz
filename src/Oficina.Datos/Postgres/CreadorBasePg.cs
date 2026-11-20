using System.Text.RegularExpressions;
using Npgsql;

namespace Oficina.Datos.Postgres;


// forma de crear la base de datos en PostgreSQL si no existe, y de aplicarle las migraciones

public static class CreadorBasePg
{
    // PostgreSQL pasa a minúscula los nombres sin comillas: la base se llama oficinaregistros
    public const string NombreBase = "oficinaregistros";

    // Completa la base de datos si la cadena no la indica.
    public static string Completar(string cadenaConexion)
    {
        var b = new NpgsqlConnectionStringBuilder(cadenaConexion);
        if (string.IsNullOrWhiteSpace(b.Database)) b.Database = NombreBase;
        return b.ConnectionString;
    }

    // Con un servidor externo la aplicación NO crea nada por su cuenta: solo comprueba que la base esté lista
    // (que exista, que tenga las tablas y que tenga usuarios). Si no, avisa con un error claro.
    // Con crear = true (la app abierta con --crear-base) sí crea la base, las tablas y las funciones, o las actualiza.
    // Devuelve verdadero si se tuvo que crear de 0 la base.
    public static bool AsegurarBase(string cadenaConexion, bool crear = false)
    {
        var b = new NpgsqlConnectionStringBuilder(Completar(cadenaConexion));
        string nombre = b.Database;

        if (crear) CrearBaseSiFalta(b, nombre);

        NpgsqlConnection db;
        try { db = new NpgsqlConnection(b.ConnectionString); db.Open(); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            throw new InvalidOperationException(
                $"La base '{nombre}' no existe en el servidor {b.Host}. Revisá conexion.local.txt o creala abriendo la aplicación con --crear-base.", ex);
        }
        using (db)
        {
            using (var tablas = new NpgsqlCommand("SELECT to_regclass('public.sector') IS NOT NULL", db))
            {
                bool hayTablas = (bool)tablas.ExecuteScalar();
                if (!hayTablas)
                {
                    if (!crear)
                        throw new InvalidOperationException(
                            $"La base '{nombre}' del servidor {b.Host} está vacía (no tiene las tablas). Si es la primera vez, abrí la aplicación con --crear-base.");
                    // la estructura de partida, todos los cambios posteriores y al final las funciones, que pueden usar
                    // columnas agregadas por las migraciones (por ejemplo Acta.NroApremio)
                    var lotes = Lotes(LeerScript("OficinaRegistros.sql")).ToList();
                    Ejecutar(db, string.Join(Environment.NewLine + "GO" + Environment.NewLine, lotes.Where(l => !EsFuncion(l))));
                    Migrar(db);
                    Ejecutar(db, string.Join(Environment.NewLine + "GO" + Environment.NewLine, lotes.Where(EsFuncion)));
                    return true;
                }
            }
            // La base está en desarrollo y la estructura cambia: las migraciones pendientes se aplican solas.
            // Con --crear-base además se vuelven a crear siempre las funciones de búsqueda.
            Actualizar(db, crear);
            if (crear) return false;

            using var estado = new NpgsqlCommand("SELECT to_regproc('sp_buscargeneral') IS NOT NULL, (SELECT COUNT(*) FROM usuario)", db);
            using var r = estado.ExecuteReader();
            r.Read();
            if (!r.GetBoolean(0))
                throw new InvalidOperationException($"A la base '{nombre}' le falta la función de búsqueda. Abrí la aplicación con --crear-base para actualizarla.");
            if (r.GetInt64(1) == 0)
                throw new InvalidOperationException($"La base '{nombre}' no tiene usuarios: no se cargan datos de ejemplo automáticamente. Abrí la aplicación con --crear-base si es una base nueva.");
            return false;
        }
    }

    private static void CrearBaseSiFalta(NpgsqlConnectionStringBuilder b, string nombre)
    {
        // se entra a "postgres" (la que siempre existe) para ver si está y crearla si falta
        var admin = new NpgsqlConnectionStringBuilder(b.ConnectionString) { Database = "postgres", Pooling = false };
        using var cn = new NpgsqlConnection(admin.ConnectionString);
        cn.Open();
        using var existe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @Base", cn);
        existe.Parameters.AddWithValue("@Base", nombre);
        if (existe.ExecuteScalar() != null) return;
        // el nombre no se puede pasar como parámetro: se escapa como identificador
        using var crear = new NpgsqlCommand($"CREATE DATABASE \"{nombre.Replace("\"", "\"\"")}\"", cn);
        crear.ExecuteNonQuery();
    }

    // base que ya existía: se aplican las migraciones pendientes y, si hubo alguna (o si se pidió), se vuelven a
    // crear las funciones de búsqueda, que siempre salen del script principal (OficinaRegistros.sql)
    private static void Actualizar(NpgsqlConnection cn, bool siempreFunciones)
    {
        int aplicadas = Migrar(cn);
        if (aplicadas == 0 && !siempreFunciones) return;
        var funciones = Lotes(LeerScript("OficinaRegistros.sql")).Where(EsFuncion);
        Ejecutar(cn, string.Join(Environment.NewLine + "GO" + Environment.NewLine, funciones));
    }

    private static bool EsFuncion(string lote) => lote.Contains("CREATE OR REPLACE FUNCTION");

    // Migraciones: sql/Migraciones/NNN_nombre.sql (se aplican en orden de nombre, cada una una sola vez).
    // El servidor anota las aplicadas en la tabla EsquemaMigracion. Cada migración corre en una transacción:
    // si falla, no queda nada a medias. Las separa el mismo "GO" que el script principal.
    // OficinaRegistros.sql es la estructura de partida y no se edita: todo cambio de tablas va en una migración nueva.
    // Ojo: una migración ya aplicada no se vuelve a correr aunque se edite el archivo; para corregirla, otra migración.
    private static int Migrar(NpgsqlConnection cn)
    {
        var migraciones = typeof(CreadorBasePg).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("MigracionesPg.")).Order(StringComparer.Ordinal).ToList();

        // un candado en el servidor evita que dos programas abiertos a la vez apliquen la misma migración
        const long candado = 7351001;
        using (var c = new NpgsqlCommand($"SELECT pg_advisory_lock({candado})", cn)) c.ExecuteNonQuery();
        try
        {
            using (var c = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS EsquemaMigracion (Nombre VARCHAR(150) PRIMARY KEY, Aplicada TIMESTAMP(0) NOT NULL DEFAULT LOCALTIMESTAMP(0))", cn))
                c.ExecuteNonQuery();

            var hechas = new HashSet<string>();
            using (var c = new NpgsqlCommand("SELECT Nombre FROM EsquemaMigracion", cn))
            using (var r = c.ExecuteReader())
                while (r.Read()) hechas.Add(r.GetString(0));

            int aplicadas = 0;
            foreach (var recurso in migraciones.Where(m => !hechas.Contains(m)))
            {
                using var tx = cn.BeginTransaction();
                try
                {
                    foreach (var lote in Lotes(LeerScript(recurso)))
                    {
                        using var cmd = new NpgsqlCommand(lote, cn, tx) { CommandTimeout = 120 };
                        cmd.ExecuteNonQuery();
                    }
                    using var anotar = new NpgsqlCommand("INSERT INTO EsquemaMigracion (Nombre) VALUES (@N)", cn, tx);
                    anotar.Parameters.AddWithValue("@N", recurso);
                    anotar.ExecuteNonQuery();
                    tx.Commit();
                    aplicadas++;
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    throw new InvalidOperationException(
                        $"Falló la migración {recurso.Replace("MigracionesPg.", "")} y no se aplicó ningún cambio de esa migración: {ex.Message}", ex);
                }
            }
            return aplicadas;
        }
        finally
        {
            using var c = new NpgsqlCommand($"SELECT pg_advisory_unlock({candado})", cn);
            c.ExecuteNonQuery();
        }
    }

    private static IEnumerable<string> Lotes(string script) =>
        Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private static void Ejecutar(NpgsqlConnection cn, string script)
    {
        foreach (var lote in Lotes(script))
        {
            using var cmd = new NpgsqlCommand(lote, cn) { CommandTimeout = 120 };
            cmd.ExecuteNonQuery();
        }
    }

    private static string LeerScript(string recurso)
    {
        using var s = typeof(CreadorBasePg).Assembly.GetManifestResourceStream(recurso)
                      ?? throw new InvalidOperationException($"Falta el script {recurso} dentro del programa.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
