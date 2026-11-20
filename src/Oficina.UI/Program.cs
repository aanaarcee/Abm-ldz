using Oficina.Negocio;
using Oficina.UI.Formularios;

namespace Oficina.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Dialogo.MostrarError(e.Exception);

        // La base es PostgreSQL. La cadena sale de conexion.local.txt (cada PC la suya, no se sube a git) o de conexion.txt.
        // --crear-base: crea la base si no existe y carga datos de ejemplo si está vacía. --memoria: prueba sin base.
        Aplicacion app;
        if (args.Contains("--memoria")) app = Aplicacion.EnMemoria();
        else
        {
            var cadena = LeerCadena();
            if (cadena == null)
            {
                MessageBox.Show("Falta la conexión a la base de datos.\n\nCree el archivo conexion.local.txt al lado del programa con una línea como:\n\n" +
                                "Host=servidor;Port=5432;Database=oficinaregistros;Username=usuario;Password=clave",
                                "Registros de Oficina", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            app = Aplicacion.ConPostgres(cadena, args.Contains("--crear-base"));
        }

        try
        {
            Cursor.Current = Cursors.WaitCursor;
            if (app.Preparar())
                Dialogo.Aviso((app.BaseCreada ? "No existía la base: se creó" : "La base estaba vacía") +
                              " y se cargaron los datos de ejemplo.\n\n" +
                              $"Cuentas: admin / {DatosDeEjemplo.ClaveAdministrador}, y una por sector " +
                              $"(entrada, seguimiento, sala, sentencias, apremio, lotes, reclamos, estadisticas) / {DatosDeEjemplo.ClaveSectores}.\n\n" +
                              "Cambie estas contraseñas en Configuración → Cuentas antes de cargar datos reales.");
        }
        catch (Exception ex)
        {
            var r = MessageBox.Show($"No se pudo conectar con la base de datos ({app.Origen}).\n\n{ex.Message}\n\n" +
                                    "Si trabaja desde afuera de la oficina, revise que la VPN (Tailscale) esté conectada.\n\n" +
                                    "¿Abrir en modo prueba? Los datos quedan en memoria y se pierden al cerrar.",
                                    "Registros de Oficina", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            app = Aplicacion.EnMemoria();
            app.Preparar();
        }
        finally { Cursor.Current = Cursors.Default; }

        while (true)
        {
            Sesion sesion;
            using (var login = new FormLogin(app))
            {
                if (login.ShowDialog() != DialogResult.OK) return;
                sesion = login.Sesion;
            }
            using var principal = new FormPrincipal(app, sesion);
            principal.ShowDialog();
            if (!principal.CambiarDeCuenta) return;
        }
    }

    // lee la cadena de conexion: primero conexion.local.txt (no se sube a git, puede tener contraseñas),
    // si no existe conexion.txt. Si ninguno tiene nada, devuelve null.
    private static string LeerCadena()
    {
        foreach (var archivo in new[] { "conexion.local.txt", "conexion.txt" })
        {
            var ruta = Path.Combine(AppContext.BaseDirectory, archivo);
            var texto = File.Exists(ruta) ? File.ReadAllText(ruta).Trim() : "";
            if (texto != "") return texto;
        }
        return null;
    }
}
