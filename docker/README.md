# PortalPedidos en Docker (demostración local)

Esta alternativa mantiene `PortalPedidos.slnx`, los perfiles de Visual Studio y
los User Secrets de Windows. No utiliza el SQL Server de Windows ni copia sus datos.
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
Para probar esta copia usa `python -B docker/smoke-test.py --port 8371`;
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
`.docker-local/ca.crt` en las entidades raíz de confianza de tu usuario Windows.
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

Se conservan cookies Secure, HttpOnly, SameSite, expiración y validación de Identity.
Docker usa `PortalPedidos.Demo.Auth`, `PortalPedidos.Demo.Antiforgery` y
`PortalPedidos.Demo.TempData` para convivir
con Visual Studio en localhost. Data Protection tiene nombre de aplicación propio,
claves persistentes y cifrado con certificado. Las aplicaciones se ejecutan como usuario
no root (1654); solo la tarea de preparación inicial necesita root para preparar permisos.

## Migraciones y datos

El modo demo exige `PORTAL_DOCKER_DEMO=true`, `Development` y ejecución en contenedor.
Carga una configuración separada y rechaza un servidor o nombre de base inesperado.
`--demo-migrate` no funciona fuera de ese modo. No se aplican migraciones en cada arranque.
Se utilizan las cuatro migraciones existentes del portal y las dos del ERP.

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

Tras completar la primera preparación:

```powershell
docker compose up -d --wait sql erp portal
docker compose ps
docker compose stop portal erp sql
```

La detención conserva contenedores, bases, certificados y claves. Para reiniciar:

```powershell
docker compose restart sql erp portal
docker compose up -d --wait sql erp portal
```

No son necesarios `down`, `down -v`, eliminaciones ni `prune`. No ejecutes otras copias
del inicializador simultáneamente. Para actualizar código, reconstruye las imágenes con
`docker compose build erp portal` y aplica el flujo de preparación si hay migraciones nuevas.

## Verificación

```powershell
docker compose config --quiet
docker compose exec portal curl --fail --silent --cacert /https/ca.crt https://localhost:8443/health/ready
docker compose exec erp curl --fail --silent --cacert /https/ca.crt https://localhost:8443/health/ready
docker compose run --rm --no-deps portal --check-erp
```

`/health/live` comprueba el proceso y `/health/ready` la conexión con la base;
no sustituyen la comprobación de migraciones, catálogo y envío de pedidos.
El estado y las pruebas efectivamente realizadas durante la implementación se documentan
en `docker/VALIDATION.md`.

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
