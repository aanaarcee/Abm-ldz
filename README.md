# ABM LDZ · registros de la oficina

Aplicación de escritorio en C# (Windows Forms, POO y N capas) con base de datos **PostgreSQL**. Reemplaza las planillas de Google Sheets de cada sector por una base única, con permisos por sector, historial de cada cambio y una búsqueda general en todos los registros o dentro de un sector.

> Prototipo funcional. Hoy corre contra una base de prueba en un servidor propio; la oficina lo va a instalar en su servidor y trabajar ahí de forma privada.

## Estructura

| Carpeta | Capa | Qué tiene |
| --- | --- | --- |
| `src/Oficina.Entidades` | Entidades | Clases del modelo: Sector, Rol, Usuario, Permiso, Persona, Acta, ActaVersion, Movimiento, Lote, Reclamo, Historial. |
| `src/Oficina.Datos` | Datos | Acceso a PostgreSQL (`Postgres/`, Npgsql), con transacciones y control de ediciones simultáneas, y una versión en memoria para probar sin base (`Memoria/`). |
| `src/Oficina.Negocio` | Negocio | Reglas: inicio de sesión, permisos por sector, validaciones, historial, búsqueda, archivo en lotes, importación y exportación. |
| `src/Oficina.UI` | Interfaz | Formularios de Windows Forms. El programa se llama `RegistrosOficina.exe`. |
| `sql/OficinaRegistros.sql` | Base | Estructura de partida: tablas y funciones de búsqueda. |
| `sql/Migraciones/` | Base | Cambios de estructura posteriores, numerados. Se aplican solos al abrir la aplicación. Ver su [LEEME](sql/Migraciones/LEEME.md). |

Detalles de uso, cuentas y qué probar: [LEEME.md](LEEME.md).

## Lo confirmado por la oficina

Sale de la entrevista (PDF de preguntas y respuestas); entre paréntesis, el número de pregunta.

- Un acta se identifica por **N° de causa + repartición + año**. El N° de acta se puede repetir.
- Existe el **acta original y el acta modificada**: cada cambio guarda una versión.
- Un **movimiento** es la actividad sobre el acta, en texto libre: no hay una lista fija (P9).
- Un acta **termina cuando se archiva en un lote**, por sentencia definitiva o porque se pagó, y queda ahí entre 5 y 10 años (P12, P13). Archivada, no admite cambios.
- **Un remito es único por acta** (P17).
- **Reclamos**: avisos de autos abandonados, con categoría Tránsito, Demanda o Municipales (P20). «Verificado» es cuando alguien va a ver el auto: se anota quién y cuándo (P21).
- **Monto 1 y monto 2** son la suma reclamada y los gastos administrativos (P22).
- **Apremio** le da a cada acta su propio número (P29).
- Una persona puede ser una empresa (razón social y CUIT). El **documento identifica a la persona**: el mismo DNI o CUIT es la misma persona en todas sus actas. Se escribe sin puntos (P34, P36).
- Hay gente que **trabaja desde su casa** (P46): se conecta por VPN.
- Quieren seguir usando **Drive como respaldo**: la app exporta a CSV.

## Qué hace

- **Planilla por sector** con su buscador, y **búsqueda general** en todo lo que la cuenta puede ver: causa, acta, N° de apremio, persona, DNI, remito, movimientos, reclamos y lotes, sin distinguir mayúsculas ni acentos.
- **Ficha del acta**: recorrido por los sectores, original contra actual, versiones e historial.
- **Archivo en lotes**: el sector Lotes archiva actas terminadas; quedan de solo lectura y solo el Administrador puede sacarlas.
- **N° de apremio**: lo carga solo el sector Apremio (clave `APR`).
- **Importar** planillas CSV o Excel con vista previa e informe de errores, y **exportar** a CSV para Drive.
- **Cuentas**: una por sector, más **cuentas personales** (por ejemplo, un juez) con sus propios permisos. El Administrador puede cambiar contraseñas, permisos y roles, y dar de baja o reactivar cuentas. Hay una casilla para mostrar la contraseña al escribirla.
- **Guardado seguro**: cada operación se guarda entera o no se guarda (transacciones), y si dos personas editan lo mismo, la segunda recibe un aviso en vez de pisar el cambio de la primera.

## Cómo abrirlo en Visual Studio

Hace falta **Visual Studio 2026** (o una versión que traiga el SDK de **.NET 10**) con la carga de trabajo «Desarrollo de escritorio de .NET», y acceso a un servidor **PostgreSQL** (versión 15 o más nueva).

1. Clonar el repositorio y abrir `Oficina.sln`. El proyecto de inicio es `Oficina.UI`.
2. Crear `src/Oficina.UI/conexion.local.txt` con la cadena de conexión (una sola línea):

   ```
   Host=servidor;Port=5432;Database=oficinaregistros;Username=oficina_app;Password=clave
   ```

   Ese archivo **no se sube a git** (tiene la contraseña) y se copia solo al lado del programa. Si no existe, se usa `conexion.txt`, que apunta a un PostgreSQL local sin contraseña.
3. Apretar **F5**.

Si no puede conectarse, la aplicación explica el motivo (por ejemplo, la VPN desconectada) y se cierra sin cargar nada. El **modo prueba** (datos de ejemplo en memoria, que se pierden al cerrar) se abre solo a propósito, con el argumento `--memoria`, y lo avisa en la barra de título.

## Base de datos

**Base nueva:** abrir la aplicación una vez con `--crear-base` (en Visual Studio: propiedades de `Oficina.UI` → Depurar → Argumentos de la línea de comandos), con el usuario dueño de la base. Crea la base `oficinaregistros`, sus tablas, le aplica las migraciones y, si está vacía, carga datos de ejemplo con cuentas de prueba (ver [LEEME.md](LEEME.md)). Esas contraseñas hay que cambiarlas antes de cargar datos reales.

**Base existente:** al abrir la aplicación se aplican solas las migraciones pendientes. Sin `--crear-base` la aplicación nunca crea una base ni carga datos de ejemplo.

### Dos usuarios de base

La contraseña de la base queda guardada en cada PC, así que la aplicación **no** usa el superusuario todos los días:

| Usuario | Para qué | Dónde va |
| --- | --- | --- |
| `oficina_app` | Uso diario. Lee, carga y modifica datos. No puede cambiar ni borrar el historial ni las versiones de actas, ni tocar la estructura de la base. | `conexion.local.txt` de cada PC |
| `oficina` (dueño de la base) | Crear la base (`--crear-base`) y aplicar migraciones nuevas. | `conexion.admin.local.txt`, solo en la PC de quien mantiene el sistema |

Cuando una versión nueva trae migraciones, la aplicación abierta con `oficina_app` avisa que hay cambios pendientes. Se abre una vez con la cadena de `oficina` (copiándola a `conexion.local.txt`), se aplican solas, y se vuelve a `oficina_app`. Los dos archivos `*.local.txt` están en `.gitignore`.

Para crear `oficina_app` en una base nueva (como `oficina`):

```sql
CREATE ROLE oficina_app LOGIN PASSWORD 'una-contraseña-larga';
GRANT CONNECT ON DATABASE oficinaregistros TO oficina_app;
GRANT USAGE ON SCHEMA public TO oficina_app;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO oficina_app;
GRANT DELETE ON Permiso TO oficina_app;
REVOKE UPDATE ON Historial, ActaVersion FROM oficina_app;
REVOKE INSERT, UPDATE ON EsquemaMigracion FROM oficina_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO oficina_app;
ALTER DEFAULT PRIVILEGES FOR ROLE oficina IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO oficina_app;
ALTER DEFAULT PRIVILEGES FOR ROLE oficina IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO oficina_app;
```

### Backups

Exportar a CSV no reemplaza un backup de la base. En el servidor conviene un `pg_dump` diario, guardado al menos 30 días y copiado **fuera** de ese servidor (si se rompe el disco, se pierden la base y los backups juntos). Por ejemplo, con la base en un contenedor Docker llamado `pg-oficina`:

```bash
#!/bin/bash
# ~/backups/backup-oficina.sh · en cron: 0 3 * * * $HOME/backups/backup-oficina.sh >> $HOME/backups/oficina.log 2>&1
set -euo pipefail
D="$HOME/backups/oficina"; mkdir -p "$D"
F="$D/oficina-$(date +%F).dump"
docker exec pg-oficina pg_dump -U oficina -Fc oficinaregistros > "$F.tmp"
[ -s "$F.tmp" ] && mv "$F.tmp" "$F"
find "$D" -name 'oficina-*.dump' -mtime +30 -delete
```

Para restaurar: `pg_restore -U oficina -d <base> --no-owner oficina-AAAA-MM-DD.dump`. Conviene probar una restauración en una base aparte de vez en cuando.

## Seguridad

- **La base no se abre a internet**: se llega por una VPN (por ejemplo, Tailscale). Quien trabaja desde su casa necesita la VPN conectada.
- **Contraseñas de las cuentas** guardadas con PBKDF2 y sal: no se pueden ver, solo cambiar.
- **Cada cuenta ve solo sus sectores**: menú, búsqueda, fichas e historial respetan los permisos.
- **Nada se borra**: las bajas son lógicas y todo cambio queda en el historial, que el usuario de todos los días no puede modificar.

## Estado

- [x] Base de datos (PostgreSQL) con migraciones
- [x] Entidades, Datos, Negocio e Interfaz
- [x] Respuestas de la entrevista: archivo en lotes, N° de apremio, categoría de reclamo, un remito por acta, el documento identifica a la persona
- [x] Cuentas personales (sin sector) con sus propios permisos
- [x] Transacciones y aviso de ediciones simultáneas
- [x] Usuario de base con permisos limitados y backup diario
- [ ] Guía de instalación para el servidor de la oficina (servidor, VPN, primera carga de datos reales)
- [ ] Menos consultas a la base en el historial de Configuración y en la ficha del acta (rendimiento con muchos datos)
