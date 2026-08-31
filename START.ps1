#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Hotel Chatbot - lokaler Einstiegspunkt (API + MCP-Sim + Frontend).

.DESCRIPTION
    Startet:
      - .NET API            http://localhost:5000
      - MCP Server          http://localhost:3001  (ChatGPT-Pfad + /sim UI)
      - Frontend Dev-Server http://localhost:8080  (Widget-Demo)

    Standard-Browserziel: ChatGPT-Simulation http://localhost:3001/sim

.PARAMETER Docker
    Startet via docker-compose statt lokal.

.PARAMETER Stop
    Stoppt lokale Dev-Prozesse (Ports 5000/3001/8080) bzw. Docker-Container (-Docker -Stop).

.PARAMETER NoBrowser
    Oeffnet den Browser nach dem Start nicht automatisch.

.PARAMETER NoFrontend
    Startet den Webpack-Frontend-Dev-Server nicht (nur API + MCP/Sim).
#>
param(
    [switch]$Docker,
    [switch]$Stop,
    [switch]$NoBrowser,
    [switch]$NoFrontend
)

$ErrorActionPreference = "Stop"

$ApiPort = 5000
$McpPort = 3001
$FrontendPort = 8080

# -----------------------------------------------------------------------------
# Farben / Hilfsfunktionen
# -----------------------------------------------------------------------------
function Write-Header($text) {
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
}

function Write-Step($text) { Write-Host "  > $text" -ForegroundColor Yellow }
function Write-OK($text)   { Write-Host "  OK $text" -ForegroundColor Green }
function Write-Info($text) { Write-Host "  * $text" -ForegroundColor Gray }
function Write-Warn($text) { Write-Host "  ! $text" -ForegroundColor DarkYellow }

function Stop-PortListeners([int[]]$Ports) {
    Write-Step "Beende Prozesse auf Ports $($Ports -join ', ')..."
    $stopped = @{}
    foreach ($port in $Ports) {
        try {
            $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
            foreach ($c in $conns) {
                $procId = $c.OwningProcess
                if (-not $procId -or $stopped.ContainsKey($procId)) { continue }
                $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
                if ($proc) {
                    Write-Info "Beende $($proc.ProcessName) (PID $procId) auf Port $port"
                    Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
                    $stopped[$procId] = $true
                }
            }
        }
        catch {
            # Get-NetTCPConnection nicht verfuegbar / keine Rechte
        }
    }
    if ($stopped.Count -gt 0) {
        Start-Sleep -Seconds 2
        Write-OK "$($stopped.Count) Prozess(e) beendet."
    }
    else {
        Write-Info "Keine Listener auf den Zielports gefunden."
    }
}

function Wait-HttpOk([string]$Url, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { return $true }
        }
        catch {
            Start-Sleep -Milliseconds 800
        }
    }
    return $false
}

# -----------------------------------------------------------------------------
# STOP
# -----------------------------------------------------------------------------
if ($Stop) {
    if ($Docker) {
        Write-Header "Docker-Container stoppen"
        docker-compose down
        Write-OK "Container gestoppt."
    }
    else {
        Write-Header "Lokale Dev-Prozesse stoppen"
        Stop-PortListeners @($ApiPort, $McpPort, $FrontendPort)
    }
    exit 0
}

# Vor dem Start alte Instanzen auf unseren Ports beenden
Stop-PortListeners @($ApiPort, $McpPort, $FrontendPort)

# -----------------------------------------------------------------------------
# DOCKER-MODUS
# -----------------------------------------------------------------------------
if ($Docker) {
    Write-Header "Hotel Chatbot - Docker-Start"

    Write-Step "Baue und starte Docker-Container..."
    docker-compose up --build -d

    Write-Host ""
    Write-OK "Container laufen. Warte auf Health-Check (~20 Sek.)..."
    Start-Sleep -Seconds 20

    $health = $null
    try { $health = Invoke-RestMethod "http://localhost:5001/health" -ErrorAction SilentlyContinue } catch {}
    if ($health) {
        Write-OK "API ist erreichbar (Status: $($health.status))"
    } else {
        Write-Warn "API noch nicht bereit - bitte manuell pruefen."
    }

    Write-Header "Verfuegbare URLs"
    Write-OK "API Health-Check : http://localhost:5001/health"
    Write-OK "Swagger (API-Docs): http://localhost:5001/swagger"
    Write-OK "Admin-Bereich    : http://localhost:5001/admin"
    Write-OK "MCP-Server       : http://localhost:3001"
    Write-OK "ChatGPT-Sim      : http://localhost:3001/sim"
    Write-Info ""
    Write-Info "Logs anzeigen    : docker-compose logs -f api"
    Write-Info "Container stoppen: .\START.ps1 -Docker -Stop"

    if (-not $NoBrowser) {
        Start-Process "http://localhost:3001/sim"
    }
    exit 0
}

# -----------------------------------------------------------------------------
# LOKALER ENTWICKLUNGS-MODUS (Standard)
# -----------------------------------------------------------------------------
Write-Header "Hotel Chatbot - Lokaler Start (Development)"

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "  FEHLER: dotnet nicht gefunden. Bitte .NET SDK installieren." -ForegroundColor Red
    Write-Host "  Download: https://dotnet.microsoft.com/download" -ForegroundColor Gray
    exit 1
}

if (-not (Get-Command "npm" -ErrorAction SilentlyContinue)) {
    Write-Host "  FEHLER: npm nicht gefunden. Bitte Node.js installieren." -ForegroundColor Red
    Write-Host "  Download: https://nodejs.org/" -ForegroundColor Gray
    exit 1
}

$apiProject = Join-Path $PSScriptRoot "src\HotelChatbot.Api\HotelChatbot.Api.csproj"
if (-not (Test-Path $apiProject)) {
    Write-Host "  FEHLER: Projektdatei nicht gefunden: $apiProject" -ForegroundColor Red
    exit 1
}

$mcpDir = Join-Path $PSScriptRoot "chatgpt"
if (-not (Test-Path (Join-Path $mcpDir "index.js"))) {
    Write-Host "  FEHLER: MCP-Server nicht gefunden: $mcpDir\index.js" -ForegroundColor Red
    exit 1
}

$frontendDir = Join-Path $PSScriptRoot "frontend"
if (-not $NoFrontend -and -not (Test-Path $frontendDir)) {
    Write-Host "  FEHLER: Frontend-Verzeichnis nicht gefunden: $frontendDir" -ForegroundColor Red
    exit 1
}

$npmCmd = "npm.cmd"
if ($IsLinux -or $IsMacOS) { $npmCmd = "npm" }
$nodeCmd = "node"
if ($IsWindows -ne $false -and (Get-Command "node.exe" -ErrorAction SilentlyContinue)) {
    # node is fine on Windows as "node"
}

# Dependencies
$mcpModules = Join-Path $mcpDir "node_modules"
if (-not (Test-Path $mcpModules)) {
    Write-Step "Installiere MCP-Dependencies (chatgpt/)..."
    Push-Location $mcpDir
    try {
        & $npmCmd install
        if ($LASTEXITCODE -ne 0) { throw "npm install (MCP) fehlgeschlagen." }
    }
    finally { Pop-Location }
    Write-OK "MCP-Dependencies installiert."
}

if (-not $NoFrontend) {
    $frontendModules = Join-Path $frontendDir "node_modules"
    if (-not (Test-Path $frontendModules)) {
        Write-Step "Installiere Frontend-Dependencies..."
        Push-Location $frontendDir
        try {
            & $npmCmd install
            if ($LASTEXITCODE -ne 0) { throw "npm install (Frontend) fehlgeschlagen." }
        }
        finally { Pop-Location }
        Write-OK "Frontend-Dependencies installiert."
    }
}

Write-Header "Verfuegbare URLs"
Write-OK "ChatGPT-Sim (Haupt) : http://localhost:$McpPort/sim"
Write-OK "MCP Health          : http://localhost:$McpPort/health"
Write-OK "MCP /mcp            : http://localhost:$McpPort/mcp"
Write-OK "API Health          : http://localhost:$ApiPort/health"
Write-OK "Swagger             : http://localhost:$ApiPort/swagger"
Write-OK "Admin               : http://localhost:$ApiPort/admin"
if (-not $NoFrontend) {
    Write-OK "Frontend Widget-Demo: http://localhost:$FrontendPort/demo.html"
}
Write-Info ""
Write-Info "Antwort in /sim = exakt MCP-Tool-Feld 'answer' (wie ChatGPT)"
Write-Info "Sidebar = Live Pipeline (Classifier / LLM / Search + Dauer)"
Write-Info "Datenbank: PostgreSQL (appsettings.Development.json)"
Write-Info "Zum Beenden: Ctrl+C   |   Stoppen: .\START.ps1 -Stop"
Write-Host ""

# --- API ---
Write-Step "Starte .NET API auf http://localhost:$ApiPort ..."
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:$ApiPort"

$apiProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --no-launch-profile --project `"$apiProject`"" `
    -WorkingDirectory (Join-Path $PSScriptRoot "src\HotelChatbot.Api") `
    -PassThru `
    -NoNewWindow

Write-Step "Warte auf API-Health..."
if (Wait-HttpOk "http://localhost:$ApiPort/health" 90) {
    Write-OK "API bereit."
}
else {
    Write-Warn "API antwortet noch nicht auf /health - MCP startet trotzdem."
}

# --- MCP ---
Write-Step "Starte MCP-Server auf http://localhost:$McpPort ..."
$mcpEnv = @{
    PORT            = "$McpPort"
    API_BASE_URL    = "http://localhost:$ApiPort"
    API_TIMEOUT_MS  = "120000"
    ENABLE_DEBUG    = "true"
}
foreach ($k in $mcpEnv.Keys) { Set-Item -Path "Env:$k" -Value $mcpEnv[$k] }

$mcpProcess = Start-Process -FilePath $nodeCmd `
    -ArgumentList "index.js" `
    -WorkingDirectory $mcpDir `
    -PassThru `
    -NoNewWindow

Write-Step "Warte auf MCP-Health..."
if (Wait-HttpOk "http://localhost:$McpPort/health" 45) {
    Write-OK "MCP bereit (inkl. /sim)."
}
else {
    Write-Warn "MCP antwortet noch nicht auf /health."
}

# --- Frontend (optional) ---
$frontendProcess = $null
if (-not $NoFrontend) {
    Write-Step "Starte Frontend Dev-Server auf http://localhost:$FrontendPort ..."
    $frontendProcess = Start-Process -FilePath $npmCmd `
        -ArgumentList "run serve" `
        -WorkingDirectory $frontendDir `
        -PassThru `
        -NoNewWindow
}

if (-not $NoBrowser) {
    Start-Job {
        param($simUrl, $adminUrl, $demoUrl, $withFrontend)
        Start-Sleep -Seconds 3
        Start-Process $simUrl
        Start-Sleep -Seconds 1
        if ($withFrontend) { Start-Process $demoUrl }
        Start-Process $adminUrl
    } -ArgumentList @(
        "http://localhost:$McpPort/sim",
        "http://localhost:$ApiPort/admin",
        "http://localhost:$FrontendPort/demo.html",
        (-not $NoFrontend)
    ) | Out-Null
}

Write-Host ""
Write-OK "Dienste gestartet."
Write-Info "Primaer testen: http://localhost:$McpPort/sim"
Write-Host ""

try {
    Write-Host "  Druecken Sie Ctrl+C zum Beenden..." -ForegroundColor Yellow
    Write-Host ""

    while ($true) {
        Start-Sleep -Seconds 1

        if ($apiProcess.HasExited) {
            Write-Host "  ! API-Prozess wurde beendet (Exit Code: $($apiProcess.ExitCode))" -ForegroundColor Red
            break
        }
        if ($mcpProcess.HasExited) {
            Write-Host "  ! MCP-Prozess wurde beendet (Exit Code: $($mcpProcess.ExitCode))" -ForegroundColor Red
            break
        }
        if ($frontendProcess -and $frontendProcess.HasExited) {
            Write-Host "  ! Frontend-Prozess wurde beendet (Exit Code: $($frontendProcess.ExitCode))" -ForegroundColor Red
            break
        }
    }
}
finally {
    Write-Host ""
    Write-Step "Beende Prozesse..."

    foreach ($p in @($apiProcess, $mcpProcess, $frontendProcess)) {
        if ($null -ne $p -and -not $p.HasExited) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }
    }
    # Child-Prozesse auf den Ports mitnehmen
    Stop-PortListeners @($ApiPort, $McpPort, $FrontendPort) | Out-Null

    Start-Sleep -Seconds 1
    Write-OK "Alle Prozesse beendet."
}
