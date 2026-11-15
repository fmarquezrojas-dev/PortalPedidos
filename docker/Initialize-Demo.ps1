[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    function Invoke-Compose {
        & docker compose --file compose.yaml @args
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose failed; initialization stopped.' }
    }

    # Resolve the same project/port as documented Compose commands (.env or environment).
    $config = & docker compose --file compose.yaml config --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Invalid Compose configuration.' }
    $projectName = $config.name
    # Refuse to adopt volumes belonging to another project or unmanaged volumes.
    foreach ($volume in @($config.volumes.'sql-data'.name, $config.volumes.'portal-keys'.name)) {
        $existing = & docker volume ls --format '{{.Name}}' --filter "name=^${volume}$"
        if ($LASTEXITCODE -ne 0) { throw 'Docker is unavailable.' }
        if ($existing -contains $volume) {
            $volumeInfo = (& docker volume inspect $volume | ConvertFrom-Json)[0]
            if ($LASTEXITCODE -ne 0 -or $volumeInfo.Labels.'com.docker.compose.project' -ne $projectName) {
                throw "Refusing to use an existing volume with an unexpected owner: $volume"
            }
        }
    }
    New-Item -ItemType Directory -Path .docker-local -Force | Out-Null
    Invoke-Compose config --quiet
    if (!$SkipBuild) { Invoke-Compose build erp portal }
    Invoke-Compose run --rm --no-deps setup
    Invoke-Compose up -d --wait sql
    # The application guard validates exact server/database names before either command.
    Invoke-Compose run --rm --no-deps erp --demo-migrate
    Invoke-Compose run --rm --no-deps portal --demo-migrate
    Invoke-Compose run --rm --no-deps erp --seed-catalog
    $seedFile = Join-Path $projectRoot '.docker-local/seed-password'
    Invoke-Compose run --rm --no-deps --volume "${seedFile}:/run/secrets/seed-password:ro" portal --seed-roles --seed-users
    Invoke-Compose up -d --wait erp
    Invoke-Compose run --rm --no-deps portal --check-erp
    Invoke-Compose run --rm --no-deps portal --sync-catalog
    Invoke-Compose up -d --wait portal
    $port = $config.services.portal.ports[0].published
    Write-Host "Demo ready: https://localhost:${port}/Account/Login (project: $projectName)"
    Write-Host 'Trust the public demo CA in your browser before opening it; see docker/README.md.'
} finally { Pop-Location }
