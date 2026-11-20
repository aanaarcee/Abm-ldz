using System.Reflection;
using Oficina.Entidades;

namespace Oficina.Datos.Memoria;

// Implementación en memoria: sirve para probar la aplicación sin base de datos (se pierde al cerrar).
// Devuelve copias para que la interfaz no cambie los datos guardados sin pasar por Guardar.
public class DatosEnMemoria : IDatosSeguridad, IDatosRegistros
{
    private readonly List<Sector> _sectores = new();
    private readonly List<Rol> _roles = new();
    private readonly List<Usuario> _usuarios = new();
    private readonly List<Permiso> _permisos = new();
    private readonly List<Reparticion> _reparticiones = new();
    private readonly List<Juzgado> _juzgados = new();
    private readonly List<Persona> _personas = new();
    private readonly List<Acta> _actas = new();
    private readonly List<ActaVersion> _versiones = new();
    private readonly List<Movimiento> _movimientos = new();
    private readonly List<Reclamo> _reclamos = new();
    private readonly List<Lote> _lotes = new();
    private readonly List<EntradaHistorial> _historial = new();
    private readonly object _candado = new();

    private static readonly MethodInfo Clonador = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
    private static T Copia<T>(T o) where T : class => o == null ? null : (T)Clonador.Invoke(o, null);

    private static int SiguienteId<T>(List<T> lista, Func<T, int> id) => lista.Count == 0 ? 1 : lista.Max(id) + 1;

    private static void Reemplazar<T>(List<T> lista, Func<T, bool> esEste, T nuevo)
    {
        int i = lista.FindIndex(x => esEste(x));
        if (i < 0) throw new InvalidOperationException("No se encontró el registro a modificar.");
        // igual que xmin en PostgreSQL: si alguien lo guardó desde que se leyó, no se acepta
        if (nuevo is RegistroAuditado n && lista[i] is RegistroAuditado viejo)
        {
            if (n.Revision != 0 && n.Revision != viejo.Revision) throw new CambioAjenoException();
            n.Revision = viejo.Revision + 1;
        }
        lista[i] = nuevo;
    }

    // alta: la copia guardada arranca en la revisión 1
    private static void Alta<T>(List<T> lista, T nuevo) where T : RegistroAuditado
    {
        var c = Copia(nuevo);
        c.Revision = 1;
        lista.Add(c);
    }

    // En memoria no hay forma de deshacer: solo se asegura que nadie más escriba en el medio
    public void EnTransaccion(Action accion)
    {
        lock (_candado) accion();
    }

    // seguridad

    public bool HayUsuarios() { lock (_candado) return _usuarios.Count > 0; }

    public List<Sector> ListarSectores() { lock (_candado) return _sectores.OrderBy(s => s.IdSector).Select(Copia).ToList(); }

    public void GuardarSector(Sector sector)
    {
        lock (_candado)
        {
            if (sector.IdSector == 0)
            {
                sector.IdSector = SiguienteId(_sectores, s => s.IdSector);
                _sectores.Add(Copia(sector));
            }
            else Reemplazar(_sectores, s => s.IdSector == sector.IdSector, Copia(sector));
        }
    }

    public List<Rol> ListarRoles() { lock (_candado) return _roles.OrderBy(r => r.IdRol).Select(Copia).ToList(); }

    public Rol ObtenerRol(int idRol) { lock (_candado) return Copia(_roles.FirstOrDefault(r => r.IdRol == idRol)); }

    public void GuardarRol(Rol rol)
    {
        lock (_candado)
        {
            if (rol.IdRol == 0)
            {
                rol.IdRol = SiguienteId(_roles, r => r.IdRol);
                _roles.Add(Copia(rol));
            }
            else Reemplazar(_roles, r => r.IdRol == rol.IdRol, Copia(rol));
        }
    }

    public List<Usuario> ListarUsuarios() { lock (_candado) return _usuarios.OrderBy(u => u.NombreUsuario).Select(Copia).ToList(); }

    public Usuario ObtenerUsuario(string nombreUsuario)
    {
        lock (_candado) return Copia(_usuarios.FirstOrDefault(u => string.Equals(u.NombreUsuario, nombreUsuario, StringComparison.OrdinalIgnoreCase)));
    }

    public void GuardarUsuario(Usuario usuario)
    {
        lock (_candado)
        {
            if (usuario.IdUsuario == 0)
            {
                usuario.IdUsuario = SiguienteId(_usuarios, u => u.IdUsuario);
                _usuarios.Add(Copia(usuario));
            }
            else Reemplazar(_usuarios, u => u.IdUsuario == usuario.IdUsuario, Copia(usuario));
        }
    }

    public List<Permiso> ListarPermisos(int idRol) { lock (_candado) return _permisos.Where(p => p.IdRol == idRol).Select(Copia).ToList(); }

    public void GuardarPermisos(int idRol, IEnumerable<Permiso> permisos)
    {
        lock (_candado)
        {
            _permisos.RemoveAll(p => p.IdRol == idRol);
            foreach (var p in permisos.Where(p => p.Acciones != Accion.Ninguna))
                _permisos.Add(new Permiso { IdRol = idRol, IdSector = p.IdSector, Acciones = p.Acciones });
        }
    }

    // listas y personas

    public List<Reparticion> ListarReparticiones() { lock (_candado) return _reparticiones.OrderBy(r => r.Codigo).Select(Copia).ToList(); }

    public void GuardarReparticion(Reparticion reparticion)
    {
        lock (_candado)
        {
            // en SQL lo frenan las restricciones UQ_Reparticion y UQ_Reparticion_Codigo
            if (_reparticiones.Any(r => r.IdReparticion != reparticion.IdReparticion
                                        && (r.Codigo == reparticion.Codigo || TextoBusqueda.Iguales(r.Nombre, reparticion.Nombre))))
                throw new InvalidOperationException("Ya existe una repartición con ese número o ese nombre.");
            if (reparticion.IdReparticion == 0)
            {
                reparticion.IdReparticion = SiguienteId(_reparticiones, r => r.IdReparticion);
                _reparticiones.Add(Copia(reparticion));
            }
            else Reemplazar(_reparticiones, r => r.IdReparticion == reparticion.IdReparticion, Copia(reparticion));
        }
    }

    public List<Juzgado> ListarJuzgados() { lock (_candado) return _juzgados.OrderBy(j => j.Nombre).Select(Copia).ToList(); }

    public void GuardarJuzgado(Juzgado juzgado)
    {
        lock (_candado)
        {
            if (juzgado.IdJuzgado == 0)
            {
                juzgado.IdJuzgado = SiguienteId(_juzgados, j => j.IdJuzgado);
                _juzgados.Add(Copia(juzgado));
            }
            else Reemplazar(_juzgados, j => j.IdJuzgado == juzgado.IdJuzgado, Copia(juzgado));
        }
    }

    public Persona ObtenerPersona(int idPersona) { lock (_candado) return Copia(_personas.FirstOrDefault(p => p.IdPersona == idPersona)); }

    public Persona BuscarPersonaPorDocumento(string tipoDocumento, string nroDocumento)
    {
        lock (_candado) return Copia(_personas.FirstOrDefault(p => p.TipoDocumento == tipoDocumento && p.NroDocumento == nroDocumento));
    }

    public void GuardarPersona(Persona persona)
    {
        lock (_candado)
        {
            if (persona.IdPersona == 0)
            {
                persona.IdPersona = SiguienteId(_personas, p => p.IdPersona);
                _personas.Add(Copia(persona));
            }
            else Reemplazar(_personas, p => p.IdPersona == persona.IdPersona, Copia(persona));
        }
    }

    // actas

    private Acta Completar(Acta a)
    {
        if (a == null) return null;
        var c = Copia(a);
        c.Reparticion = _reparticiones.FirstOrDefault(r => r.IdReparticion == a.IdReparticion)?.ToString();
        c.Juzgado = _juzgados.FirstOrDefault(j => j.IdJuzgado == a.IdJuzgado)?.Nombre;
        var p = _personas.FirstOrDefault(x => x.IdPersona == a.IdPersona);
        c.Persona = p?.NombreCompleto;
        c.Documento = p?.NroDocumento;
        return c;
    }

    public Acta ObtenerActa(int idActa) { lock (_candado) return Completar(_actas.FirstOrDefault(a => a.IdActa == idActa)); }

    public Acta BuscarActa(string nroCausa, int idReparticion, int anio)
    {
        lock (_candado)
            return Completar(_actas.FirstOrDefault(a => TextoBusqueda.Iguales(a.NroCausa, nroCausa) && a.IdReparticion == idReparticion && a.Anio == anio));
    }

    public void GuardarActa(Acta acta)
    {
        lock (_candado)
        {
            bool repetida = _actas.Any(a => a.IdActa != acta.IdActa && TextoBusqueda.Iguales(a.NroCausa, acta.NroCausa)
                                            && a.IdReparticion == acta.IdReparticion && a.Anio == acta.Anio);
            if (repetida) throw new InvalidOperationException("Ya existe un acta con ese N° de causa, repartición y año.");
            if (acta.IdActa == 0)
            {
                acta.IdActa = SiguienteId(_actas, a => a.IdActa);
                Alta(_actas, acta);
            }
            else Reemplazar(_actas, a => a.IdActa == acta.IdActa, Copia(acta));
        }
    }

    public List<ActaVersion> ListarVersiones(int idActa)
    {
        lock (_candado)
            return _versiones.Where(v => v.IdActa == idActa).OrderBy(v => v.NroVersion).Select(v =>
            {
                var c = Copia(v);
                c.Juzgado = _juzgados.FirstOrDefault(j => j.IdJuzgado == v.IdJuzgado)?.Nombre;
                c.Persona = _personas.FirstOrDefault(p => p.IdPersona == v.IdPersona)?.NombreCompleto;
                return c;
            }).ToList();
    }

    public void AgregarVersion(ActaVersion version)
    {
        lock (_candado)
        {
            version.IdActaVersion = SiguienteId(_versiones, v => v.IdActaVersion);
            _versiones.Add(Copia(version));
        }
    }

    // movimientos

    private Movimiento Completar(Movimiento m)
    {
        if (m == null) return null;
        var c = Copia(m);
        var a = Completar(_actas.First(x => x.IdActa == m.IdActa));
        c.Sector = _sectores.FirstOrDefault(s => s.IdSector == m.IdSector)?.Nombre;
        c.NroCausa = a.NroCausa; c.Reparticion = a.Reparticion; c.Anio = a.Anio; c.NroActa = a.NroActa;
        c.Persona = a.Persona; c.Documento = a.Documento; c.NroApremio = a.NroApremio; c.SumaReclamada = a.SumaReclamada; c.GastosAdministrativos = a.GastosAdministrativos;
        return c;
    }

    public Movimiento ObtenerMovimiento(int idMovimiento) { lock (_candado) return Completar(_movimientos.FirstOrDefault(m => m.IdMovimiento == idMovimiento)); }

    public List<Movimiento> ListarMovimientos(int idSector)
    {
        lock (_candado)
            return _movimientos.Where(m => m.IdSector == idSector && !m.Eliminado && !ActaEliminada(m.IdActa))
                .OrderByDescending(m => m.Fecha).ThenByDescending(m => m.IdMovimiento).Select(Completar).ToList();
    }

    public List<Movimiento> ListarMovimientosDeActa(int idActa)
    {
        lock (_candado)
            return _movimientos.Where(m => m.IdActa == idActa && !m.Eliminado)
                .OrderBy(m => m.Fecha).ThenBy(m => m.IdMovimiento).Select(Completar).ToList();
    }

    private bool ActaEliminada(int idActa) => _actas.FirstOrDefault(a => a.IdActa == idActa)?.Eliminado ?? true;

    public void GuardarMovimiento(Movimiento movimiento)
    {
        lock (_candado)
        {
            if (movimiento.IdMovimiento == 0)
            {
                movimiento.IdMovimiento = SiguienteId(_movimientos, m => m.IdMovimiento);
                Alta(_movimientos, movimiento);
            }
            else Reemplazar(_movimientos, m => m.IdMovimiento == movimiento.IdMovimiento, Copia(movimiento));
        }
    }

    public Dictionary<int, int> ContarMovimientosPorSector()
    {
        lock (_candado)
            return _movimientos.Where(m => !m.Eliminado && !ActaEliminada(m.IdActa))
                .GroupBy(m => m.IdSector).ToDictionary(g => g.Key, g => g.Count());
    }

    // reclamos y lotes

    public Reclamo ObtenerReclamo(int idReclamo) { lock (_candado) return Copia(_reclamos.FirstOrDefault(r => r.IdReclamo == idReclamo)); }

    public List<Reclamo> ListarReclamos()
    {
        lock (_candado) return _reclamos.Where(r => !r.Eliminado).OrderByDescending(r => r.Fecha).ThenByDescending(r => r.IdReclamo).Select(Copia).ToList();
    }

    public void GuardarReclamo(Reclamo reclamo)
    {
        lock (_candado)
        {
            if (_reclamos.Any(r => r.IdReclamo != reclamo.IdReclamo && TextoBusqueda.Iguales(r.NroCaso, reclamo.NroCaso)))
                throw new InvalidOperationException("Ya existe un reclamo con ese N° de caso.");
            if (reclamo.IdReclamo == 0)
            {
                reclamo.IdReclamo = SiguienteId(_reclamos, r => r.IdReclamo);
                Alta(_reclamos, reclamo);
            }
            else Reemplazar(_reclamos, r => r.IdReclamo == reclamo.IdReclamo, Copia(reclamo));
        }
    }

    public Lote ObtenerLote(int idLote) { lock (_candado) return Copia(_lotes.FirstOrDefault(l => l.IdLote == idLote)); }

    public List<Lote> ListarLotes()
    {
        lock (_candado) return _lotes.Where(l => !l.Eliminado).OrderByDescending(l => l.Fecha).ThenByDescending(l => l.IdLote).Select(Copia).ToList();
    }

    public List<Acta> ListarActasDeLote(int idLote)
    {
        lock (_candado)
            return _actas.Where(a => a.IdLote == idLote).OrderByDescending(a => a.FechaArchivo).ThenByDescending(a => a.IdActa).Select(Completar).ToList();
    }

    public void GuardarLote(Lote lote)
    {
        lock (_candado)
        {
            if (_lotes.Any(l => l.IdLote != lote.IdLote && TextoBusqueda.Iguales(l.NroLote, lote.NroLote)))
                throw new InvalidOperationException("Ya existe un lote con ese número.");
            if (lote.IdLote == 0)
            {
                lote.IdLote = SiguienteId(_lotes, l => l.IdLote);
                Alta(_lotes, lote);
            }
            else Reemplazar(_lotes, l => l.IdLote == lote.IdLote, Copia(lote));
        }
    }

    // historial

    public void AgregarHistorial(EntradaHistorial entrada)
    {
        lock (_candado)
        {
            entrada.IdHistorial = _historial.Count + 1;
            _historial.Add(Copia(entrada));
        }
    }

    private EntradaHistorial Completar(EntradaHistorial h)
    {
        var c = Copia(h);
        c.Usuario = _usuarios.FirstOrDefault(u => u.IdUsuario == h.IdUsuario)?.NombreUsuario;
        c.Sector = _sectores.FirstOrDefault(s => s.IdSector == h.IdSector)?.Nombre;
        return c;
    }

    public List<EntradaHistorial> ListarHistorial(string entidad, int idEntidad)
    {
        lock (_candado)
            return _historial.Where(h => h.Entidad == entidad && h.IdEntidad == idEntidad)
                .OrderByDescending(h => h.Fecha).ThenByDescending(h => h.IdHistorial).Select(Completar).ToList();
    }

    public List<EntradaHistorial> ListarUltimosCambios(IEnumerable<int> sectores, int cantidad)
    {
        var ids = sectores.ToHashSet();
        lock (_candado)
            return _historial.Where(h => h.IdSector.HasValue && ids.Contains(h.IdSector.Value))
                .OrderByDescending(h => h.Fecha).ThenByDescending(h => h.IdHistorial).Take(cantidad).Select(Completar).ToList();
    }

    // busqueda

    public List<ResultadoBusqueda> Buscar(string texto, IEnumerable<int> sectores, int? idSector = null)
    {
        var ids = sectores.Where(s => idSector == null || s == idSector).ToHashSet();
        var t = texto?.Trim() ?? "";
        var lista = new List<ResultadoBusqueda>();
        lock (_candado)
        {
            foreach (var m in _movimientos.Where(m => !m.Eliminado && ids.Contains(m.IdSector) && !ActaEliminada(m.IdActa)))
            {
                var a = Completar(_actas.First(x => x.IdActa == m.IdActa));
                var p = _personas.FirstOrDefault(x => x.IdPersona == a.IdPersona);
                var codigo = _reparticiones.FirstOrDefault(r => r.IdReparticion == a.IdReparticion)?.Codigo.ToString();
                bool coincide = a.Anio.ToString() == t || codigo == t || TextoBusqueda.AlgunoContiene(t, a.NroCausa, a.NroActa, a.Reparticion,
                    a.Dominio, p?.Apellido, p?.Nombre, p?.NroDocumento, a.Observaciones, m.Detalle, m.Remito, m.Observaciones, a.NroApremio);
                if (!coincide) continue;
                lista.Add(new ResultadoBusqueda
                {
                    Tipo = "Acta", IdSector = m.IdSector, Sector = _sectores.First(s => s.IdSector == m.IdSector).Nombre,
                    Id = m.IdMovimiento, IdActa = a.IdActa, Titulo = $"Causa {a.NroCausa} · {a.Reparticion} · {a.Anio}",
                    Detalle = $"Acta {a.NroActa}" + (a.NroApremio != null ? $" · Apremio {a.NroApremio}" : "") + (a.Persona != null ? $" · {a.Persona}" : "") + (a.Dominio != null ? $" · Dominio {a.Dominio}" : "") + $" · {m.Detalle}"
                              + (string.IsNullOrEmpty(m.Remito) ? "" : $" · Remito {m.Remito}"),
                    Fecha = m.Fecha
                });
            }
            foreach (var s in _sectores.Where(s => ids.Contains(s.IdSector)))
            {
                if (s.Tipo == TipoSector.Reclamos)
                    lista.AddRange(_reclamos.Where(r => !r.Eliminado && TextoBusqueda.AlgunoContiene(t, r.NroCaso, r.Ubicacion, r.Entrecalles,
                            r.Delegacion, r.Vehiculo, r.Denunciante, r.VerificadoPor, r.NroActaInfraccion, r.Observaciones))
                        .Select(r => new ResultadoBusqueda
                        {
                            Tipo = "Reclamo", IdSector = s.IdSector, Sector = s.Nombre, Id = r.IdReclamo, Titulo = $"Caso {r.NroCaso}",
                            Detalle = r.Ubicacion + (string.IsNullOrEmpty(r.Delegacion) ? "" : $" · {r.Delegacion}")
                                      + (string.IsNullOrEmpty(r.Vehiculo) ? "" : $" · {r.Vehiculo}") + $" · {r.TextoEstado}",
                            Fecha = r.Fecha
                        }));
                if (s.Tipo == TipoSector.Lotes)
                    lista.AddRange(_lotes.Where(l => !l.Eliminado && TextoBusqueda.AlgunoContiene(t, l.NroLote, l.Observaciones))
                        .Select(l => new ResultadoBusqueda
                        {
                            Tipo = "Lote", IdSector = s.IdSector, Sector = s.Nombre, Id = l.IdLote, Titulo = $"Lote {l.NroLote}",
                            Detalle = $"Tránsito {l.CantTransito} · Competencia {l.CantCompetencia} · Prescriptas {l.CantPrescriptas}",
                            Fecha = l.Fecha
                        }));
            }
        }
        return lista.OrderByDescending(r => r.Fecha).Take(300).ToList();
    }
}
