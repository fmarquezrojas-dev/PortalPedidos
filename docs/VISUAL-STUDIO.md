# PortalPedidos en Visual Studio

El perfil compartido **Portal completo** inicia el portal y el simulador con HTTPS. Esta instalación utiliza dos bases de desarrollo en SQL Server para Windows. Los comandos se ejecutan desde la raíz del repositorio en PowerShell.

## Preparar el entorno

Instala Visual Studio 2026 con la carga de trabajo de ASP.NET, el SDK de .NET 10 y una instancia de SQL Server con permisos para crear y migrar las dos bases. Configura la confianza en el certificado TLS de SQL Server para el nombre que se usará en las conexiones.

```powershell
dotnet restore PortalPedidos.slnx
dotnet dev-certs https --trust
$env:ASPNETCORE_ENVIRONMENT = "Development"
```

En **Administrar secretos de usuario** de cada proyecto ejecutable, configura estos valores. Sustituye `SERVIDOR_SQL` por el nombre del servidor y `CLAVE_COMPARTIDA` por la misma clave aleatoria en ambos proyectos. Una clave de 32 bytes aleatorios codificados en Base64 cumple la longitud que exige el simulador (32 a 512 caracteres). Los ejemplos usan autenticación integrada de Windows.

**Portal.Web**

```json
{
  "ConnectionStrings": {
    "PortalDb": "Server=SERVIDOR_SQL;Database=PortalPedidosDev;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
  },
  "Erp": {
    "BaseUrl": "https://localhost:7117/",
    "ApiKey": "CLAVE_COMPARTIDA"
  }
}
```

**ErpSimulator.Api**

```json
{
  "ConnectionStrings": {
    "ErpSimulatorDb": "Server=SERVIDOR_SQL;Database=ErpSimulatorDev;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
  },
  "Simulator": {
    "ApiKey": "CLAVE_COMPARTIDA"
  }
}
```

## Cargar esquema y datos

Utiliza la CLI de EF Core compatible con los paquetes 10.0.12 de la solución. Si falta la herramienta, instálala con `dotnet tool install --global dotnet-ef --version 10.0.12`.

```powershell
dotnet ef database update --project src/Portal.Infrastructure --startup-project src/Portal.Web --context PortalDbContext
dotnet ef database update --project src/ErpSimulator.Api --startup-project src/ErpSimulator.Api --context ErpSimulatorDbContext
dotnet run --project src/ErpSimulator.Api --no-launch-profile -- --environment Development --seed-catalog
dotnet run --project src/Portal.Web --no-launch-profile -- --environment Development --seed-roles --seed-users
```

La semilla crea 10 clientes, 30 productos y sus existencias en el simulador. En el portal prepara los roles y las cuentas `admin@portal.test`, `vendedor@portal.test` y `operador@portal.test`. Ejecuta la semilla en una consola interactiva. Solicita una contraseña de al menos 12 caracteres por cada cuenta nueva, con mayúscula, minúscula, dígito y símbolo.

## Iniciar con F5

1. Abre [PortalPedidos.slnx](../PortalPedidos.slnx), selecciona **Portal completo** y pulsa **F5**. El [perfil de inicio](../PortalPedidos.slnLaunch) arranca ERP y portal. Utiliza los perfiles `https`.
2. Comprueba `https://localhost:7117/health/ready` para el ERP y `https://localhost:7271/health/ready` para el portal. Un HTTP 200 indica que el servicio puede acceder a su base.
3. Entra en `https://localhost:7271/Account/Login` como vendedor y abre **Catálogo**. El trabajador sincroniza los datos del simulador.

Para verificar la conexión y solicitar una sincronización explícita desde PowerShell, mantén el ERP en marcha. Estos comandos reutilizan los binarios de Debug compilados por Visual Studio:

```powershell
dotnet run --project src/Portal.Web --no-build --no-launch-profile -- --environment Development --check-erp
dotnet run --project src/Portal.Web --no-build --no-launch-profile -- --environment Development --sync-catalog
```

Para arrancar desde PowerShell en lugar del IDE, cierra primero los procesos que ocupen los mismos puertos y ejecuta `.\IniciarPortal.cmd`. El iniciador compila la solución, abre ambos servicios y espera a que respondan antes de abrir el portal.

## Comprobar una suplencia

1. El valor predeterminado permite vender a cualquier cliente activo. Para probar la revocación en una instalación de desarrollo con cartera abierta, ejecuta este comando y reinicia el portal.

   ```powershell
   dotnet user-secrets set "CustomerPortfolio:Restricted" "true" --project src/Portal.Web
   ```

2. Como administrador, prepara un supervisor y dos cuentas activas con rol **Vendedor** en **Usuarios**. En **Equipos**, crea un equipo con ese supervisor e incorpora a ambos vendedores. En **Cartera comercial** (`/CustomerPortfolio`), abre un cliente activo y asígnale un responsable. En **Suplencias del cliente**, escribe el correo del otro vendedor, indica un motivo de 5 a 250 caracteres y elige fechas que incluyan hoy en Lima. Pulsa **Registrar suplencia**.
3. En otro perfil de navegador, entra como suplente. En **Mi cartera** (`/MyPortfolio`), abre la ficha del cliente y pulsa **Nuevo pedido**. Guarda dos líneas y pulsa **Revisar y enviar**. Deja abierta la confirmación. El supervisor del equipo puede consultar el mismo cliente en **Cartera del equipo** (`/TeamPortfolio`).
4. En la sesión administrativa, escribe un motivo y pulsa **Revocar suplencia**. Desde la confirmación que sigue abierta, el suplente marca la casilla y pulsa **Confirmar envío**. El servidor devuelve HTTP 409 y muestra **Editar borrador**. Al abrirlo, siguen las dos líneas y sus cantidades. La prueba automática de cartera contrasta además que el borrador tiene cero mensajes Outbox.
5. Si la restricción se activó solo para la prueba, elimina esa clave con `dotnet user-secrets remove "CustomerPortfolio:Restricted" --project src/Portal.Web` y reinicia el portal. El valor predeterminado es `false`.

El [informe de pruebas](../docker/VALIDATION.md) distingue la compilación en Windows del recorrido funcional ejecutado con Chromium sobre Docker. El arranque interactivo con F5 contra las bases Windows queda pendiente de validación.
