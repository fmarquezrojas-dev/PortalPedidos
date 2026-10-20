@echo off
setlocal
cd /d "%~dp0"
if /i "%~1"=="api" goto api
if /i "%~1"=="web" goto web
title PortalPedidos - Inicio conjunto
echo.
echo  PORTALPEDIDOS - Inicio de la demostracion local
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
 echo No se encontro dotnet. Instala el SDK .NET 10 y vuelve a abrir este archivo.
 goto failed
)
if not exist "src\ErpSimulator.Api\ErpSimulator.Api.csproj" (
 echo Falta el proyecto del simulador junto a este archivo.
 goto failed
)
if not exist "src\Portal.Web\Portal.Web.csproj" (
 echo Falta el proyecto web junto a este archivo.
 goto failed
)
rem Si ambos procesos conocidos ya responden, abrir el portal sin duplicarlos.
powershell.exe -NoLogo -NoProfile -Command "try { $a=Invoke-RestMethod 'https://localhost:7117/health' -TimeoutSec 2; $w=Invoke-RestMethod 'https://localhost:7271/health' -TimeoutSec 2; if ($a.service -eq 'ErpSimulator.Api' -and $w.service -eq 'Portal.Web') { exit 0 } } catch {}; exit 1"
if not errorlevel 1 goto open
rem Evitar competir con Visual Studio u otros procesos en los mismos puertos.
powershell.exe -NoLogo -NoProfile -Command "$listeners=[System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners(); foreach ($listener in $listeners) { if ($listener.Port -in 7117,7271,5129,5145) { exit 1 } }; exit 0"
if errorlevel 1 (
 echo Uno de los puertos del portal ya esta ocupado.
 echo Deten la ejecucion anterior desde Visual Studio o su consola y vuelve a intentarlo.
 goto failed
)
echo [1/3] Restaurando y compilando la solucion...
dotnet build "PortalPedidos.slnx" --nologo
if errorlevel 1 goto failed
echo [2/3] Iniciando ERP y portal...
start "PortalPedidos - ERP" cmd.exe /d /k ""%~f0" api"
start "PortalPedidos - Web" cmd.exe /d /k ""%~f0" web"
echo [3/3] Esperando respuesta de ambos servicios...
powershell.exe -NoLogo -NoProfile -Command "$until=(Get-Date).AddSeconds(120); do { try { $a=Invoke-RestMethod 'https://localhost:7117/health' -TimeoutSec 2; $w=Invoke-RestMethod 'https://localhost:7271/health' -TimeoutSec 2; if ($a.service -eq 'ErpSimulator.Api' -and $w.service -eq 'Portal.Web') { exit 0 } } catch {}; Start-Sleep -Seconds 1 } while ((Get-Date) -lt $until); exit 1"
if errorlevel 1 (
 echo Los servicios no respondieron a tiempo. Revisa los mensajes en sus dos ventanas.
 echo Verifica las conexiones SQL, los secretos existentes y el certificado HTTPS de desarrollo.
 goto failed
)
:open
start "" "https://localhost:7271/Account/Login"
echo El portal esta disponible. Conserva abiertas las ventanas del ERP y Web.
echo Para detenerlos, pulsa Ctrl+C en cada ventana o cierra sus consolas.
exit /b 0
:api
title PortalPedidos - ERP
dotnet run --project "src\ErpSimulator.Api\ErpSimulator.Api.csproj" --no-build --launch-profile https
exit /b %errorlevel%
:web
title PortalPedidos - Web
dotnet run --project "src\Portal.Web\Portal.Web.csproj" --no-build --launch-profile https
exit /b %errorlevel%
:failed
echo.
echo No se completo el inicio. El mensaje anterior indica el paso que requiere revision.
pause
exit /b 1
