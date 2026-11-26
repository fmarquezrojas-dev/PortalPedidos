# Validación de PortalPedidos

La verificación completa del **26 de noviembre de 2026, hora de Lima**, pasó en Docker con SQL Server, el portal y el ERP simulado. Chromium recorrió las pantallas de captura, atención y cartera. Las pruebas de formularios HTTPS y las consultas SQL comprobaron las transiciones del pedido y su efecto en el ERP. Los datos son sintéticos. En Windows se comprobó la compilación de la solución. El recorrido interactivo con **F5** sigue pendiente.

| Para seguir un caso | Evidencia |
|---|---|
| El ERP acepta el pedido y el portal pierde la respuesta | [Pedido #1004, consulta por referencia y una sola reserva](#respuesta-perdida-después-de-la-aceptación) |
| El ERP está detenido antes de recibir el pedido | [Pedido #1002, consulta sin recibo y reenvío](#interrupción-antes-de-la-recepción) |
| El vendedor pierde la cobertura antes o después del envío | [Borradores, Outbox y recibo tras el vencimiento](#cartera-comercial-y-suplencias) |

## Entorno y reproducción

Para revisar una entrega, consulta el SHA completo, los comandos ejecutados, los resultados SQL y el estado de Actions en el [PR de publicación](https://github.com/fmarquezrojas-dev/PortalPedidos/pulls?q=is%3Apr+head%3Aentrega%2Fcaptura-atencion-cartera). Desde un clon con historial, usa `git switch --detach <SHA-del-PR>` y prepara Python y Chromium según la guía. Ejecuta `Test-Capture.ps1 -Python $pythonCaptura -Portfolio` para instalar en bases vacías y recorrer los tres bloques. En otra ejecución, `-MeasureBaseline` instala primero `c53707c`, registra un pedido confirmado, equipos y responsables, y compara huellas SQL de clientes, membresías, asignaciones, pedidos y líneas al migrar. La suite comprueba que un vendedor puede seguir eligiendo un cliente sin asignar. Actions compila en Release y ejecuta esta segunda variante con captura en Chromium. El recorrido interactivo con F5 se revisa por separado.

La [validación íntegra de la publicación anterior](https://github.com/fmarquezrojas-dev/PortalPedidos/blob/c53707cad68d765361543cb7f8c0ef225697f306/docker/VALIDATION.md) conserva las decisiones y los resultados de noviembre previos a estos tres bloques.

Windows PowerShell 5.1, SDK .NET 10.0.403, Docker Engine 29.9.2 en Linux amd64, Compose 5.6.1, SQL Server 2022 CU22 Developer, Python 3.13.17, Playwright 1.56.0 y Chromium headless 141.0.7390.37. El proyecto Compose crea red, volúmenes, certificados, credenciales y puerto de bucle local propios. Las bases son `PortalPedidosDockerDemo` y `ErpSimulatorDockerDemo` en `sql,1433`. Se aplicaron cinco migraciones del portal y dos del simulador.

Ejecuta estos comandos en la raíz del repositorio después de preparar el entorno de prueba. [Instalar Python y Chromium para las pruebas](README.md#pruebas-de-captura-de-pedidos)

```powershell
dotnet build PortalPedidos.slnx -c Release
$pythonCaptura = (Resolve-Path .\artifacts\capture-venv\Scripts\python.exe).Path
.\docker\Test-Capture.ps1 -Python $pythonCaptura -Portfolio
```

El [ejecutor](Test-Capture.ps1) prueba captura y atención con cartera abierta, y después prueba suplencias en modo restringido. Termina deteniendo los servicios de la instancia. Cada ejecución escribe sus informes en `artifacts/portalpedidos-capture-*/`. La del 26 de noviembre (`portalpedidos-capture-a14ae79574a2`) aprobó los ocho grupos HTTP y reprodujo la consulta tras perder la respuesta, los dos borradores bloqueados y la confirmación tras vencer la suplencia. Para seguir un pedido de principio a fin, las tablas usan los identificadores y mediciones de la ejecución del 25 de noviembre (`portalpedidos-capture-925c36381152`), salvo la comparación histórica del formulario. Los sufijos horarios de los informes HTTP están en UTC. [Registros de la demostración del 25 de noviembre](../docs/evidencia/2026-11-25/)

## Captura de pedidos

La muestra contenía 510 clientes y 530 productos, de los cuales 509 clientes eran elegibles y 529 productos estaban activos. **Nuevo pedido** abrió con cero opciones de catálogo precargadas y 18 888 bytes de HTML. Una búsqueda por prefijo devolvió hasta 20 coincidencias. La consulta SQL obtiene hasta 21 filas para informar si hay más resultados. Una búsqueda por código exacto tiene prioridad sobre una coincidencia parcial.

| Prefijo | Opciones | Bytes del cuerpo JSON | Mediana HTTPS local | Última ejecución SQL |
|---|---:|---:|---:|---:|
| Clientes, `TC` | 20 | 1 873 | 13,08 ms | 775 µs |
| Productos, `TP` | 20 | 2 293 | 8,17 ms | 604 µs |

Cada mediana utiliza las últimas cinco de seis solicitudes sobre la misma conexión. SQL se midió con `sys.dm_exec_query_stats` y las etiquetas de consulta de EF. Son mediciones de un vendedor en la máquina de prueba. Como referencia histórica, `-MeasureBaseline` comparó `c53707c` y la primera implementación de búsqueda sobre la misma base. El formulario pasó de 1 567 opciones con identificador y 322 511 bytes a cero opciones y 18 487 bytes, una reducción del 94,3 % en el cuerpo HTML.

**Chromium** comprobó la selección por teclado, la búsqueda sin coincidencias, el fallo de una petición y la llegada tardía de una respuesta anterior. Al reabrir un borrador de dos líneas, el formulario recuperó el cliente y sus dos productos. Dos editores abrieron el mismo borrador. La segunda escritura recibió HTTP 409 y la recarga mostró el cambio guardado por el primero. Con cliente y producto desactivados y precio actualizado en el ERP, la corrección guardó cuatro unidades de TP0500 y cinco de TP0498 por PEN 119,46. El operador pudo leer el detalle y recibió 403 al intentar capturar. [Prueba de navegador](capture_browser.py) · [Medición HTTP y SQL](capture-test.py)

### Interrupción antes de la recepción

La [prueba funcional HTTP](functional-test.py) detuvo el ERP antes del envío del pedido **#1002** para C009. El portal conservó el estado `Queued`. El Outbox pasó a `Unknown` y registró `ERP_CONNECTION_FAILED`. Al volver el simulador, la consulta por referencia devolvió 404. El trabajador reenvió el pedido y obtuvo `ORDER_ACCEPTED`. La referencia ERP **SIM-7ad22644c9194479ba19b28d962e9570** coincidió en el pedido y en el recibo del simulador. Las reservas aumentaron en dos P013 y tres P014. Repetir el POST de confirmación conservó un solo mensaje y un solo recibo.

La prueba registró ocho grupos aprobados y cero fallidos. Incluyen permisos administrativos y del vendedor, rechazo de cantidad cero, reapertura del borrador, historial, auditoría, intentos y cierre de sesión. [Registro HTTP y SQL del pedido #1002](../docs/evidencia/2026-11-25/functional-report-20261126T002718Z.json)

### Respuesta perdida después de la aceptación

El vendedor confirmó en Chromium el pedido **#1004** por **PEN 86,42**, con siete unidades de TP0400. El simulador interrumpió la respuesta **después** de guardar la aceptación. Una interrupción en ese punto deja al portal sin saber si debe reenviar.

| Registro observado | Resultado |
|---|---|
| Pedido y clave de idempotencia | `ff685fc5-2b94-4061-81ed-87b36db95bc2` |
| Mensaje Outbox | `7cd28412-7a4a-4ee4-9199-23f4efbac948` |
| Atención operativa | El operador vio **Resultado ERP por confirmar** durante la incertidumbre. |
| Intentos persistidos | `Submit / ERP_CONNECTION_FAILED`, seguido de `Lookup / HTTP 200 / ORDER_ACCEPTED`. |
| Resultado y efecto comercial | Portal `Confirmed`, recibo ERP `Accepted`, referencia **SIM-77a1d72455194c5cbf705be9e7c67901**. Existencia física 1 000. Reserva de 0 a 7. |

Desde la creación del borrador hasta la última confirmación, el total aumentó en **un pedido, un mensaje y un recibo**. Repetir la confirmación durante el procesamiento y después de recibir el resultado conservó esos totales. El registro conserva las lecturas antes y después, los identificadores y los intentos. [Registro del pedido #1004](../docs/evidencia/2026-11-25/capture-lost-response.json) · [Simulación de respuesta perdida](../src/ErpSimulator.Api/Development/LoseOrderResponseOnceFilter.cs) · [Procesador Outbox](../src/Portal.Infrastructure/Integration/OrderOutboxProcessor.cs)

## Atención operativa

Para comprobar prioridades y paginación, la prueba cargó 29 casos en la base aislada con el envío automático detenido. La recuperación concurrente utilizó dos sesiones y un recibo que el simulador ya había emitido. [Datos de la bandeja](operational-fixture.sql)

| Caso en `/Integration/Attention` | Resultado |
|---|---|
| Prioridad y paginación | Chromium recorrió 22 mensajes que requerían revisión en páginas de 20 y 2. Al volver del detalle conservó la segunda página. El pedido #1032 pasó a demora al superar cinco minutos en cola, con la misma versión de fila. |
| Procesamiento y reintentos | La bandeja distinguió el plazo vencido de #1028, el plazo de procesamiento aún vigente de #1029 y los reintentos futuros de los que ya podían ejecutarse. El rechazo comercial siguió disponible en el historial. |
| Recuperación concurrente | Dos sesiones de la cuenta operadora abrieron la misma confirmación. La confirmación antigua recibió 409. La secuencia registrada fue `Submit → Lookup → Recovery → Lookup`. |
| Acceso y errores | El operador obtuvo 200 y el vendedor 403. Una confirmación sin token antifalsificación recibió 400. Al dejar temporalmente fuera de servicio la tabla de intentos en la base de prueba, la pantalla devolvió 503 y mostró «No se pudo consultar la bandeja». |
| Consultas de lectura | Los filtros y la navegación conservaron 32 mensajes, ocho intentos, tres recibos y 13 unidades reservadas. Se comprobaron primera sincronización pendiente, catálogo vigente, vencido y fecha adelantada. |

[Registro de atención operativa](../docs/evidencia/2026-11-25/capture-operational.json) · [Casos operativos](operational_test.py) · [Guía de prioridades](README.md#atención-operativa)

## Cartera comercial y suplencias

Se aplicó `20261125234634_AddCustomerCoverages`. La prueba creó dos equipos y utilizó cuentas separadas de administrador, supervisor y vendedor. Los borradores se abrieron desde la ficha del cliente y las concesiones y revocaciones se comprobaron en el historial.

| Caso | Comprobación |
|---|---|
| Alcance de las carteras | **Mi cartera** mostró 23 clientes del filtro en páginas de 20 y 3. **Cartera del equipo** mostró próximos vencimientos al supervisor del equipo. Un supervisor ajeno recibió 404 al consultar la ficha. |
| Dos administradores y fechas superpuestas | Dos sesiones enviaron las altas simultáneamente. Se guardó una suplencia. La solicitud en conflicto recibió 409 y la ficha mostró el conflicto. |
| Revocación entre apertura y confirmación de envío | El POST antiguo devolvió 409. SQL conservó el estado `Draft`, las dos líneas con sus cantidades y precios y **cero mensajes Outbox**. El vendedor pudo editar y elegir otro cliente autorizado. |
| Vencimiento antes de confirmar | Al confirmar otro borrador, el servidor devolvió 409 con sus dos líneas intactas y **cero mensajes Outbox**. La prueba con reloj controlado verificó los dos extremos del intervalo. El permiso empezó a las 00:00 del primer día en Lima, seguía vigente el viernes a las 23:59:59.999 y terminó el sábado a las 00:00. |
| Vencimiento después de encolar | La prueba mantuvo detenido el envío automático, encoló el pedido durante la vigencia y adelantó el fin de la cobertura en la base aislada. Tras el vencimiento, reactivó el envío y el pedido pasó a `Confirmed`. El ERP emitió un único recibo, **SIM-7d019e2d7b6a4ebf973379681c2a3290**, y reservó dos unidades. La reasignación posterior conservó el titular histórico, el vendedor creador y el equipo del pedido. |

[Registro de revocación y vencimiento](../docs/evidencia/2026-11-25/capture-portfolio.json) · [Prueba de cartera](portfolio_test.py) · [Prueba del límite horario](PortfolioClockCheck/Program.cs) · [Recorrido manual](../docs/VISUAL-STUDIO.md#comprobar-una-suplencia)

## Instalación, persistencia y límites

- **Instalación limpia en Docker, 14 de noviembre de 2026.** `docker compose build --no-cache erp portal` reconstruyó las aplicaciones con imágenes base disponibles. La semilla preparó tres cuentas, seis roles, diez clientes y treinta productos con existencias. En aquel esquema había cuatro migraciones del portal y dos del ERP. El pedido #1001 se confirmó, con un recibo y una unidad de P001 reservada.
- **Persistencia.** Tras `docker compose restart sql erp portal` y `docker compose up -d --wait sql erp portal`, `smoke-test.py --verify-existing` recuperó el pedido y la sesión anteriores. Repetir `Initialize-Demo.ps1 -SkipBuild` conservó los datos, las contraseñas y los certificados. [Prueba HTTP](smoke-test.py) · [Consultas SQL](verify-data.sql)
- **Transporte y permisos.** HTTPS validó nombre y CA. Un nombre incorrecto falló la validación TLS. La API ERP sin clave devolvió 401 y el POST sin token antifalsificación devolvió 400. Las conexiones de aplicación a SQL utilizaron `Encrypt=True;TrustServerCertificate=False`. Los comandos de migración Docker rechazaron destinos distintos de las bases de demostración.
- **Dependencias.** El 26 de noviembre, `dotnet nuget why PortalPedidos.slnx System.Security.Cryptography.Pkcs --framework net10.0` confirmó PKCS 10.0.13. `dotnet list PortalPedidos.slnx package --vulnerable --include-transitive --no-restore` no identificó paquetes vulnerables en los orígenes consultados.
- **Windows y Visual Studio.** El 26 de noviembre, `dotnet build PortalPedidos.slnx -c Release --no-incremental` compiló los cinco proyectos en Windows con cero errores y cuatro avisos CS8602 en `ErpCatalogValidator.cs`. EF Core 10.0.12 enumeró cinco migraciones del portal y dos del simulador con `migrations list --no-connect`. Se revisaron los dos perfiles HTTPS del inicio conjunto. [Procedimiento F5 con SQL Server para Windows](../docs/VISUAL-STUDIO.md#iniciar-con-f5)
- **Alcance.** Las pruebas de navegador corresponden a Chromium headless sobre la instalación Docker. La galería ilustra otra demostración. La evidencia utiliza un ERP simulado y una carga sintética local. Quedan fuera de las mediciones el rendimiento concurrente a escala de producción y la instalación en otros sistemas anfitriones.
