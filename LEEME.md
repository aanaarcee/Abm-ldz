# Registros de Oficina · prototipo de escritorio

Aplicación de Windows Forms en C# (.NET 10) con PostgreSQL, en cuatro capas, para reemplazar las planillas de Google Sheets de cada sector.

## Cómo se arma

| Capa | Proyecto | Qué tiene |
|---|---|---|
| Entidades | `src/Oficina.Entidades` | Clases del dominio: Sector, Rol, Usuario, Permiso, Acta, ActaVersion, Movimiento, Reclamo, Lote, historial. |
| Datos | `src/Oficina.Datos` | Interfaces `IDatosSeguridad` e `IDatosRegistros`, con dos implementaciones: PostgreSQL (`Postgres/`, Npgsql) y en memoria (`Memoria/`, para probar sin base). `CreadorBasePg` crea la base y aplica las migraciones de `sql/Migraciones`. |
| Negocio | `src/Oficina.Negocio` | Ingreso con contraseña (PBKDF2), permisos por sector, servicios que validan, controlan permisos y escriben el historial, búsqueda, administración, importación y exportación a CSV, y datos de ejemplo. `Aplicacion` es la única puerta de entrada que usa la interfaz. |
| Interfaz | `src/Oficina.UI` | Pantallas armadas en código (sin diseñador). El programa se llama `RegistrosOficina.exe`. |

## Ponerlo en marcha

1. Crear `src/Oficina.UI/conexion.local.txt` con la cadena de PostgreSQL (una línea: `Host=…;Port=5432;Database=oficinaregistros;Username=…;Password=…`). Se copia sola al lado del programa y **no se sube a git**. Si no existe, se usa `conexion.txt` (PostgreSQL local, sin contraseña).
2. Abrir `Oficina.sln` en Visual Studio y apretar F5, o por consola:

```bash
dotnet build Oficina.sln
```

```bash
dotnet run --project src/Oficina.UI
```

Para una base nueva, la primera vez: `dotnet run --project src/Oficina.UI -- --crear-base`. Crea la base, sus tablas y las migraciones, y si está vacía carga datos de ejemplo con cuentas de prueba: la aplicación muestra sus contraseñas al terminar. **Hay que cambiarlas** (Configuración → Cuentas) antes de cargar datos reales.

Sin `--crear-base` la aplicación nunca crea una base ni carga ejemplos: solo aplica las migraciones pendientes. Si no puede conectarse, explica el motivo (por ejemplo, la VPN desconectada) y se cierra sin cargar nada. El **modo prueba** (datos de ejemplo en memoria, que se pierden al cerrar) se abre solo a propósito, con `--memoria`, y lo avisa al entrar y en la barra de título.

## Cuentas

- **Cuentas de sector**: una cuenta general por sector (`entrada`, `seguimiento`, `sala`, `sentencias`, `apremio`, `lotes`, `reclamos`, `estadisticas`). Sus permisos son los del rol del sector.
- **Cuentas personales**: para una persona que no trabaja en un sector, por ejemplo un juez de la oficina. Se crean en Configuración → Cuentas → «Nueva cuenta personal», con usuario, contraseña y cargo. Cada una tiene su propio rol (sin sector) y arranca **sin permisos**: la aplicación lleva directo a marcarlos.
- **Administrador** (`admin`): puede todo y es el único que ve Configuración.

Desde Configuración → Cuentas el Administrador puede, con cualquier cuenta: cambiar la contraseña, ver y modificar sus permisos («Permisos»), cambiarle el rol y **darla de baja o reactivarla**. Una cuenta dada de baja no puede entrar, pero no se borra: su nombre sigue en el historial de lo que hizo. El Administrador no puede darse de baja a sí mismo.

En el ingreso, al crear una cuenta y al cambiar una contraseña está la casilla **«Mostrar contraseña»** para ver lo que se escribió.

## Qué se puede probar

- **Buscar en todo** (arriba): busca mientras se escribe, sin mayúsculas ni acentos ("gomez" encuentra "Gómez"), en causa, acta, N° de apremio, persona, DNI, remito, movimiento, observaciones, reclamos y lotes. Los resultados salen agrupados por sector. Esc borra la búsqueda; Ctrl+F vuelve al buscador.
- **Buscar dentro de un sector**: cada planilla tiene su propio buscador.
- **Caso de ejemplo**: buscar `4512`. Es la misma acta pasando por los cinco sectores de actas, y fue modificada una vez: en la ficha se ve la versión original contra la actual. Buscar `1187` muestra dos actas distintas con el mismo N° de acta (lo que las distingue es causa + repartición + año).
- **Nuevo / Modificar / Eliminar / Exportar CSV / Importar**: los botones aparecen según los permisos de la cuenta. Eliminar es una baja lógica: la fila deja de verse pero queda guardada, con el motivo en el historial.
- **Ficha del acta** (doble clic en una fila o "Ver acta"): datos, recorrido por los sectores, original y actual, versiones e historial. "Modificar acta" pide un motivo y guarda una versión nueva.
- **Archivar en un lote**: con `lotes`, elegir un lote → «Actas del lote» → causa, repartición, año, motivo (sentencia definitiva o pagada) y fecha. El acta queda terminada: ya no admite movimientos ni cambios. Solo el Administrador puede sacarla del lote.
- **N° de apremio**: con `apremio`, en la ficha de un acta → «N° de apremio». No se puede repetir entre actas. La aplicación reconoce a Apremio por la clave de sector `APR`: no hay que cambiarla.
- **Categoría de reclamo**: con `reclamos`, Nuevo o Modificar → Tránsito, Demanda o Municipales.
- **Cuenta personal**: con `admin`, Configuración → Cuentas → «Nueva cuenta personal» (por ejemplo `jperez`, cargo «Juez de Faltas N° 2»), marcarle ver y modificar en Sentencias, guardar, y entrar con esa cuenta: solo ve Sentencias.
- **Cada cuenta ve solo lo suyo**: una cuenta que no es admin no ve los sectores que no puede leer en ningún lado (menú, inicio, búsqueda, fichas, historial, totales, sugerencias), ni los nombres de cuentas de otros sectores. Si intenta abrir algo oculto, el mensaje es «Ese registro no existe o no está disponible». Por ejemplo, entrar con `lotes` y buscar `4512`.
- **Configuración** (botón ⚙ arriba, solo para el Administrador): roles, permisos por sector, cuentas, sectores, reparticiones y juzgados, e historial de estos cambios. Al cerrarla, el menú de sectores y el inicio se actualizan.

## Respaldo en Drive

"Exportar CSV" guarda la planilla del sector (con el filtro que se esté usando) en un archivo separado por punto y coma, que Google Sheets y Excel abren directo. Si está instalado Google Drive para escritorio, propone guardarlo en la carpeta de Drive (`G:\Mi unidad` o similar), así se sube solo. Esto no reemplaza el backup de la base en el servidor.

## Decisiones que todavía son provisorias

- **Movimientos**: texto libre, con sugerencias (la oficina confirmó que no hay una lista fija).
- **Sala Auditoría y Sentencias**: están como sectores separados que anotan movimientos sobre la misma acta.
- **Lotes**: guardan cantidades (tránsito, competencia, prescriptas) y las actas archivadas en cada uno.
- **Personas**: el documento (DNI o CUIT) identifica a la persona: el mismo documento es la misma persona en todas sus actas. Al escribir un documento ya cargado, el formulario completa el nombre. Si se cambia el nombre de alguien con documento, pregunta y lo corrige en todas sus actas, y la corrección queda en el historial de cada una. Una planilla importada nunca renombra a nadie: usa el nombre ya cargado y lo avisa en la fila. Sin documento no se puede saber si es la misma persona, así que el cambio queda solo en esa acta.
- Los cambios de permisos y los sectores nuevos se ven cuando la cuenta vuelve a entrar.

## Guardado seguro

- **Todo o nada**: cada operación (por ejemplo, el alta de un acta con su persona, su versión original y su primer movimiento, y las entradas del historial) se guarda en una sola transacción. Si algo falla o se corta la conexión en el medio, no queda nada a medias.
- **Ediciones simultáneas**: si dos personas abren el mismo acta, movimiento, reclamo o lote y las dos guardan, la segunda recibe el aviso «Otra persona modificó este registro mientras usted lo tenía abierto» y su cambio no pisa al de la primera. Para esto se usa la columna de sistema `xmin` de PostgreSQL (no hizo falta cambiar las tablas).
