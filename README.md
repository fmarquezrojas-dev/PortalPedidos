# PortalPedidos

Vendedores, supervisores y operadores usan PortalPedidos para saber quién atiende a cada cliente y si su pedido llegó al ERP, incluso cuando se interrumpe la conexión. El vendedor prepara un borrador con productos del catálogo. Al confirmar el envío, el portal marca el pedido como pendiente y guarda su mensaje de salida (Outbox) en la misma transacción. El **ERP simulado** recibe el pedido, reserva existencias si lo acepta o registra el motivo del rechazo. El vendedor ve el resultado en el detalle, el supervisor consulta los pedidos de su equipo y el operador atiende los envíos pendientes.

**Pedido #1004 · PEN 86,42.** El simulador aceptó siete unidades de TP0400 y la respuesta se perdió antes de llegar al portal. Mientras el operador veía **Resultado ERP por confirmar**, el portal consultó la referencia original. El pedido terminó **Confirmado**, con un solo recibo y siete unidades reservadas. [Seguir el pedido #1004](docker/VALIDATION.md#respuesta-perdida-después-de-la-aceptación) · [Ver qué ocurre cuando vence una suplencia](docker/VALIDATION.md#cartera-comercial-y-suplencias)

## Instalación

Desde la raíz del repositorio, en Windows PowerShell con Docker Desktop en modo Linux x64, Compose y el puerto local 8271 libre:

```powershell
Copy-Item .env.example .env
.\docker\Initialize-Demo.ps1
docker compose ps
```

El inicializador construye las aplicaciones .NET 10, prepara SQL Server Developer, aplica las migraciones y carga el catálogo y tres cuentas de demostración. Con los tres servicios saludables, configura la confianza del navegador en el certificado público de esa instalación y abre `https://localhost:8271/Account/Login`. La contraseña inicial de las cuentas se genera en `.docker-local/seed-password`. [Certificado y navegador](docker/README.md#certificados-y-navegador) · [Instalación con SQL Server y Visual Studio](docs/VISUAL-STUDIO.md)

**Recorrido en una instalación recién preparada**

1. Entra como `vendedor@portal.test` y abre **Nuevo pedido**. Busca y selecciona `C001` (Ferretería Central) y `P001` (Cemento Portland 42.5 kg). Con una unidad, **Guardar borrador** abre el detalle con el importe **PEN 32.00**.
2. Anota el número de pedido. Pulsa **Revisar y enviar**, marca la casilla de confirmación y pulsa **Confirmar envío**. Actualiza el detalle hasta ver **Confirmado**. En **Historial de estados** puedes seguir el paso de **Borrador** a **Pendiente de integración** y el resultado del ERP.
3. Cierra la sesión del vendedor, entra como `operador@portal.test` y abre **Integraciones**. **Atención operativa** muestra las operaciones abiertas. Para localizar un pedido ya confirmado por su número, abre **Monitor completo e historial** y consulta sus intentos.

## Funcionalidades

### Captura de pedidos

El formulario busca por código o nombre y muestra 20 coincidencias por página. «Ver más coincidencias» recupera la página siguiente del servidor. Al editar un borrador, recupera los productos y las cantidades guardados. Antes de encolar el envío, el servidor comprueba cliente, líneas, precios y existencias del catálogo sincronizado. El simulador decide la disponibilidad definitiva. El vendedor también puede cancelar un borrador o preparar uno nuevo a partir de un pedido rechazado.

### Atención operativa

En `/Integration/Attention`, operadores y administradores distinguen incidencias que requieren revisión, resultados por confirmar, plazos vencidos, demoras y envíos normales. Pueden filtrar por número, leer el último intento y consultar la próxima acción. Cuando se pierde la respuesta, el portal consulta primero la referencia original. Si el ERP ya guardó un recibo, recupera ese resultado. Si la consulta devuelve que el pedido no existe, puede volver a enviarlo. Las recuperaciones manuales autorizadas registran el motivo. [Prioridades y acciones del operador](docker/README.md#atención-operativa)

### Cartera comercial y suplencias

**Mi cartera** reúne los clientes del vendedor como titular o suplente vigente. **Cartera del equipo** permite al supervisor consultar los clientes de sus equipos y localizar suplencias próximas a vencer. En **Cartera comercial**, el administrador concede o revoca con motivo la cobertura de un cliente a otro vendedor activo del mismo equipo. Las fechas incluyen el primer y el último día completos, en horario de Lima.

La **cartera abierta** es el modo predeterminado. El vendedor puede preparar pedidos para cualquier cliente activo. Con `CustomerPortfolio:Restricted=true`, necesita titularidad o suplencia vigente. Si pierde la cobertura antes de confirmar, el servidor conserva las líneas del borrador y deniega el envío antes de crear el mensaje Outbox. Si la suplencia vence cuando el pedido ya está en cola, el portal continúa el envío o la consulta y conserva el titular de origen registrado en el borrador. [Comprobar una revocación con dos sesiones](docs/VISUAL-STUDIO.md#comprobar-una-suplencia)

El directorio de clientes, el catálogo, la gestión de usuarios y equipos, y la auditoría completan el recorrido. El **Asistente de búsqueda** interpreta expresiones como «pedidos rechazados de este mes» mediante reglas y expresiones regulares. Prepara filtros que el usuario puede abrir con los permisos de su cuenta. [Implementación de la búsqueda](src/Portal.Infrastructure/Services/Search/LocalSearchAssistant.cs)

## Arquitectura

La solución utiliza ASP.NET Core MVC y Razor, Identity, Entity Framework Core y SQL Server. El portal y el simulador tienen bases independientes. La comunicación entre ambos utiliza HTTPS, una clave API y referencias estables para reconocer un pedido repetido.

| Proyecto | Responsabilidad |
|---|---|
| [Portal.Web](src/Portal.Web/) | Interfaz MVC, autorización y trabajadores de sincronización y envío. |
| [Portal.Application](src/Portal.Application/) | Contratos de aplicación y datos que intercambian las capas. |
| [Portal.Domain](src/Portal.Domain/Portal.Domain/) | Entidades y estados del pedido y de la integración. |
| [Portal.Infrastructure](src/Portal.Infrastructure/) | Consultas y persistencia, reglas de cartera, seguimiento Outbox y cliente del ERP. |
| [ErpSimulator.Api](src/ErpSimulator.Api/) | Catálogo, existencias, reservas y recibos del simulador. |

El servicio de borradores comprueba la cobertura dentro de la transacción que pone el pedido en cola. El procesador Outbox coordina los envíos y las consultas por referencia. Al aceptar un pedido, el simulador guarda el recibo y la reserva de existencias en una sola transacción. [Servicio de borradores](src/Portal.Infrastructure/Services/Orders/OrderDraftService.cs) · [Procesador Outbox](src/Portal.Infrastructure/Integration/OrderOutboxProcessor.cs) · [Servicio de pedidos del ERP](src/ErpSimulator.Api/Services/Orders/ErpOrderService.cs)

## Evidencia

| Escenario reproducido en Docker | Resultado |
|---|---|
| Dos usuarios editan el mismo borrador en Chromium | La segunda escritura recibe HTTP 409 y la recarga recupera los valores guardados. |
| El ERP está detenido al enviar | El pedido permanece en cola. Al volver el ERP, la consulta y el reintento producen un único recibo. |
| Dos administradores registran suplencias superpuestas | Se guarda una suplencia y la otra solicitud recibe HTTP 409. |
| Revocación o vencimiento antes del envío | En cada caso SQL conserva dos líneas y registra cero mensajes Outbox para el borrador. |
| Vencimiento después de encolar | El ERP confirma el pedido ya encolado con un recibo y una reserva de dos unidades. |

El informe incluye los identificadores del simulador, los resultados observados en Chromium y las comprobaciones en SQL. La solución se compiló en Windows. El recorrido interactivo con F5 queda pendiente. [Informe de validación](docker/VALIDATION.md) · [Reproducir las pruebas](docker/README.md#pruebas-de-captura-de-pedidos)

<details>
<summary>Capturas del portal y de un pedido confirmado</summary>

Las capturas de la galería corresponden a una demostración anterior. El detalle del pedido **#1008** muestra el resultado y su referencia en el ERP simulado.

![Detalle del pedido 1008 confirmado](docs/IMAGES/Pedido%20detalle.png)

![Referencia ERP e historial del pedido 1008](docs/IMAGES/Referencia%20ERP%20Pedido.png)

[Galería completa](docs/IMAGES/)

</details>

## Autor

**Fabrizio Antonio Marquez Rojas**

Desarrollador de software y estudiante de Ingeniería de Software, con interés en desarrollo backend con .NET, bases de datos e integraciones empresariales. [Perfil en GitHub](https://github.com/fmarquezrojas-dev)
