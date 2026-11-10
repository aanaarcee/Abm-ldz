using Oficina.Negocio;

namespace Oficina.UI.Formularios;

/// <summary>
/// Importar una planilla (CSV o Excel) a un sector. Muestra primero una vista previa de lo que se va a cargar,
/// con las filas que tienen errores, y recién cuando se confirma se guarda. Al final deja guardar un informe de errores.
/// </summary>
internal class FormImportar : Form
{
    private readonly ServicioImportacion _servicio;
    private readonly VistaPreviaImportacion _vp;
    private readonly DataGridView _grilla;
    private readonly Label _resumen;
    private readonly Label _titulo2;
    private readonly ProgressBar _progreso;
    private readonly Button _importar;
    private readonly Button _informe;
    private readonly Button _cerrar;
    private ResultadoImportacion _resultado;
    private bool _trabajando;

    public FormImportar(ServicioImportacion servicio, VistaPreviaImportacion vp)
    {
        _servicio = servicio;
        _vp = vp;
        Text = $"Importar a {vp.Sector.Nombre}";
        Font = Estilo.Letra;
        BackColor = Estilo.Panel;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Estilo.Px(1020), Estilo.Px(650));
        MinimumSize = new Size(Estilo.Px(760), Estilo.Px(460));
        ShowInTaskbar = false;
        Padding = new Padding(Estilo.Px(16));

        var titulo = Estilo.Etiqueta($"Importar a {vp.Sector.Nombre}", Estilo.Titulo, Estilo.ColorSector(vp.Sector.IdSector));
        var archivo = Estilo.Etiqueta($"Archivo: {Path.GetFileName(vp.Archivo)}", color: Estilo.TextoSuave);
        string ignoradas = vp.ColumnasIgnoradas.Count > 0
            ? $"Columnas que no se cargan (no se reconocen): {string.Join(", ", vp.ColumnasIgnoradas)}." : "";
        var ayuda = Estilo.Etiqueta(
            "Las fechas del archivo se respetan: las filas quedan ordenadas por su fecha, no por el día en que se cargan. " +
            "Nada se guarda hasta que confirme." + (ignoradas == "" ? "" : "\n" + ignoradas), color: Estilo.TextoSuave);
        ayuda.MaximumSize = new Size(Estilo.Px(940), 0);

        _resumen = Estilo.Etiqueta("", Estilo.Negrita);
        _resumen.MaximumSize = new Size(Estilo.Px(940), 0);
        _titulo2 = Estilo.Etiqueta("Vista previa", Estilo.Subtitulo);

        _grilla = Estilo.Grilla();
        Estilo.ColorearGrilla(_grilla, vp.Sector.IdSector);
        _grilla.Columna(nameof(FilaImportacion.Numero), "Fila", 55);
        _grilla.Columna(nameof(FilaImportacion.Estado), "Estado", 70);
        _grilla.Columna(nameof(FilaImportacion.Resumen), "Qué se carga", 380);
        _grilla.Columna(nameof(FilaImportacion.Mensaje), "Aviso / error");
        _grilla.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grilla.Rows[e.RowIndex].DataBoundItem is not FilaImportacion f) return;
            string col = _grilla.Columns[e.ColumnIndex].Name;
            if (col == nameof(FilaImportacion.Estado))
            {
                e.CellStyle.ForeColor = f.Valida ? Estilo.Positivo : Estilo.Peligro;
                e.CellStyle.Font = Estilo.Negrita;
            }
            else if (col == nameof(FilaImportacion.Mensaje) && !f.Valida) e.CellStyle.ForeColor = Estilo.Peligro;
        };

        _progreso = new ProgressBar { Dock = DockStyle.Fill, Visible = false, Height = Estilo.Px(14), Margin = new Padding(0, Estilo.Px(8), 0, 0) };

        _importar = Estilo.Boton("", (_, _) => Importar(), principal: true);
        _informe = Estilo.Boton("Guardar informe de errores", (_, _) => GuardarInforme());
        _cerrar = Estilo.Boton("Cerrar", (_, _) => DialogResult = _resultado is { Importadas: > 0 } ? DialogResult.OK : DialogResult.Cancel);
        var botones = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, Estilo.Px(10), 0, 0) };
        botones.Controls.AddRange([_cerrar, _importar, _informe]);

        var tabla = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        for (int i = 0; i < 4; i++) tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (Control c in new Control[] { titulo, archivo, ayuda, _resumen, _titulo2 }) tabla.Controls.Add(c);
        tabla.Controls.Add(Estilo.Marco(_grilla));
        tabla.Controls.Add(_progreso);
        tabla.Controls.Add(botones);
        Controls.Add(tabla);

        CancelButton = _cerrar;
        FormClosing += (_, e) => { if (_trabajando) e.Cancel = true; };   // no se cierra a mitad de la carga
        MostrarVistaPrevia();
    }

    private void MostrarVistaPrevia()
    {
        _grilla.DataSource = _vp.Filas;
        string nuevas = _vp.Sector.Tipo == Entidades.TipoSector.Actas && _vp.ActasNuevas > 0 ? $" Se crearán {_vp.ActasNuevas} actas nuevas." : "";
        _resumen.Text = $"{_vp.Filas.Count} filas en el archivo: {_vp.Listas} listas para importar y {_vp.ConError} con errores " +
                        $"(esas no se cargan).{nuevas}";
        _resumen.ForeColor = _vp.ConError > 0 ? Estilo.Pendiente : Estilo.Positivo;
        _importar.Text = $"Importar {_vp.Listas} filas";
        _importar.Enabled = _vp.Listas > 0;
        _informe.Enabled = _vp.ConError > 0;
    }

    private async void Importar()
    {
        if (!Dialogo.Confirmar($"Se van a cargar {_vp.Listas} filas en «{_vp.Sector.Nombre}».\n" +
                               "Quedan registradas en el historial con su usuario.\n\n¿Continuar?")) return;
        _trabajando = true;
        _importar.Enabled = _informe.Enabled = _cerrar.Enabled = false;
        _progreso.Visible = true;
        _progreso.Maximum = Math.Max(_vp.Listas, 1);
        _progreso.Value = 0;
        Cursor = Cursors.WaitCursor;
        try
        {
            _resultado = await Task.Run(() => _servicio.Importar(_vp, (hechas, total) =>
            {
                if (IsHandleCreated) BeginInvoke(() => { _progreso.Maximum = Math.Max(total, 1); _progreso.Value = Math.Min(hechas, _progreso.Maximum); });
            }));
        }
        catch (Exception ex)
        {
            Dialogo.MostrarError(ex);
            _trabajando = false;
            _cerrar.Enabled = true;
            MostrarVistaPrevia();
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
            _progreso.Visible = false;
        }
        _trabajando = false;
        _cerrar.Enabled = true;

        var r = _resultado;
        string nuevas = _vp.Sector.Tipo == Entidades.TipoSector.Actas ? $" ({r.ActasNuevas} actas nuevas)" : "";
        _resumen.Text = $"Listo: se importaron {r.Importadas} filas{nuevas}." +
                        (r.Errores.Count > 0 ? $" No se cargaron {r.Errores.Count} filas: guarde el informe de errores, corríjalas y vuelva a importar solo esas." : "");
        _resumen.ForeColor = r.Errores.Count > 0 ? Estilo.Pendiente : Estilo.Positivo;
        _titulo2.Text = r.Errores.Count > 0 ? "Filas que no se cargaron" : "Todo se cargó";
        _grilla.DataSource = r.Errores;
        _importar.Text = "Ya importado";
        _importar.Enabled = false;        // para no cargar dos veces lo mismo
        _informe.Enabled = r.Errores.Count > 0;
        _cerrar.Focus();
    }

    private void GuardarInforme()
    {
        var errores = _resultado?.Errores ?? _vp.Filas.Where(f => !f.Valida).ToList();
        using var d = new SaveFileDialog
        {
            Title = "Guardar informe de errores", Filter = "Planilla CSV (*.csv)|*.csv",
            InitialDirectory = ExportadorCsv.CarpetaSugerida(), FileName = $"Errores de importación {_vp.Sector.Nombre} {DateTime.Now:yyyy-MM-dd HHmm}.csv"
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _servicio.GuardarInforme(_vp, errores, d.FileName);
            Dialogo.Aviso($"Se guardó el informe con {errores.Count} filas en:\n{d.FileName}\n\nTiene el número de fila, el motivo y los datos originales para corregir y volver a importar.");
        }
        catch (ExcepcionNegocio ex) { Dialogo.MostrarError(ex); }
    }
}
