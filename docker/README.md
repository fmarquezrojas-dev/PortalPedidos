# PortalPedidos en Docker (demostración local)

La demostración ejecuta el portal, el ERP simulado y SQL Server en tres servicios de Compose. Los datos de las dos aplicaciones se guardan en un volumen propio. Para trabajar con SQL Server de Windows, sigue la [guía de Visual Studio](../docs/VISUAL-STUDIO.md).
Requiere Docker Desktop con contenedores Linux x64, Compose, acceso a los registros
de imágenes y el puerto local 8271 libre. SQL Server usa la edición Developer.

## Primera preparación

Desde la raíz del repositorio, en PowerShell:

```powershell
Copy-Item .env.example .env
.\docker\Initialize-Demo.ps1
```

El script valida Compose, construye las dos imágenes, genera certificados y secretos,
inicia SQL, aplica migraciones y semillas, comprueba ERP, sincroniza y arranca el portal.
Se detiene si falla un paso. `-SkipBuild` permite reutilizar imágenes ya construidas.
La primera descarga de SQL Server y del SDK puede tardar varios minutos.

El proyecto Compose es `portalpedidos-demo`. Solo el portal publica un puerto:
`127.0.0.1:8271 -> 8443`. ERP y SQL son accesibles por la red del proyecto.
Las bases son exclusivamente `PortalPedidosDockerDemo` y `ErpSimulatorDockerDemo`
en `sql,1433`. Los volúmenes son `portalpedidos-demo_sql-data` y
`portalpedidos-demo_portal-keys`; la red predeterminada también lleva el prefijo del proyecto.
No usar este nombre para otra instalación. Para una segunda copia, **antes de inicializar**,
crea `.env` con estos valores (desde la raíz de esa copia):

```powershell
@"
COMPOSE_PROJECT_NAME=portalpedidos-cleancheck
PORTAL_HTTPS_PORT=8371
"@ | Set-Content .env -Encoding ascii
.\docker\Initialize-Demo.ps1
```

Todos los comandos `docker compose` ejecutados desde esa carpeta leen `.env`.
El inicializador resuelve esa misma configuración, incluidas imágenes, red y volúmenes
con el prefijo `portalpedidos-cleancheck`; solo publica `127.0.0.1:8371`.
No reutilices `.docker-local` de otra instalación. Evita variables de sesión
`COMPOSE_PROJECT_NAME` / `PORTAL_HTTPS_PORT` que sobreescriban `.env`.
No cambies el nombre después de inicializar: separaría los volúmenes de sus certificados.
Para probar la segunda instancia usa `python -B docker/smoke-test.py --port 8371`;
para verificar el pedido guardado agrega `--verify-existing`. La prueba adicional se
invoca con `python -B docker/functional-test.py --project-name portalpedidos-cleancheck --port 8371`
y detiene brevemente solo su ERP. Usa perfiles de navegador separados si abres dos
instancias demo: las cookies de localhost no se aíslan por puerto.

## Certificados y navegador

La tarea `setup` genera una CA privada y certificados para `portal`, `erp` y `sql`;
cada certificado incluye además `localhost` en su SAN. Los certificados de servidor
duran 180 días y la CA 365 días. No se regeneran al repetir la preparación.
Las aplicaciones confían en la CA pública mediante un bundle que conserva las raíces
del sistema. `Erp:BaseUrl` es `https://erp:8443/`; no se desactiva la validación TLS.
SQL fuerza TLS 1.2 y las conexiones usan `Encrypt=True;TrustServerCertificate=False`.
El sondeo SQL local usa `sqlcmd -C`; las conexiones reales de las aplicaciones sí validan
el certificado y el nombre `sql`.

Para que el navegador confíe en el portal, importa **solo el certificado público**
`.docker-local/ca.crt` en el almacén de entidades raíz de confianza del usuario de Windows.
Este paso modifica la confianza del usuario y no lo realiza el script automáticamente:

```powershell
Import-Certificate -FilePath .\.docker-local\ca.crt -CertStoreLocation Cert:\CurrentUser\Root
```

Después abre **https://localhost:8271/Account/Login**. No omitas las advertencias TLS.
Un navegador con almacén propio podría necesitar importar allí el mismo certificado.
No exportes ni distribuyas `ca.key`. Conserva los certificados junto con los volúmenes:
la clave de Data Protection está cifrada con el certificado del portal. Su renovación
requiere conservar la capacidad de descifrar las claves anteriores; no borres esta carpeta
para renovar certificados ni para resolver problemas de arranque.

## Secretos y usuarios

`.docker-local/` está excluida de Git y del contexto de construcción. Compose monta
los JSON mediante archivos secretos; las contraseñas no se interpolan en el YAML ni
se incorporan a las imágenes. La contraseña de SQL se lee desde archivo al iniciar SQL.
La contraseña inicial de usuarios solo se monta en el contenedor temporal de preparación.
Protege los permisos Windows de esta carpeta: los secretos de Compose local son archivos,
no un almacén cifrado. No publiques su contenido ni uses comandos que lo impriman en logs.

Se crean `admin@portal.test`, `vendedor@portal.test` y `operador@portal.test` con roles
respectivos y una contraseña aleatoria compartida solo para esta demo. Consulta localmente
`.docker-local/seed-password` con tu editor para iniciar sesión. Las contraseñas existentes
no se sobrescriben al repetir la preparación. La cuenta SQL `sa` se utiliza únicamente
en esta instancia aislada de demostración, sin puerto SQL publicado; este esquema de
credenciales no está diseñado para producción.

La cookie de autenticación utiliza Secure, HttpOnly y SameSite=Lax. Identity valida la sesión y su vigencia.
Docker usa `PortalPedidos.Demo.Auth`, `PortalPedidos.Demo.Antiforgery` y
`PortalPedidos.Demo.TempData` para convivir
con Visual Studio en localhost. Data Protection tiene nombre de aplicación propio,
claves persistentes y cifrado con certificado. Las aplicaciones se ejecutan como usuario
no root (1654); solo la tarea de preparación inicial necesita root para preparar permisos.

## Migraciones y datos

El modo demo exige `PORTAL_DOCKER_DEMO=true`, `Development` y ejecución en contenedor.
Carga una configuración separada y rechaza un servidor o nombre de base inesperado.
`--demo-migrate` no funciona fuera de ese modo. No se aplican migraciones en cada arranque.
Se aplican las migraciones del portal, incluida `AddCustomerCoverages`, y las del ERP.

La preparación ejecuta, por orden:

1. SQL saludable.
2. `erp --demo-migrate` y `portal --demo-migrate`.
3. `erp --seed-catalog`: 10 clientes, 30 productos y 30 existencias.
4. `portal --seed-roles --seed-users`: roles y tres cuentas.
5. ERP saludable, `portal --check-erp`, `portal --sync-catalog`.
6. Portal saludable; sincronización de catálogo y outbox habilitados.

Los comandos se ejecutan mediante `docker compose run --rm --no-deps`; `--rm` elimina
únicamente cada contenedor temporal creado por ese comando, nunca los servicios ni volúmenes.
Las semillas existentes preservan datos inicializados y fallan ante ciertos estados
inconsistentes. No crean equipos comerciales, carteras ni pedidos: puedes prepararlos
desde la interfaz. El script de prueba de pedidos descrito más abajo sí crea un pedido demo.

## Arranque, estado y detención

Para arrancar los servicios y comprobar su estado tras la primera preparación, ejecuta estos comandos.

```powershell
docker compose up -d --wait sql erp portal
docker compose ps
```

Al terminar, `docker compose stop portal erp sql` detiene la demostración y conserva las bases, los certificados y las claves. Para comprobar que los servicios vuelven a estar disponibles tras un reinicio, ejecuta estos comandos.

```powershell
docker compose restart sql erp portal
docker compose up -d --wait sql erp portal
```

Para actualizar desde la demo anterior, conserva el nombre del proyecto, `.env`, los volúmenes y `.docker-local`. Con la copia de seguridad de tus datos hecha, ejecuta `docker compose stop portal erp`, cambia al código nuevo y ejecuta `.\docker\Initialize-Demo.ps1`. El inicializador reconstruye, migra y arranca ambos servicios. `AddCustomerCoverages` crea las suplencias y añade la referencia opcional `SalesOrders.CustomerAssignmentId`. Los pedidos anteriores conservan la referencia a su cliente y empiezan con la nueva columna nula. La cartera continúa abierta por defecto.

## Verificación

```powershell
docker compose config --quiet
docker compose exec portal curl --fail --silent --cacert /https/ca.crt https://localhost:8443/health/ready
docker compose exec erp curl --fail --silent --cacert /https/ca.crt https://localhost:8443/health/ready
docker compose run --rm --no-deps portal --check-erp
```

`/health/live` comprueba el proceso y `/health/ready` la conexión con la base;
no sustituyen la comprobación de migraciones, catálogo y envío de pedidos.
El [informe de validación](VALIDATION.md) incluye los pedidos de prueba, sus recibos en el ERP y las diferencias de existencias.

Con Python 3 puedes probar los formularios HTTPS reales (crea y envía un pedido C001/P001):

```powershell
python docker/smoke-test.py
docker compose restart sql erp portal
docker compose up -d --wait sql erp portal
python docker/smoke-test.py --verify-existing
```

La segunda invocación comprueba el mismo pedido y la sesión guardada; no crea otro pedido.
No automatiza JavaScript ni inspecciona la apariencia del navegador. Las cookies de prueba
se guardan únicamente en `.docker-local/`. Para consultar el estado de ambas bases:

```powershell
Get-Content docker/verify-data.sql -Raw | docker compose exec -T sql bash -c 'SQLCMDPASSWORD=$(cat /run/secrets/sql-password); export SQLCMDPASSWORD; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -W'
```

## Pruebas de Captura de pedidos

El ejecutor necesita un clon del repositorio con Git, Docker Desktop en modo Linux, el SDK de .NET 10 y Python 3.13. Para medir la referencia histórica, el clon debe incluir el commit `c53707c`. Ejecuta los comandos desde la raíz en PowerShell.

```powershell
dotnet build PortalPedidos.slnx -c Release
python -m venv artifacts/capture-venv
$pythonCaptura = (Resolve-Path .\artifacts\capture-venv\Scripts\python.exe).Path
& $pythonCaptura -m pip install -r docker/capture-requirements.txt
& $pythonCaptura -m playwright install --only-shell chromium
.\docker\Test-Capture.ps1 -Python $pythonCaptura -MeasureBaseline
```

El script prepara una copia en la carpeta temporal, elige un puerto libre y crea un proyecto Compose exclusivo. Con `-MeasureBaseline` arranca primero el portal de `c53707c`, toma la medición y después prueba el árbol de trabajo sobre la misma base. Puedes omitir el parámetro para ejecutar directamente la implementación actual. Playwright 1.56.0 y su Chromium headless solo se instalan para las pruebas. El navegador reconoce la clave pública del certificado de esa copia. La prueba HTTP valida el nombre TLS y la CA.

La muestra agrega 500 clientes, 500 productos y sus existencias al ERP simulado. Se ejecutan las pruebas HTTP, la captura con JavaScript, dos sesiones de edición y la pérdida de respuesta tras aceptar. Los casos que interrumpen o reconfiguran el ERP se ejecutan en secuencia. El ejecutor restaura la configuración de la falla y detiene sus servicios al finalizar. La carpeta temporal, las imágenes y los volúmenes conservan la ejecución para inspeccionarla.

Los informes JSON se copian a `artifacts/portalpedidos-capture-*/`. Incluyen conteos y bytes del HTML y las búsquedas, tiempos observados, precios, intentos de integración, referencia ERP y diferencias de reserva. El [workflow de captura](../.github/workflows/capture.yml) está configurado con `-MeasureBaseline` para adjuntar los JSON de captura. La configuración privada y las cookies quedan en la carpeta temporal. [Resultados y método de medición](VALIDATION.md#captura-de-pedidos).

Para repetir solo la respuesta perdida, entra en la copia temporal que imprimió el ejecutor. Cada ejecución crea un pedido y reserva siete unidades de TP0400 en esa copia.

```powershell
docker compose up -d --wait sql erp portal
& $pythonCaptura -B docker/capture-test.py lost-response
docker compose stop portal erp sql
```

El resultado de esa ejecución individual queda en `.docker-local/capture-lost-response.json`.

## Atención operativa

Entra como operador o administrador y abre **Integraciones** en el menú lateral. La pantalla **Atención operativa** corresponde a `/Integration/Attention`. La bandeja usa la primera regla aplicable de esta tabla. Dentro de cada prioridad, muestra primero el mensaje más antiguo. Cada contador abre su selección completa y conserva la búsqueda por número. Las fechas se presentan en hora de Perú.

| Prioridad | Cuándo aparece | Qué hacer |
|---|---|---|
| Requiere revisión | Mensaje `DeadLetter`. El procesamiento se detuvo. | Abre el pedido desde la bandeja, lee el último código y revisa los intentos. Si aparece **Revisar recuperación**, corrige la causa, abre la previsualización, escribe el motivo y confirma. Si la versión cambió, vuelve a consultar el seguimiento. |
| Resultado ERP por confirmar | Mensaje `Unknown`. El ERP podría haber aceptado el pedido. | Espera la comprobación indicada. El trabajador consulta la referencia original. El detalle registra el resultado de la consulta. |
| Procesamiento con plazo vencido | Mensaje `Processing` cuyo plazo terminó, o sin plazo asignado. | El portal consultará la referencia original al retomar el mensaje. Si el plazo sigue vencido al actualizar, comprueba que el servicio del portal esté en ejecución. |
| Pendiente con demora | Otra operación abierta lleva **más de cinco minutos** desde que se encoló. El resumen del portal utiliza el mismo umbral. | En un reintento futuro, espera la hora indicada. Si ya llegó, comprueba si hay un intento nuevo al actualizar. Si ya está en proceso, consulta el fin del plazo indicado. |
| Seguimiento normal | Operación abierta restante, con hasta cinco minutos en cola. | Espera el primer envío, el fin del procesamiento o el reintento previsto. |

El tiempo en cola se mide desde la creación del mensaje Outbox. En revisión también aparece el tiempo desde la detención cuando el procesador la registró. El último intento corresponde a un envío o consulta HTTP. **Monitor completo e historial** (`/Integration`) permite buscar también el resultado definitivo y distinguir un rechazo comercial de una incidencia técnica.

El indicador **Catálogo global** es independiente del filtro por pedido. El envío requiere versión positiva y una sincronización correcta entre cinco minutos antes y un minuto después de la hora UTC de consulta, incluidos ambos límites. Ante la primera carga pendiente o la pérdida de vigencia, revisa la fecha y el último código disponible del sincronizador.

Para reproducir la validación en una copia nueva con el entorno Python preparado en la sección anterior, ejecuta:

```powershell
.\docker\Test-Capture.ps1 -Python $pythonCaptura -Operational
```

El ejecutor prueba captura, interrupción del ERP y respuesta perdida antes de los casos de atención operativa. Guarda los informes en `artifacts/portalpedidos-capture-*/` y detiene los servicios de la copia.

## Cartera comercial y suplencias

Con la configuración inicial, un vendedor puede elegir cualquier cliente activo. Las pantallas **Mi cartera** (`/MyPortfolio`) y **Cartera del equipo** (`/TeamPortfolio`) muestran las asignaciones del vendedor o del supervisor. Para ensayar una revocación, la prueba activa `CustomerPortfolio:Restricted` en su propia instancia Docker. Los pasos para reproducirla a mano están en la [guía de suplencias](../docs/VISUAL-STUDIO.md#comprobar-una-suplencia).

Desde la raíz, con el entorno Python de captura preparado, ejecuta:

```powershell
.\docker\Test-Capture.ps1 -Python $pythonCaptura -Portfolio
```

La ejecución prueba primero captura y atención operativa con la cartera abierta. Después crea dos equipos en la copia aislada, activa la restricción y comprueba las vistas por rol, dos altas concurrentes, los límites de fecha en Lima y el envío tras revocación o vencimiento. Compara las líneas guardadas y los mensajes Outbox en SQL, y comprueba el recibo y la reserva del ERP para un pedido encolado durante la suplencia. El resultado se guarda en `capture-portfolio.json` junto con las regresiones.
