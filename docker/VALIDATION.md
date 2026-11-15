# Validación de Docker Compose

> **Estado actual:** consultar «Cierre de instalación limpia y publicación» al final.
> Las secciones anteriores conservan evidencia histórica; el hallazgo PKCS de esa
> revisión quedó corregido y auditado en el cierre.
> La revisión final de publicación al final clasifica CS8602, separa las capturas
> preexistentes y deja explícita la comprobación manual de navegador pendiente.

Ejecutada el 14 de noviembre de 2026 en Windows con Docker Desktop, motor Linux
amd64 29.9.2 y Compose 5.6.1. No se accedió a las bases de SQL Server de Windows.
Los archivos locales previamente existentes en `docs/` se conservaron.

## Evidencia previa conservada (implementación Docker)

- `docker compose config --quiet`: correcto.
- Construcción Release de `portalpedidos-demo-erp:local` y
  `portalpedidos-demo-portal:local`: correcta.
- Compilación Windows de `PortalPedidos.slnx --no-restore`: correcta, sin errores.
- SQL, ERP y portal iniciados con el proyecto `portalpedidos-demo`, saludables.
- Solo puerto `127.0.0.1:8271` publicado. SQL y ERP sin puertos publicados en Windows.
- Destinos verificados antes de migrar: `sql,1433`, `PortalPedidosDockerDemo` y
  `ErpSimulatorDockerDemo`. Pruebas negativas de ambos ejecutables, en contenedores
  sin red, rechazaron un servidor distinto y una base distinta antes de conectar.
- Cuatro migraciones del portal y dos del ERP aplicadas. Ambas bases `ONLINE`.
- Semillas: tres usuarios, seis roles, diez clientes, treinta productos y treinta
  registros de existencias. Catálogo sincronizado al portal.
- HTTPS validado con CA y nombres correctos. Un nombre de servidor incorrecto fue
  rechazado por TLS (curl código 60). No se usó `--insecure` ni validadores permisivos.
- Conexiones reales a SQL con `Encrypt=True;TrustServerCertificate=False`.
- `/health/live` y `/health/ready` del portal correctos; comprobaciones de disponibilidad
  de ERP correctas; `--check-erp` devolvió el catálogo por HTTPS entre contenedores.
- API ERP sin clave: HTTP 401. Login POST sin antiforgery: HTTP 400.
- Login real como vendedor mediante formularios HTTP y antiforgery. Cookie demo
  Secure, HttpOnly y SameSite=Lax verificada.
- Pedido **1001** creado por formulario, enviado y confirmado mediante outbox/ERP.
  Cliente C001, producto P001, cantidad 1. Estado portal `Confirmed`, recibo ERP
  `Accepted`, misma referencia ERP. Existencia P001: OnHand 120, Reserved 1.
- Reinicio de los tres servicios: pedido, recibo, existencias, usuarios y conteos de
  migraciones conservados; consultas SQL anteriores y posteriores idénticas.
  La sesión autenticada anterior continuó funcionando con las claves persistidas.
- Segunda ejecución completa del inicializador: no repitió migraciones, no sobrescribió
  contraseñas ni catálogo y mantuvo el pedido y su reserva.
- Valores secretos generados ausentes del YAML resuelto, logs de servicios, historial
  de imágenes y variables configuradas en los contenedores. Los archivos privados
  están ignorados por Git y fuera del contexto Docker.

`docker/smoke-test.py` usa únicamente la biblioteca estándar de Python y valida HTTPS;
no instala certificados en Windows. Sus cookies y el identificador del pedido se guardan
en `.docker-local/`. Las consultas reproducibles están en `docker/verify-data.sql`.

## Límites y pendientes

- No se automatizó un navegador gráfico ni el JavaScript del formulario. El recorrido
  sí ejecutó endpoints MVC reales, autenticación, antiforgery, base de datos y ERP.
- No se inició la aplicación desde Visual Studio contra las bases Windows. La solución
  compila en Windows; sus perfiles, conexiones y User Secrets no se modificaron.
- El usuario confirmó posteriormente un login manual correcto con `admin@portal.test`.
  No se inspeccionó su almacén de certificados ni se infiere de ese login que se haya
  validado todo el recorrido visual. La confianza del navegador se documenta en `docker/README.md`.
- No se prepararon equipos comerciales ni asignaciones de cartera.
- La compilación reporta advertencias existentes CS8602 en `ErpCatalogValidator.cs`
  y NU1902/NU1903 para la dependencia transitiva `System.Security.Cryptography.Pkcs`
  9.0.11 (GHSA-2fmv-rv2c-59x7 y GHSA-846f-f764-76mx). No se actualizaron las
  dependencias del proyecto como parte de la incorporación de Compose.
- Este entorno local usa SQL Developer y `sa` en una instancia aislada. No constituye
  una configuración de despliegue de producción.

No se hicieron commits, push, prune ni eliminaciones de recursos preexistentes.
Los contenedores temporales de inicialización/prueba se crearon con `--rm`.

## Revisión final: alcance y método

Revisión adicional del 14 de noviembre de 2026. Se leyeron este informe, la guía Docker,
las exclusiones y el código de autorización, pedidos e integración. No se encontraron
archivos `AGENTS.md` en el repositorio ni en sus directorios ascendientes consultados.
`git status` contenía los cambios de implementación anteriores y `docs/`; se preservaron.
No había archivos preparados para commit.

Se trabaja exclusivamente con `portalpedidos-demo`, servidor `sql,1433`, bases
`PortalPedidosDockerDemo` y `ErpSimulatorDockerDemo`. La nueva prueba verifica etiquetas
de los contenedores y destinos antes de actuar. No ejecuta migraciones, semillas,
eliminaciones ni consultas contra SQL Server de Windows.

No hay una herramienta de navegador interactivo disponible en esta sesión. El método
es HTTP real sobre HTTPS, con cookies y antiforgery, y SQL de solo lectura para contrastar
el efecto comercial. Se reutiliza el parser de `smoke-test.py` desde
`functional-test.py`. Los informes locales se conservan con sufijo UTC, sin credenciales.
La evidencia previa de persistencia, TLS, semillas y aislamiento no se repite salvo
las lecturas necesarias para comprobar el estado y los nuevos escenarios.

### Permisos reales revisados

| Recurso/operación | Administrador | Vendedor |
|---|---|---|
| Clientes y catálogo | Permitido | Permitido |
| Crear/editar/enviar pedidos | Permitido | Permitido, sujeto a propiedad y estado |
| Ver todos los pedidos | Permitido | No: consulta restringida por propietario |
| Usuarios | Permitido | Denegado |
| Monitor de integración y auditoría | Permitido (también operador) | Denegado |

Fuentes locales: `PortalAuthorization.cs`, `CurrentUser.cs`, `OrderDraftService.cs`
y los atributos `[Authorize]` de los controladores. Las comprobaciones de acceso
se realizan sobre endpoints del backend, no sobre la visibilidad de botones.

### Compatibilidad con Visual Studio: aprobada por revisión y compilación

- `PortalPedidos.slnx`, ambos `launchSettings.json` y ambos conjuntos `appsettings`
  coinciden con HEAD; Docker no sustituyó estos archivos.
- Perfiles HTTPS conservados: portal 7271, ERP 7117. Docker publica solamente 8271.
- Lectura local sin mostrar valores: ambas conexiones User Secrets conservan
  autenticación Windows y no apuntan a `sql,1433` ni a las bases demo.
- `dotnet build PortalPedidos.slnx --no-restore --nologo -v quiet`: cero errores,
  seis advertencias NU1902/NU1903. La evidencia de CS8602 de la compilación completa
  anterior se conserva; no se recompiló innecesariamente para forzar su reaparición.
- Arranque real fuera de Docker pendiente: abrir la solución en Visual Studio,
  seleccionar los perfiles `https` y verificar login, catálogo y `/health/ready`
  contra las bases locales previamente preparadas. Ese procedimiento accede a las
  bases Windows y por ello no se ejecutó en esta revisión. No ejecutar inicializadores
  ni migraciones Docker desde Visual Studio.

### Dependencias: hallazgo de seguridad abierto, sin cambios de versiones

Comprobaciones ejecutadas: `dotnet nuget why PortalPedidos.slnx
System.Security.Cryptography.Pkcs --framework net10.0`, lectura de `project.assets.json`
y `dotnet list PortalPedidos.slnx package --vulnerable --include-transitive --no-restore`.
La auditoría consultó NuGet tras autorizar la lectura de red inicialmente bloqueada.

Versión resuelta **9.0.11**, transitiva, en Portal.Infrastructure, Portal.Web y
ErpSimulator.Api. Cadena: `Microsoft.EntityFrameworkCore.SqlServer 10.0.12` →
`Microsoft.Data.SqlClient 6.1.6` → `System.Security.Cryptography.Pkcs 9.0.11`.
Portal.Web la recibe a través de Portal.Infrastructure. Application y Domain no
reportan paquetes vulnerables en los orígenes consultados.

| Aviso oficial | Gravedad y alcance | Corrección publicada |
|---|---|---|
| [CVE-2026-84008 / GHSA-846f-f764-76mx](https://github.com/dotnet/announcements/issues/468) | Alta (7.5), agotamiento CPU en `SignedCms.CheckSignature`; Windows, Linux y macOS | PKCS 9.0.21 o 10.0.13 en las ramas correspondientes |
| [CVE-2026-70162 / GHSA-2fmv-rv2c-59x7](https://github.com/dotnet/announcements/issues/454) | Moderada (5.9), agotamiento CPU en `EnvelopedCms.Decode`; Windows | PKCS 9.0.21 o 10.0.13 en las ramas correspondientes |

La versión resuelta entra en los rangos afectados. No se probó explotación ni se
demostró que las operaciones vulnerables reciban datos de un atacante por los endpoints
de esta aplicación; el resultado de NuGet no basta para afirmar explotabilidad concreta.

Distinción comprobada en las imágenes actualmente en ejecución: ambos contenedores
usan **Microsoft.AspNetCore.App / Microsoft.NETCore.App 10.0.15**. Sus manifiestos
`/app/*.deps.json` no incluyen PKCS 9.0.11 ni existe una copia PKCS en `/app`.
El archivo disponible está en el framework compartido
`/usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.15/System.Security.Cryptography.Pkcs.dll`;
su manifiesto declara assemblyVersion `10.0.0.0` y fileVersion `10.0.1526.53008`.
La rama 10.0.15 es posterior al parche 10.0.13 indicado por Microsoft. Por tanto,
**no se presenta el contenedor actual como si distribuyera el binario PKCS 9.0.11**;
el hallazgo pendiente corresponde al grafo NuGet y debe corregirse para evitar
dependencias afectadas en otras modalidades de publicación. No se hizo tracing
de carga de assemblies ni se ejecutaron exploits.

Propuesta acotada para un cambio posterior: fijar una referencia explícita de PKCS
**10.0.13 o un parche posterior corregido de la rama 10.0** en Portal.Infrastructure
y ErpSimulator.Api, compatible con el objetivo net10.0. Portal.Web la heredaría.
La corrección mínima dentro de la rama actualmente resuelta sería 9.0.21. Validar
restauración y grafo, repetir auditoría, compilar/publicar y probar SQL TLS, cookies y
flujo ERP antes de aceptar el cambio. No actualizar en bloque EF Core/SqlClient ni
silenciar NU1902/NU1903. No se modificó ninguna dependencia durante esta validación.

## Revisión final: resultados aprobados

Recorrido ejecutado por `python -B docker/functional-test.py`. Informe local conservado:
`.docker-local/functional-report-20261114T215425Z.json`. Ocho grupos aprobados y
ningún grupo funcional fallido en la ejecución completa.

| Comprobación | Método y resultado observado |
|---|---|
| Alcance | Etiquetas del proyecto, servicios saludables, conexiones esperadas y `DB_NAME()` verificados antes de crear datos. |
| Login y consultas | Sesiones HTTP independientes de administrador y vendedor. Clientes (incluido C009), catálogo (incluido P013) y formulario de pedidos: HTTP 200. |
| Autorización backend | Administrador: usuarios, integración y auditoría HTTP 200. Vendedor: los tres endpoints HTTP 403; también detalle de integración del pedido HTTP 403. |
| Datos inválidos | Cantidad cero enviada con token antiforgery válido: el formulario mostró el error de rango 1–9999. SQL confirmó que no existía el pedido antes de reenviar datos válidos. |
| Borrador | Guardado con dos líneas y reabierto dos veces por `/Orders/Edit`. Coincidieron cliente, productos, cantidades, precios y subtotal en HTML y SQL. |
| Interrupción ERP | Se ejecutó únicamente `docker compose -p portalpedidos-demo stop erp`. El pedido siguió `Queued`; outbox quedó `Unknown`, con `ERP_CONNECTION_FAILED`. No había recibo ERP ni rechazo comercial. |
| Repetición de envío | Repetición del POST original mientras estaba pendiente y después de confirmarse, conservando su RowVersion y antiforgery. HTTP 200 según la idempotencia prevista por `QueueAsync`; un único mensaje y un único recibo ERP. |
| Recuperación automática | Se arrancó el mismo contenedor mediante `start erp`. El procesador consultó el resultado (404) y después reenvió; terminó `Confirmed` / `Accepted`. No se modificó manualmente la cola ni el pedido. |
| Efecto comercial | P013 reservó exactamente 2 unidades y P014 exactamente 3. La repetición posterior no cambió reservas, recibo ni número de mensajes. |
| Trazabilidad | Coincidencia de referencia ERP en pedido y recibo; historial Draft → Queued → Confirmed, tres intentos y cinco eventos de auditoría. Detalle de pedido, monitor filtrado y detalle de integración consultados como administrador. |
| Auditoría | Consulta HTTP del listado y apertura de los cinco detalles; identificador de pedido/mensaje verificado en sus respuestas. SQL contrastó acciones y entidades. |
| Logout | POST con antiforgery en ambas sesiones; desapareció la cookie de autenticación de cada cliente. Nueva solicitud protegida redirigió al login. |

### Datos de prueba conservados

Etiqueta documental: **DOCKER-FUNCTIONAL-20261114T215425Z**. La aplicación no dispone
de un campo de etiqueta libre en este formulario; el pedido se identifica por estos IDs.
No se alteraron datos maestros ni el pedido 1001 de la validación anterior.

| Dato | Valor |
|---|---|
| Pedido nuevo | **1002** |
| ID pedido | `ba950556-f610-4cec-a9a7-aafbd783d9b8` |
| Cliente / propietario | C009 / cuenta demo vendedor |
| Línea 1 | P013, cantidad 2, precio PEN 7.50, importe PEN 15.00 |
| Línea 2 | P014, cantidad 3, precio PEN 16.00, importe PEN 48.00 |
| Subtotal referencial sin impuestos | **PEN 63.00** |
| Mensaje outbox | `e8d059c7-c7f6-4b68-8e36-9ca61be74581` |
| Referencia ERP | `SIM-d9f9a707e2b5459689a6c60ba38eae8f` |
| Auditoría | IDs 4–8: DraftCreated, SubmissionQueued, Integration.Failed, Integration.LookupNotFound, ErpConfirmed |
| Reservas antes → después | P013: 0 → 2 (OnHand 50); P014: 0 → 3 (OnHand 28) |

Intentos observados:

1. Submit: `ERP_CONNECTION_FAILED`, sin respuesta HTTP.
2. Lookup: HTTP 404, `ORDER_RESULT_NOT_FOUND`.
3. Submit: HTTP 200, `ORDER_ACCEPTED`.

El fallo de comunicación del intento 1 era intencional; fue recuperado y **no** se
interpretó como rechazo comercial. La evidencia anterior de persistencia tras reinicio
de SQL, portal y ERP continúa vigente. No se volvió a reiniciar SQL ni portal para
repetirla: en esta revisión solo se interrumpió el ERP para el caso que faltaba cubrir.

### Secretos y archivos: aprobado

Se examinó el índice Git (incluidos blobs preparados), rutas sensibles y exclusiones.
No hay archivos en staging. Ninguna ruta `.docker-local`, secreto JSON, contraseña,
PFX/P12 o clave privada está versionada. Todos los archivos locales Docker están
ignorados. Una comparación en memoria de los valores generados y marcadores de claves
privadas contra **366 archivos candidatos** (versionados y no ignorados) no encontró
coincidencias; no se imprimieron sus valores. `.dockerignore` excluye todo por defecto
y solo admite fuentes y el entrypoint; `.docker-local` no entra al contexto.
Tras el recorrido se compararon también los logs completos de los tres servicios
contra los valores secretos en memoria: no hubo coincidencias ni se mostró el contenido
de los logs. `git diff --check` terminó sin errores de espacios o conflictos.

## Revisión final: fallos y hallazgos abiertos

- **Funcionalidad/Docker:** no se observó un defecto funcional ni de configuración
  Docker en el recorrido completado. No fue necesario modificar la aplicación,
  Compose, imágenes ni dependencias.
- **Dependencias:** permanece el hallazgo NU1902/NU1903 del grafo NuGet descrito arriba.
  La compilación termina correctamente, pero el resultado de auditoría no está limpio.
  Se propone resolverlo mediante un cambio separado y acotado antes de publicar.
- **Instrumentación, corregida:** los dos primeros intentos de la nueva prueba no
  pudieron interpretar la salida de `sqlcmd`: primero el mensaje de cambio de base,
  después una consulta vacía que no emite JSON. Se corrigió el lector usando `-d` y
  representando la salida vacía exitosa como lista vacía. Se conservaron ambos informes;
  no se crearon pedidos ni se interrumpió ERP en esos intentos. No se relajaron reglas
  de negocio, TLS ni aserciones para hacer pasar el recorrido.

## Revisión final: pendientes y límites

- Validación **visual y de JavaScript** en navegador: no ejecutada por el agente. El
  login manual de administrador confirmado por el usuario es evidencia aparte y no
  reemplaza la revisión visual de clientes, catálogo, líneas dinámicas y mensajes.
- Arranque desde Visual Studio contra las bases Windows: pendiente por el límite
  explícito de esta revisión; perfiles y compilación sí comprobados.
- Corregir el grafo vulnerable en un cambio posterior y repetir auditoría/pruebas
  afectadas. No se hicieron actualizaciones amplias ni se ocultaron advertencias.
- No se probó pérdida de respuesta después de aceptar un pedido, agotamiento de
  reintentos/dead-letter, concurrencia masiva, otros roles ni toda la aplicación.
  La interrupción probada ocurrió antes de que el ERP recibiera el pedido.
- No se creó una segunda cuenta de vendedor para probar aislamiento entre dos
  propietarios; esa restricción se revisó en código. Los accesos administrativos
  denegados al vendedor sí se ejecutaron contra el backend.

## Estado final y uso

Se dejaron **sql, erp y portal saludables**, sin borrar datos, contenedores ni volúmenes.
No se hicieron commit, push, prune, down ni operaciones en otros proyectos.
En esta revisión solo se añadió `docker/functional-test.py` y se actualizó este informe;
los cambios anteriores permanecen pendientes de revisión/publicación.

Desde la raíz, para iniciar los contenedores ya preparados sin recrearlos:

```powershell
docker compose -p portalpedidos-demo start sql erp portal
docker compose -p portalpedidos-demo ps
```

Para detener conservando datos:

```powershell
docker compose -p portalpedidos-demo stop portal erp sql
```

Abrir **https://localhost:8271/Account/Login**. Las credenciales se consultan localmente
según `docker/README.md`; este informe no contiene contraseñas.
Para repetir voluntariamente el recorrido adicional: `python -B docker/functional-test.py`.
Cada ejecución crea un pedido demo identificable en su informe, interrumpe brevemente
solo ERP y conserva todos los datos; no es una prueba sin efectos.

## Cierre de instalación limpia y publicación

Realizado el **14 de noviembre de 2026, hora de Lima** (15 de noviembre UTC).
Esta sección actualiza el estado de los hallazgos históricos anteriores.
No se encontraron AGENTS.md aplicables. Se conservaron los cambios locales existentes,
incluidos README, aislamiento Compose, referencia PKCS y las siete capturas de `docs/IMAGES`.

### Copia y aislamiento

Se copiaron **367 archivos** del árbol de trabajo: versionados más nuevos no ignorados
(`git ls-files --cached --others --exclude-standard`), sin copiar `.git`, `.docker-local`,
`.env`, bin, obj, .vs, bases, backups, secretos ni configuraciones personales.
La copia contiene las modificaciones sin commit: no es un clone del HEAD remoto.
Se comprobó que no había rutas sensibles dentro de esa selección antes de copiar.

Carpeta ejecutada: `%TEMP%/PortalPedidos-release-99ce211f`.
La primera carpeta `%TEMP%/PortalPedidos-release-gr72ud_h` quedó inaccesible para el
usuario autorizado de Docker por permisos del entorno de ejecución; no inició servicios.
Ambas se conservaron. No se reutilizó configuración privada de ninguna instalación.

Ya existía `portalpedidos-cleancheck` en el puerto 8371: se dejó intacto. Se siguió la
guía de segunda copia de `docker/README.md`, eligiendo otro nombre y puerto libres.
Desde la raíz de la copia:

```powershell
@"
COMPOSE_PROJECT_NAME=portalpedidos-releasecheck
PORTAL_HTTPS_PORT=8471
"@ | Set-Content .env -Encoding ascii
.\docker\Initialize-Demo.ps1
```

El inicializador y los comandos posteriores resolvieron la misma configuración.
Imágenes, red y volúmenes llevan el prefijo `portalpedidos-releasecheck`.
Solo el portal publica `127.0.0.1:8471`; SQL y ERP no publican puertos del anfitrión.
No fue necesario modificar adicionalmente el aislamiento ya presente en los cambios locales.

Un intento de capturar la salida con `*> install.log` hizo que Windows PowerShell 5.1
tratara el progreso de Docker en stderr como `NativeCommandError`. Se ejecutó después
el comando documentado sin redirección y terminó correctamente. No se cambió la política
de ejecución ni se desactivó ningún control. El log de ese intento se conserva en la copia.

### Resultados y comandos

- Configuración y certificados nuevos generados por `setup`, sin imprimir sus valores.
- Inicialización completa correcta: SQL healthy, cuatro migraciones del portal,
  dos del ERP, seis roles, tres cuentas, diez clientes, treinta productos/existencias,
  comprobación ERP por HTTPS y sincronización del catálogo.
- El primer build utilizó caché de Docker. Para validar compilación/restauración desde
  fuentes se ejecutó además `docker compose build --no-cache erp portal`: correcto.
  Se conservaron imágenes base locales; no es una prueba con caché global vacía.
- Se inició lo recién compilado con `docker compose up -d --wait sql erp portal`.
- `python -B docker/smoke-test.py --port 8471`: correcto. Validó CA/nombre HTTPS,
  salud, rechazo antiforgery, login vendedor, atributos de cookie y pedido por outbox.
- Pedido **1001**, ID `e054e6ca-9abe-4471-8528-d36ebea70594`, `Confirmed`;
  recibo ERP `Accepted`, referencia `SIM-b21c84d4c56044299fdc0be2d9db3216`.
  P001: OnHand 120, Reserved 1; un único pedido y recibo.
- `.\docker\Initialize-Demo.ps1 -SkipBuild`: correcto. Sin migraciones nuevas,
  sin sobrescribir usuarios/catálogo. Comparación de hashes de archivos privados
  antes/después idéntica; salida de `docker/verify-data.sql` idéntica, incluidos
  conteos, pedido, recibo y reserva. La sincronización sí actualiza su fecha de lectura.
- `python -B docker/smoke-test.py --port 8471 --verify-existing`: correcto después
  de repetir la inicialización y nuevamente después de los siguientes comandos:

```powershell
docker compose restart sql erp portal
docker compose up -d --wait sql erp portal
python -B docker/smoke-test.py --port 8471 --verify-existing
docker compose ps
```

Sesión y pedido conservados. Los tres servicios quedaron saludables. Una consulta
final de solo lectura confirmó también `portalpedidos-demo` saludable en 8271.
No se arrancaron aplicaciones contra SQL Windows ni se operó sobre otros proyectos.

Plataforma comprobada: Windows PowerShell 5.1.26100.9818, Docker Engine 29.9.2 amd64,
Compose 5.6.1, Python 3.13.17, contenedores Linux. La confianza se suministró mediante
la CA de esta copia a los clientes HTTP: **no se importó una raíz al almacén Windows**.
El procedimiento manual y sus efectos están explicados en README.

### Corrección concreta de dependencias

Los cambios locales ya contenían una referencia directa a
`System.Security.Cryptography.Pkcs` **10.0.13** en `Portal.Infrastructure.csproj`
y `ErpSimulator.Api.csproj`; se conservaron y validaron. Portal.Web la recibe
transitivamente. EF Core 10.0.12 y SqlClient 6.1.6 no se actualizaron.
Los avisos oficiales [CVE-2026-84008](https://github.com/dotnet/announcements/issues/468)
y [CVE-2026-70162](https://github.com/dotnet/announcements/issues/454) indican
10.0.13 como versión corregida de la rama 10.0.

Desde la copia temporal:

```powershell
dotnet restore PortalPedidos.slnx --force --no-cache
dotnet list PortalPedidos.slnx package --vulnerable --include-transitive --no-restore
dotnet build PortalPedidos.slnx --no-restore --nologo -v quiet
dotnet nuget why PortalPedidos.slnx System.Security.Cryptography.Pkcs --framework net10.0
```

Restauración correcta, auditoría sin paquetes vulnerables reportados en los cinco
proyectos y grafo resuelto a PKCS 10.0.13. Compilación: cero errores, cuatro CS8602
preexistentes en ErpCatalogValidator (líneas 57, 58, 85 y 114). Build Linux sin caché,
TLS, migraciones y pedido integrado pasaron con esta corrección. No se ampliaron
dependencias ni se suprimieron avisos. La auditoría representa los orígenes consultados
en esta fecha; no es garantía de ausencia de vulnerabilidades futuras.

### Revisión de publicación y propuesta de commit

Se revisaron rutas versionadas/nuevas, appsettings, ejemplos y patrones de credenciales.
La comparación en memoria de secretos generados de la instalación original contra los
367 candidatos no encontró coincidencias ni bloques de clave privada. No se imprimieron
valores secretos. Las siete capturas se inspeccionaron visualmente: muestran cuentas
demo y datos del simulador, sin contraseñas o claves visibles. No son evidencia visual
del recorrido actual. `.env.example`, scripts y plantillas necesarios permanecen incluidos.
La comprobación final amplió la comparación a los secretos nuevos de la copia y a sus
logs: cero coincidencias. Las fuentes de ambas carpetas coinciden; solo README e informe
cambiaron después de ejecutar las pruebas. `git diff --check` terminó correctamente.
El índice sigue sin cambios preparados. `HANDOFF.md` aporta continuidad de sesión,
pero duplica esta documentación y contiene rutas/estado local: queda ignorado, sin publicar.

Archivos propuestos para el commit (revisar el diff, no usar una inclusión indiscriminada):

- `.gitattributes`, `.gitignore`, `.dockerignore`, `.env.example`, `compose.yaml`, `README.md`.
- `docker/`: Initialize-Demo.ps1, setup.sh, entrypoint.sh, sql-entrypoint.sh,
  mssql.conf, README.md, VALIDATION.md, smoke-test.py, functional-test.py y verify-data.sql.
- `src/Shared/DockerDemo.cs`; Dockerfile, Program.cs y csproj de Portal.Web y ErpSimulator.Api.
- `src/Portal.Infrastructure/Portal.Infrastructure.csproj` por la corrección PKCS.
- Las siete imágenes existentes de `docs/IMAGES/`, referenciadas por README.

Título sugerido: **Preparar demo Docker instalable y documentar validación limpia**.

Descripción sugerida: «Incorpora una instalación local aislada de PortalPedidos y su ERP
simulado con TLS, secretos generados, migraciones y persistencia. Documenta Docker y
Visual Studio, permite varias copias mediante proyecto/puerto propios y corrige el grafo
PKCS a 10.0.13. Validado desde copia limpia en esta computadora: build sin caché,
auditoría NuGet, pedido integrado, reinicio y repetición segura del inicializador.»

### Recursos conservados y límites pendientes

- Carpetas temporales `PortalPedidos-release-99ce211f` y `PortalPedidos-release-gr72ud_h`
  bajo `%TEMP%`. La primera contiene `.env`, certificados/secretos/cookies en
  `.docker-local`, `install.log` y bin/obj generados por la validación Windows.
  Estos archivos generados no forman parte de la selección publicable.
- Tres contenedores `portalpedidos-releasecheck-{sql,erp,portal}-1`, imágenes
  `portalpedidos-releasecheck-{erp,portal}:local`, red `portalpedidos-releasecheck_default`,
  volúmenes `portalpedidos-releasecheck_sql-data` y `portalpedidos-releasecheck_portal-keys`.
  Se conservaron también las capas/cache de build; contenedores de tareas usan `--rm`.
- Puntero local `.docker-local/release-copy-path.txt` en el checkout; no se publica.
  Informe resumido `.docker-local/release-audit.json`, también excluido de publicación.
- No hay un bloqueo observado para instalar la demo Docker con el procedimiento probado.
  Siguen pendientes navegador/JavaScript completo, arranque real desde Visual Studio,
  otras plataformas/equipos y escenarios funcionales no cubiertos en la revisión histórica.
  Las cuatro advertencias CS8602 siguen abiertas; no se cambió lógica comercial para cerrarlas.
- Esta prueba demuestra **instalación limpia en esta computadora**, no reproducibilidad
  en otra máquina ni un despliegue de producción. No se probó una descarga GitHub del
  cambio aún sin publicar. Las etiquetas de imágenes .NET son actualizables.
- No se hicieron commit, push, eliminación de volúmenes ni limpiezas globales.

## Revisión final para GitHub

Revisión estática posterior al cierre. No se repitieron build, auditoría de red,
migraciones, inicialización, reinicios ni pedidos ya aprobados. Solo se leyeron fuentes,
diff, artefactos de evidencia y metadatos de los contenedores temporales.

### Clasificación de las cuatro CS8602

Todas pertenecen a `src/Portal.Infrastructure/Synchronization/ErpCatalogValidator.cs`.
Se comparó el archivo completo con HEAD: idéntico normalizando finales de línea.
Son **preexistentes**, no introducidas por Docker ni por la corrección PKCS.

| Línea | Expresión señalada | Impacto potencial si faltara la guarda |
|---|---|---|
| 57 | `snapshot.Items.Count` | Excepción por colección de productos nula al comprobar tamaño. |
| 58 | `snapshot.Stocks.Count` | Excepción por colección de existencias nula al comprobar tamaño. |
| 85 | `foreach (var item in snapshot.Items)` | Excepción al recorrer productos nulos. |
| 114 | `foreach (var stock in snapshot.Stocks)` | Excepción al recorrer existencias nulas. |

Impacto real por revisión del flujo actual: bajo, deuda de análisis de nulabilidad.
La guarda de colecciones al inicio llama a `Fail()` cuando alguna es nula; ese método
siempre lanza `ErpClientException("CATALOG_INVALID", ...)` y nunca retorna. El compilador
no conoce ese contrato porque no lleva una anotación de no retorno. Por ello esas
colecciones no llegan nulas a las cuatro expresiones por el flujo actual.
`CatalogSyncService` valida antes de abrir su transacción y modificar el catálogo.
No se observaron fallos del catálogo/pedido demo en las pruebas existentes.

No se añadieron `!`, pragmas, NoWarn ni cambios para aparentar un build limpio.
Una mejora futura acotada sería expresar formalmente el contrato no retornante de
`Fail()` y comprobar entradas inválidas; no es parte de este cierre documental.
Esta conclusión se refiere a estas cuatro advertencias, no a todo posible dato ERP inválido.

### PKCS y límites de la evidencia

Confirmación de solo lectura de los `project.assets.json` de releasecheck:
Infrastructure, Web y ERP resuelven **System.Security.Cryptography.Pkcs/10.0.13**.
Las dos referencias directas siguen en los csproj de Infrastructure y ERP.
Se conserva la evidencia anterior de restauración, grafo `dotnet nuget why`, auditoría
sin vulnerabilidades reportadas, compilaciones Windows/Linux y pedido integrado.
No se volvió a consultar NuGet ni se ejecutó un exploit. El grafo resuelto no prueba
por sí solo qué assembly carga el runtime; no se afirma tracing de carga.

No se identificó un bloqueo funcional nuevo para la demo Docker. La revisión visual
completa sigue abierta: hay binarios Chrome/Edge instalados, pero no una herramienta
de control de navegador conectada ni Playwright/Selenium/Puppeteer disponibles en esta
sesión. No se modificaron certificados del usuario ni se instalaron herramientas.
El [recorrido manual en README](../README.md#comprobación-manual-pendiente-en-navegador)
cubre login, clientes, catálogo, líneas dinámicas, envío, integración y errores de
Console/Network. No se afirma ausencia de errores JavaScript sin ejecutar ese recorrido.
Visual Studio real permanece pendiente para no intervenir en bases Windows.
La validación limpia corresponde únicamente a esta computadora.

### Selección exacta y separación de cambios

Se revisó todo el diff de los ocho archivos modificados y el contenido de los nuevos
archivos de código/configuración/pruebas. Total propuesto: **31 archivos** (8 modificados,
23 nuevos). Ninguno está preparado en staging. El conjunto completo de fuentes candidatas
contiene 367 archivos; no confundir ese conteo con los 31 cambios del commit.

Cambios Docker, seguridad y documentación de instalación (24 archivos):

```text
.gitattributes
.gitignore
.dockerignore
.env.example
compose.yaml
README.md
docker/Initialize-Demo.ps1
docker/README.md
docker/VALIDATION.md
docker/entrypoint.sh
docker/functional-test.py
docker/mssql.conf
docker/setup.sh
docker/smoke-test.py
docker/sql-entrypoint.sh
docker/verify-data.sql
src/ErpSimulator.Api/Dockerfile
src/ErpSimulator.Api/ErpSimulator.Api.csproj
src/ErpSimulator.Api/Program.cs
src/Portal.Infrastructure/Portal.Infrastructure.csproj
src/Portal.Web/Dockerfile
src/Portal.Web/Portal.Web.csproj
src/Portal.Web/Program.cs
src/Shared/DockerDemo.cs
```

La referencia PKCS es una corrección de seguridad acotada, distinguible de la
infraestructura Docker. README también ajusta ejemplos TLS y explica Visual Studio.
No hay cambios en reglas comerciales, esquema, perfiles de inicio ni conexiones Windows.

Material de portafolio **preexistente y ajeno a la implementación Docker**, conservado
y propuesto como documentación adicional (7 archivos):

```text
docs/IMAGES/dashboard light.png
docs/IMAGES/dashboard night.png
docs/IMAGES/Detalle Integ Pedido.png
docs/IMAGES/Integraciones de ERP.png
docs/IMAGES/Nuevo Pedido.png
docs/IMAGES/Pedido detalle.png
docs/IMAGES/Referencia ERP Pedido.png
```

Se reutilizó la inspección visual anterior y se comprobó identidad byte a byte con las
capturas revisadas de la copia: ninguna cambió. Muestran cuentas y datos demo, sin
secretos visibles. Pueden ir en un commit documental separado si se desea separar alcance.

La selección de 367 candidatos se volvió a contrastar con rutas sensibles, marcadores
de claves privadas y secretos generados de demo/releasecheck: cero coincidencias.
Se confirmaron exclusiones para `.docker-local`, `.env`, HANDOFF, bin/obj, claves,
certificados privados, bases y backups. Los informes de ejecución privados no se incluyen;
este informe público contiene únicamente evidencia y datos del simulador.
Los scripts referenciados por Compose, los Dockerfiles, el archivo compartido y
`.env.example` están presentes y no ignorados. HANDOFF permanece local; no se publica.

### Entornos temporales y detención sin eliminación

Ubicación y puertos confirmados leyendo etiquetas y PortBindings de los contenedores:

| Proyecto | Carpeta bajo `%TEMP%` | Puerto portal | Volúmenes conservados |
|---|---|---|---|
| `portalpedidos-cleancheck` | `PortalPedidos-clean-cuezx6ia` | 8371 | `portalpedidos-cleancheck_sql-data`, `portalpedidos-cleancheck_portal-keys` |
| `portalpedidos-releasecheck` | `PortalPedidos-release-99ce211f` | 8471 | `portalpedidos-releasecheck_sql-data`, `portalpedidos-releasecheck_portal-keys` |

Comandos para ejecutar cuando se desee detenerlos; **no se ejecutaron en esta revisión**:

```powershell
Push-Location "$env:TEMP\PortalPedidos-clean-cuezx6ia"
docker compose -p portalpedidos-cleancheck stop portal erp sql
Pop-Location

Push-Location "$env:TEMP\PortalPedidos-release-99ce211f"
docker compose -p portalpedidos-releasecheck stop portal erp sql
Pop-Location
```

`stop` conserva contenedores, volúmenes y datos. Mantener ambas carpetas privadas con
sus certificados. Para volver a arrancar, desde la carpeta correspondiente usar
`docker compose -p <nombre-del-proyecto> start sql erp portal`.
La carpeta `PortalPedidos-release-gr72ud_h` no tiene un proyecto Docker asociado.
Estos comandos no actúan sobre `portalpedidos-demo` ni sobre las bases Windows.

### Propuesta final

Título: **Preparar PortalPedidos como demo Docker instalable desde GitHub**.

Descripción: «Añade Compose aislable, configuración y certificados locales generados,
migraciones explícitas y persistencia para portal, ERP simulado y SQL Server. Corrige
PKCS a 10.0.13 y documenta instalación, operación y evidencia de copia limpia en esta
computadora. Incluye las capturas de portafolio existentes. Validados build sin caché,
auditoría NuGet, pedido integrado, reinicio e inicialización repetible. Quedan pendientes
el recorrido visual/JavaScript, el arranque real desde Visual Studio y otras plataformas;
las cuatro CS8602 preexistentes no se suprimen.»

No hay bloqueo técnico identificado para publicar el repositorio **como demo con esos
límites declarados**. No corresponde presentarlo como validado visualmente ni listo
para producción. No se hizo commit, push, detención, borrado ni cambio de bases Windows.
