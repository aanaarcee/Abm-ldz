# ABM LDZ · registros de la oficina

Aplicación de escritorio en C# (Windows Forms, POO y N capas) con base de datos **PostgreSQL**. Reemplaza las planillas de Google Sheets de cada sector por una base única, con roles por sector y una búsqueda general en todos los registros, o dentro de uno solo.

> Prototipo funcional. Hoy corre contra una base de prueba; la oficina lo va a instalar en su propio servidor.

## Estructura

| Carpeta | Capa | Qué tiene |
| --- | --- | --- |
| `src/Oficina.Entidades` | Entidades | Clases del modelo: Sector, Rol, Usuario, Permiso, Acta, ActaVersion, Movimiento, Lote, Reclamo, Historial. |
| `src/Oficina.Datos` | Datos | Acceso a PostgreSQL (`Postgres/`, Npgsql) y una versión en memoria para probar sin base (`Memoria/`). |
| `src/Oficina.Negocio` | Negocio | Reglas: inicio de sesión, permisos por sector, validaciones, historial, búsqueda, importación y exportación. |
| `src/Oficina.UI` | Interfaz | Formularios de Windows Forms. El programa se llama `RegistrosOficina.exe`. |
| `sql/OficinaRegistros.sql` | Base | Estructura de partida: tablas y funciones de búsqueda. |
| `sql/Migraciones/` | Base | Cambios de estructura posteriores, numerados. Se aplican solos al abrir la aplicación. Ver su [LEEME](sql/Migraciones/LEEME.md). |

## Lo confirmado por la oficina

- Un acta se identifica por **N° de causa + repartición + año**. El N° de acta se puede repetir.
- Existe el **acta original y el acta modificada**: cada cambio guarda una versión.
- Un acta **termina cuando se archiva en un lote**, por sentencia definitiva o porque se pagó, y queda ahí entre 5 y 10 años. Archivada, no admite cambios.
- **Apremio** le da a cada acta su propio número (N° de apremio).
- **Un remito es único por acta.**
- **Reclamos** son avisos de autos abandonados, con categoría Tránsito, Demanda o Municipales. «Verificado» es cuando alguien va a ver el auto: se anota quién y cuándo.
- **Monto 1 y monto 2** son la suma reclamada y los gastos administrativos.
- Quieren seguir usando **Drive como respaldo**: la app exporta a CSV.

## Cómo abrirlo en Visual Studio

Hace falta **Visual Studio 2026** (o una versión que traiga el SDK de **.NET 10**) con la carga de trabajo «Desarrollo de escritorio de .NET», y acceso a un servidor **PostgreSQL** (versión 15 o más nueva).

1. Clonar el repositorio y abrir `Oficina.sln`. El proyecto de inicio es `Oficina.UI`.
2. Crear `src/Oficina.UI/conexion.local.txt` con la cadena de conexión (una sola línea):

   ```
   Host=servidor;Port=5432;Database=oficinaregistros;Username=usuario;Password=clave
   ```

   Ese archivo **no se sube a git** (tiene la contraseña) y se copia solo al lado del programa. Si no existe, se usa `conexion.txt`, que apunta a un PostgreSQL local sin contraseña.
3. Apretar **F5**.

## Base de datos

**Base nueva:** abrir la aplicación una vez con `--crear-base` (en Visual Studio: propiedades de `Oficina.UI` → Depurar → Argumentos de la línea de comandos). Crea la base `oficinaregistros`, sus tablas, le aplica las migraciones y, si está vacía, carga datos de ejemplo con cuentas de prueba (ver [LEEME.md](LEEME.md)). Esas contraseñas hay que cambiarlas antes de cargar datos reales.

**Base existente:** al abrir la aplicación se aplican solas las migraciones pendientes. Sin `--crear-base` la aplicación nunca crea una base ni carga datos de ejemplo.

El usuario de la cadena de conexión necesita permiso para crear tablas solo cuando hay migraciones nuevas para aplicar.

Si no puede conectarse, ofrece abrir en **modo prueba**, con datos en memoria que se pierden al cerrar. También se puede forzar con `--memoria`.

## Estado

- [x] Base de datos (PostgreSQL) con migraciones
- [x] Entidades, Datos, Negocio e Interfaz
- [x] Respuestas de la entrevista: archivo en lotes, N° de apremio, categoría de reclamo, un remito por acta
- [ ] Guía de instalación para el servidor de la oficina (backups, VPN, usuario de base con permisos limitados)
