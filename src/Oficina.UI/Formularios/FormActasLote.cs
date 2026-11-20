using Oficina.Entidades;
using Oficina.Negocio;

namespace Oficina.UI.Formularios;

/// <summary>
/// Las actas archivadas en un lote. Un acta termina cuando se archiva en el lote, por sentencia definitiva o porque
/// se pagó (P12), y queda ahí entre 5 y 10 años (P13). Desde acá Lotes archiva actas; el Administrador puede sacarlas.
/// </summary>
internal class FormActasLote : Form
{
    private readonly INavegador _nav;
    private readonly ServicioRegistros _servicio;
    private readonly Sector _sector;
    private readonly Lote _lote;
    private readonly DataGridView _grilla;
    private readonly Label _resumen;

    private readonly TextBox _causa;
    private readonly ComboBox _reparticion, _motivo;
    private readonly NumericUpDown _anio;
    private readonly DateTimePicker _fecha;

    private record Opcion(MotivoArchivo Valor, string Texto)
    {
        public override string ToString() => Texto;
    }

    public FormActasLote(INavegador nav, ServicioRegistros servicio, Sector sector, Lote lote)
    {
        _nav = nav;
        _servicio = servicio;
        _sector = sector;
        _lote = lote;
        bool puedeArchivar = nav.Sesion.Puede(sector.IdSector, Accion.Modificar);

        Text = $"Actas del lote {lote.NroLote}";
        Font = Estilo.Letra;
        BackColor = Estilo.Panel;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Estilo.Px(980), Estilo.Px(620));
        MinimumSize = new Size(Estilo.Px(720), Estilo.Px(440));
        ShowInTaskbar = false;
        Padding = new Padding(Estilo.Px(16));

        var titulo = Estilo.Etiqueta($"Lote {lote.NroLote}", Estilo.Titulo, Estilo.ColorSector(sector.IdSector));
        var ayuda = Estilo.Etiqueta("Un acta archivada en el lote está terminada: no admite movimientos ni cambios. " +
                                    "Solo el Administrador puede sacarla si se archivó por error.", color: Estilo.TextoSuave);
        ayuda.MaximumSize = new Size(Estilo.Px(900), 0);
        _resumen = Estilo.Etiqueta("", Estilo.Negrita);

        _grilla = Estilo.Grilla();
        Estilo.ColorearGrilla(_grilla, sector.IdSector);
        _grilla.Columna(nameof(Acta.Clave), "Acta (causa · repartición · año)", 300);
        _grilla.Columna(nameof(Acta.NroActa), "N° acta", 80);
        _grilla.Columna(nameof(Acta.Persona), "Persona");
        _grilla.Columna(nameof(Acta.TextoMotivoArchivo), "Motivo", 150);
        _grilla.Columna(nameof(Acta.FechaArchivo), "Archivada", 95, "dd/MM/yyyy");
        _grilla.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) VerFicha(); };

        // ---------- Archivar un acta (solo si el sector puede modificar) ----------
        var archivar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, Estilo.Px(10), 0, 0) };
        _causa = new TextBox { Width = Estilo.Px(110), MaxLength = 20 };
        _reparticion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Estilo.Px(220) };
        _reparticion.Items.AddRange(servicio.Reparticiones().ToArray<object>());
        if (_reparticion.Items.Count > 0) _reparticion.SelectedIndex = 0;
        _anio = new NumericUpDown { Minimum = 1990, Maximum = DateTime.Today.Year + 1, Value = DateTime.Today.Year, Width = Estilo.Px(70) };
        _motivo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Estilo.Px(170) };
        _motivo.Items.AddRange([new Opcion(MotivoArchivo.SentenciaDefinitiva, "Sentencia definitiva"), new Opcion(MotivoArchivo.Pagada, "Pagada")]);
        _motivo.SelectedIndex = 0;
        _fecha = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = Estilo.Px(110), MaxDate = DateTime.Today.AddDays(1).AddTicks(-1) };
        foreach (var (etiqueta, control) in new (string, Control)[] { ("N° de causa", _causa), ("Repartición", _reparticion), ("Año", _anio), ("Motivo", _motivo), ("Fecha", _fecha) })
        {
            var l = Estilo.Etiqueta(etiqueta, color: Estilo.TextoSuave);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(Estilo.Px(6), Estilo.Px(6), Estilo.Px(2), 0);
            archivar.Controls.Add(l);
            archivar.Controls.Add(control);
        }
        archivar.Controls.Add(Estilo.Boton("Archivar acta", (_, _) => Archivar(), principal: true));

        var botones = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, Estilo.Px(10), 0, 0) };
        var cerrar = Estilo.Boton("Cerrar", (_, _) => Close());
        botones.Controls.Add(cerrar);
        botones.Controls.Add(Estilo.Boton("Ver ficha", (_, _) => VerFicha()));
        if (nav.Sesion.EsAdministrador) botones.Controls.Add(Estilo.Boton("Sacar del lote", (_, _) => Quitar()));

        var tabla = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.Controls.Add(titulo);
        tabla.Controls.Add(ayuda);
        tabla.Controls.Add(_resumen);
        tabla.Controls.Add(Estilo.Marco(_grilla));
        if (puedeArchivar) tabla.Controls.Add(archivar);
        tabla.Controls.Add(botones);
        Controls.Add(tabla);
        CancelButton = cerrar;

        Cargar();
    }

    private void Cargar()
    {
        var actas = _servicio.ActasDelLote(_sector.IdSector, _lote.IdLote);
        _grilla.DataSource = actas;
        _resumen.Text = actas.Count == 0
            ? "Todavía no hay actas archivadas en este lote."
            : $"{actas.Count} acta{(actas.Count == 1 ? "" : "s")} archivada{(actas.Count == 1 ? "" : "s")} · " +
              $"{actas.Count(a => a.MotivoArchivo == MotivoArchivo.SentenciaDefinitiva)} por sentencia definitiva, " +
              $"{actas.Count(a => a.MotivoArchivo == MotivoArchivo.Pagada)} pagadas";
    }

    private void Archivar()
    {
        try
        {
            Cursor.Current = Cursors.WaitCursor;
            var acta = _servicio.ArchivarActa(_sector.IdSector, _lote.IdLote, _causa.Text, (_reparticion.SelectedItem as Reparticion)?.IdReparticion ?? 0,
                (int)_anio.Value, (_motivo.SelectedItem as Opcion)?.Valor, _fecha.Value.Date);
            _causa.Clear();
            _causa.Focus();
            Cargar();
            Dialogo.Aviso($"Se archivó en el lote {_lote.NroLote}:\n{acta.Clave}");
        }
        catch (ExcepcionNegocio ex) { Dialogo.MostrarError(ex); }
        finally { Cursor.Current = Cursors.Default; }
    }

    private void Quitar()
    {
        if (_grilla.CurrentRow?.DataBoundItem is not Acta a) { Dialogo.Aviso("Primero elija un acta de la lista."); return; }
        var motivo = Dialogo.PedirTexto(this, "Sacar del lote",
            $"Se va a sacar del lote {_lote.NroLote}:\n{a.Clave}\n\nVuelve a quedar abierta y se puede modificar.\n\nMotivo:");
        if (motivo == null) return;
        try
        {
            _servicio.QuitarDelLote(_sector.IdSector, a.IdActa, motivo);
            Cargar();
        }
        catch (ExcepcionNegocio ex) { Dialogo.MostrarError(ex); }
    }

    private void VerFicha()
    {
        if (_grilla.CurrentRow?.DataBoundItem is not Acta a) return;
        try { _nav.AbrirActa(a.IdActa); }
        catch (ExcepcionNegocio ex) { Dialogo.MostrarError(ex); }
    }
}
