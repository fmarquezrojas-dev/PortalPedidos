[CmdletBinding()]
param(
    [string]$Python = 'python',
    [ValidateRange(0, 65535)][int]$Port = 0,
    [switch]$MeasureBaseline,
    [switch]$Operational,
    [switch]$Portfolio
)
$ErrorActionPreference = 'Stop'
$source = Split-Path $PSScriptRoot -Parent
$Python = (Get-Command $Python -CommandType Application).Source
$id = [guid]::NewGuid().ToString('N').Substring(0, 12)
$project = "portalpedidos-capture-$id"
$run = Join-Path ([IO.Path]::GetTempPath()) $project
$reports = Join-Path $source "artifacts/$project"
$oldProject = $env:COMPOSE_PROJECT_NAME
$oldPort = $env:PORTAL_HTTPS_PORT

function Run {
    $command, $arguments = $args
    & $command @arguments
    if ($LASTEXITCODE -ne 0) { throw "$command exit code $LASTEXITCODE." }
}

function Copy-Worktree {
    $files = Run git -c "safe.directory=$source" -C $source ls-files --cached --others --exclude-standard -- src docker compose.yaml .dockerignore
    foreach ($file in $files) {
        $from = Join-Path $source $file
        if (!(Test-Path -LiteralPath $from -PathType Leaf)) { continue }
        $to = Join-Path $run $file
        New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
    }
}

if ($Port -eq 0) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { $Port = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
New-Item -ItemType Directory -Path $run | Out-Null
Push-Location $run
try {
    $env:COMPOSE_PROJECT_NAME = $project
    $env:PORTAL_HTTPS_PORT = "$Port"
    @("COMPOSE_PROJECT_NAME=$project", "PORTAL_HTTPS_PORT=$Port") | Set-Content .env -Encoding ascii
    Write-Host "Copia privada de prueba $run (HTTPS $Port)"
    if ($MeasureBaseline) {
        Run git -c "safe.directory=$source" -C $source archive -o "$run/baseline.zip" c53707c
        Expand-Archive -LiteralPath baseline.zip -DestinationPath $run
        & ./docker/Initialize-Demo.ps1
        Run $Python -B "$source/docker/capture-test.py" seed --demo $run
        Run $Python -B "$source/docker/capture-test.py" before --demo $run
        Run $Python -B docker/smoke-test.py --port $Port
        Run $Python -B "$source/docker/capture-test.py" seed-portfolio --demo $run
        Run $Python -B "$source/docker/capture-test.py" upgrade-before --demo $run
    }
    Copy-Worktree
    & ./docker/Initialize-Demo.ps1
    if ($MeasureBaseline) { Run $Python -B docker/capture-test.py upgrade-after }
    if (!$MeasureBaseline) { Run $Python -B docker/capture-test.py seed }
    Run $Python -B docker/capture-test.py after
    Run $Python -B docker/smoke-test.py --port $Port
    # La aceptación del smoke incrementa la versión de existencias del ERP.
    Run $Python -B docker/capture-test.py sync
    Run $Python -B docker/functional-test.py --project-name $project --port $Port
    Run $Python -B docker/capture-test.py browser
    Run $Python -B docker/capture-test.py lost-response
    Run $Python -B docker/smoke-test.py --port $Port --verify-existing
    Run $Python -B docker/capture-test.py verify
    if ($Operational -or $Portfolio) { Run $Python -B docker/capture-test.py operational }
    if ($Portfolio) {
        Run docker compose up -d --wait portal
        if (!$MeasureBaseline) { Run $Python -B docker/capture-test.py seed-portfolio }
        Run dotnet publish docker/PortfolioClockCheck -c Release -o "$run/clock-check"
        Run $Python -B docker/capture-test.py portfolio
    }
} finally {
    if (Test-Path .docker-local) {
        New-Item -ItemType Directory -Path $reports -Force | Out-Null
        Get-ChildItem .docker-local -Include 'capture-*.json','functional-report-*.json' -File -Recurse |
            Copy-Item -Destination $reports
        Write-Host "Informes de prueba en $reports"
    }
    try {
        if (Test-Path compose.yaml) { Run docker compose stop portal erp sql }
    } finally {
        Pop-Location
        $env:COMPOSE_PROJECT_NAME = $oldProject
        $env:PORTAL_HTTPS_PORT = $oldPort
    }
}
