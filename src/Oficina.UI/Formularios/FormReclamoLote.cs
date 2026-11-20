using Oficina.Entidades;
using Oficina.Negocio;

namespace Oficina.UI.Formularios;

/// <summary>Alta o modificación de un aviso de auto abandonado.</summary>
internal class FormReclamo : FormularioDatos
{
    private readonly ServicioRegistros _servicio;
    private readonly Sector _sector;
    private readonly Reclamo _existente;
    private readonly TextBox _caso, _ubicacion, _entrecalles, _delegacion, _vehiculo, _denunciante, _verificadoPor, _actaInfraccion, _obs;
    private readonly DateTimePicker _fecha, _fechaVerif, _fechaNotificacion;
    private readonly ComboBox _origen, _categoria, _estado;
    private readonly CheckBox _verificado;

    private record Opcion<T>(T Valor, string Texto)
    {
        public override string ToString() => Texto;
    }

    public FormReclamo(ServicioRegistros servicio, Sector sector, Reclamo existente)
        : base(existente == null ? "Nuevo reclamo" : $"Modificar reclamo {existente.NroCaso}")
    {
        _servicio = servicio;
        _sector = sector;
        _existente = existente;

        Seccion("Aviso de auto abandonado");
        _caso = Campo("N° de caso", Texto(20));
        _fecha = Campo("Fecha de ingreso", Fecha());
        _origen = Campo("Entró por", Lista(new[]
        {
            new Opcion<OrigenReclamo?>(null, "(sin dato)"), new Opcion<OrigenReclamo?>(OrigenReclamo.Transito, "Tránsito"),
            new Opcion<OrigenReclamo?>(OrigenReclamo.GestionDeReclamos, "Gestión de reclamos (página del municipio)")
        }));
        _categoria = Campo("Categoría", Lista(new[]
        {
            new Opcion<CategoriaReclamo?>(null, "(sin dato)"), new Opcion<CategoriaReclamo?>(CategoriaReclamo.Transito, "Tránsito"),
            new Opcion<CategoriaReclamo?>(CategoriaReclamo.Demanda, "Demanda"), new Opcion<CategoriaReclamo?>(CategoriaReclamo.Municipales, "Municipales")
        }));
        _estado = Campo("Estado", Lista(new[]
        {
            new Opcion<EstadoReclamo>(EstadoReclamo.EnAbordaje, "En abordaje"), new Opcion<EstadoReclamo>(EstadoReclamo.Verificando, "Verificando"),
            new Opcion<EstadoReclamo>(EstadoReclamo.Solucionado, "Solucionado")
        }));
        _ubicacion = Campo("Dirección", Texto(150));
        _entrecalles = Campo("Entrecalles", Texto(150));
        _delegacion = Campo("Delegación", Texto(80));
        _vehiculo = Campo("Vehículo", Texto(100));
        _denunciante = Campo("Denunciante", Texto(120));
        Nota("Vehículo: marca, color y patente si se sabe. Denunciante: quién avisó y cómo contactarlo, o «Anónimo».");
        Seccion("Verificación");
        _verificado = Campo("", new CheckBox { Text = "Alguien fue a ver el auto", AutoSize = true });
        _fechaVerif = Campo("Primera verificación", Fecha());
        _verificadoPor = Campo("Inspector", Texto(80));
        _actaInfraccion = Campo("Acta de infracción", Texto(20));
        _fechaNotificacion = Campo("Cédula de notificación", Fecha(opcional: true));
        Seccion("Otros datos");
        _obs = Campo("Observaciones", Texto(500, varias: true));

        _verificado.CheckedChanged += (_, _) => _fechaVerif.Enabled = _verificadoPor.Enabled = _verificado.Checked;
        _fechaNotificacion.Checked = false;
        if (existente == null)
            _caso.Text = $"RVP-{DateTime.Today.Year}-";
        else
        {
            _caso.Text = existente.NroCaso;
            _fecha.Value = existente.Fecha;
            Elegir<Opcion<OrigenReclamo?>>(_origen, o => o.Valor == existente.Origen);
            Elegir<Opcion<CategoriaReclamo?>>(_categoria, o => o.Valor == existente.Categoria);
            Elegir<Opcion<EstadoReclamo>>(_estado, o => o.Valor == existente.Estado);
            _ubicacion.Text = existente.Ubicacion;
            _entrecalles.Text = existente.Entrecalles;
            _delegacion.Text = existente.Delegacion;
            _vehiculo.Text = existente.Vehiculo;
            _denunciante.Text = existente.Denunciante;
            _verificado.Checked = existente.Verificado;
            if (existente.FechaVerificacion.HasValue) _fechaVerif.Value = existente.FechaVerificacion.Value;
            _verificadoPor.Text = existente.VerificadoPor;
            _actaInfraccion.Text = existente.NroActaInfraccion;
            if (existente.FechaNotificacion.HasValue)
            {
                _fechaNotificacion.Value = existente.FechaNotificacion.Value;
                _fechaNotificacion.Checked = true;
            }
            _obs.Text = existente.Observaciones;
        }
        _fechaVerif.Enabled = _verificadoPor.Enabled = _verificado.Checked;
    }

    protected override bool Guardar()
    {
        var r = new Reclamo
        {
            IdReclamo = _existente?.IdReclamo ?? 0, NroCaso = _caso.Text, Fecha = _fecha.Value.Date,
            Origen = ((Opcion<OrigenReclamo?>)_origen.SelectedItem).Valor, Categoria = ((Opcion<CategoriaReclamo?>)_categoria.SelectedItem).Valor, Estado = ((Opcion<EstadoReclamo>)_estado.SelectedItem).Valor,
            Ubicacion = _ubicacion.Text, Entrecalles = _entrecalles.Text, Delegacion = _delegacion.Text,
            Vehiculo = _vehiculo.Text, Denunciante = _denunciante.Text,
            Verificado = _verificado.Checked, FechaVerificacion = _verificado.Checked ? _fechaVerif.Value.Date : null,
            VerificadoPor = _verificadoPor.Text, NroActaInfraccion = _actaInfraccion.Text,
            FechaNotificacion = _fechaNotificacion.Checked ? _fechaNotificacion.Value.Date : null, Observaciones = _obs.Text
        };
        if (_existente == null) _servicio.CargarReclamo(_sector.IdSector, r);
        else _servicio.ModificarReclamo(_sector.IdSector, r);
        return true;
    }
}

/// <summary>Alta o modificación de un lote.</summary>
internal class FormLote : FormularioDatos
{
    private readonly ServicioRegistros _servicio;
    private readonly Sector _sector;
    private readonly Lote _existente;
    private readonly TextBox _nro, _obs;
    private readonly DateTimePicker _fecha;
    private readonly NumericUpDown _transito, _competencia, _prescriptas;
    private readonly Label _total;

    public FormLote(ServicioRegistros servicio, Sector sector, Lote existente)
        : base(existente == null ? "Nuevo lote" : $"Modificar lote {existente.NroLote}")
    {
        _servicio = servicio;
        _sector = sector;
        _existente = existente;

        Seccion("Lote");
        _nro = Campo("N° de lote", Texto(20));
        _fecha = Campo("Fecha", Fecha());
        Seccion("Cantidad de actas");
        _transito = Campo("Tránsito", new NumericUpDown { Maximum = 100_000 });
        _competencia = Campo("Competencia", new NumericUpDown { Maximum = 100_000 });
        _prescriptas = Campo("Prescriptas", new NumericUpDown { Maximum = 100_000 });
        _total = Campo("Total", Estilo.Etiqueta("0", Estilo.Negrita));
        Seccion("Otros datos");
        _obs = Campo("Observaciones", Texto(500, varias: true));

        foreach (var n in new[] { _transito, _competencia, _prescriptas })
            n.ValueChanged += (_, _) => _total.Text = (_transito.Value + _competencia.Value + _prescriptas.Value).ToString("N0", Formatos.Cultura);

        if (existente == null) _nro.Text = $"L-{DateTime.Today.Year}-";
        else
        {
            _nro.Text = existente.NroLote;
            _fecha.Value = existente.Fecha;
            _transito.Value = existente.CantTransito;
            _competencia.Value = existente.CantCompetencia;
            _prescriptas.Value = existente.CantPrescriptas;
            _obs.Text = existente.Observaciones;
        }
    }

    protected override bool Guardar()
    {
        var l = new Lote
        {
            IdLote = _existente?.IdLote ?? 0, NroLote = _nro.Text, Fecha = _fecha.Value.Date, CantTransito = (int)_transito.Value,
            CantCompetencia = (int)_competencia.Value, CantPrescriptas = (int)_prescriptas.Value, Observaciones = _obs.Text
        };
        if (_existente == null) _servicio.CargarLote(_sector.IdSector, l);
        else _servicio.ModificarLote(_sector.IdSector, l);
        return true;
    }
}
