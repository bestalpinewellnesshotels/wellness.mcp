#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Hotel Chatbot - Einstiegspunkt fuer lokale Entwicklung und Administration.

.DESCRIPTION
    Startet die .NET-API lokal und zeigt alle wichtigen URLs.
    Fuer Docker-Deployment: Option -Docker verwenden.

.PARAMETER Docker
    Startet die Anwendung via docker-compose statt lokal.

.PARAMETER Stop
    Stoppt laufende Docker-Container.

.PARAMETER NoBrowser
    Oeffnet den Browser nach dem Start nicht automatisch.
#>
param(
    [switch]$Docker,
    [switch]$Stop,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

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

# -----------------------------------------------------------------------------
# Beende alle laufenden Prozesse (dotnet, node)
# -----------------------------------------------------------------------------
function Stop-RunningProcesses {
    Write-Step "Beende laufende dotnet- und node-Prozesse..."
    
    $processes = Get-Process | Where-Object { 
        $_.ProcessName -match "dotnet|node" -and 
        $_.ProcessName -ne "escape-node-job" 
    }
    
    if ($processes) {
        foreach ($proc in $processes) {
            try {
                Write-Info "Beende Prozess: $($proc.ProcessName) (PID: $($proc.Id))"
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            }
            catch {
                Write-Host "  ! Konnte Prozess $($proc.Id) nicht beenden" -ForegroundColor Red
            }
        }
        Start-Sleep -Seconds 2
        Write-OK "Alle Prozesse beendet."
    }
    else {
        Write-Info "Keine laufenden dotnet- oder node-Prozesse gefunden."
    }
}

# Beende zuerst alle laufenden Prozesse (außer bei -Stop Parameter)
if (-not $Stop) {
    Stop-RunningProcesses
}

# -----------------------------------------------------------------------------
# STOP
# -----------------------------------------------------------------------------
if ($Stop) {
    Write-Header "Docker-Container stoppen"
    docker-compose down
    Write-OK "Container gestoppt."
    exit 0
}

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

    $health = Invoke-RestMethod "http://localhost:5001/health" -ErrorAction SilentlyContinue
    if ($health) {
        Write-OK "API ist erreichbar (Status: $($health.status))"
    } else {
        Write-Host "  ! API noch nicht bereit - bitte manuell pruefen." -ForegroundColor Red
    }

    Write-Header "Verfuegbare URLs"
    Write-OK "API Health-Check : http://localhost:5001/health"
    Write-OK "Swagger (API-Docs): http://localhost:5001/swagger"
    Write-OK "Admin-Bereich    : http://localhost:5001/admin"
    Write-OK "MCP-Server       : http://localhost:3001"
    Write-Info ""
    Write-Info "Logs anzeigen    : docker-compose logs -f api"
    Write-Info "Container stoppen: .\START.ps1 -Stop"

    if (-not $NoBrowser) {
        Start-Process "http://localhost:5001/admin"
    }
    exit 0
}

# -----------------------------------------------------------------------------
# LOKALER ENTWICKLUNGS-MODUS (Standard)
# -----------------------------------------------------------------------------
Write-Header "Hotel Chatbot - Lokaler Start (Development)"

# .NET pruefen
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "  FEHLER: dotnet nicht gefunden. Bitte .NET SDK installieren." -ForegroundColor Red
    Write-Host "  Download: https://dotnet.microsoft.com/download" -ForegroundColor Gray
    exit 1
}

# Node.js pruefen
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

$frontendDir = Join-Path $PSScriptRoot "frontend"
if (-not (Test-Path $frontendDir)) {
    Write-Host "  FEHLER: Frontend-Verzeichnis nicht gefunden: $frontendDir" -ForegroundColor Red
    exit 1
}

# Frontend Dependencies installieren falls noetig
$nodeModules = Join-Path $frontendDir "node_modules"
if (-not (Test-Path $nodeModules)) {
    Write-Step "Installiere Frontend-Dependencies..."
    Set-Location $frontendDir
    npm install
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FEHLER: npm install fehlgeschlagen." -ForegroundColor Red
        exit 1
    }
    Set-Location $PSScriptRoot
    Write-OK "Dependencies installiert."
}

Write-Header "Verfuegbare URLs nach dem Start"
Write-OK "Demo-Seite       : http://localhost:8080"
Write-OK "Admin-Bereich    : http://localhost:5000/admin"
Write-OK "Swagger (API-Docs): http://localhost:5000/swagger"
Write-OK "API Health-Check : http://localhost:5000/health"
Write-Info ""
Write-Info "Datenbank        : PostgreSQL auf dev-universe.net (extern und bereits konfiguriert)"
Write-Info "KI-Modell        : Azure OpenAI gpt-4.1-mini"
Write-Info ""
Write-Info "Zum Beenden: Ctrl+C druecken"
Write-Host ""

# API im Hintergrund starten
Write-Step "Starte .NET API auf http://localhost:5000..."
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5000"

$apiProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --no-launch-profile --project `"$apiProject`"" `
    -WorkingDirectory (Join-Path $PSScriptRoot "src\HotelChatbot.Api") `
    -PassThru `
    -NoNewWindow

# Webpack Dev Server im Hintergrund starten
Write-Step "Starte Frontend Dev-Server auf http://localhost:8080..."
$npmCmd = "npm.cmd"
if ($IsLinux -or $IsMacOS) { $npmCmd = "npm" }
$frontendProcess = Start-Process -FilePath $npmCmd `
    -ArgumentList "run serve" `
    -WorkingDirectory $frontendDir `
    -PassThru `
    -NoNewWindow

# Browser oeffnen (nach kurzem Delay)
if (-not $NoBrowser) {
    Start-Job {
        Start-Sleep -Seconds 7
        Start-Process "http://localhost:8080"
        Start-Sleep -Seconds 1
        Start-Process "http://localhost:5000/admin"
    } | Out-Null
}

Write-Host ""
Write-OK "API und Frontend werden gestartet..."
Write-Info "Bitte warten Sie ca. 5-10 Sekunden, bis beide Dienste bereit sind."
Write-Host ""

# Warte auf Ctrl+C
try {
    Write-Host "  Druecken Sie Ctrl+C zum Beenden..." -ForegroundColor Yellow
    Write-Host ""
    
    # Endlos-Schleife bis Ctrl+C
    while ($true) {
        Start-Sleep -Seconds 1
        
        # Pruefen ob Prozesse noch laufen
        if ($apiProcess.HasExited) {
            Write-Host "  ! API-Prozess wurde beendet (Exit Code: $($apiProcess.ExitCode))" -ForegroundColor Red
            break
        }
        if ($frontendProcess.HasExited) {
            Write-Host "  ! Frontend-Prozess wurde beendet (Exit Code: $($frontendProcess.ExitCode))" -ForegroundColor Red
            break
        }
    }
}
finally {
    # Cleanup bei Ctrl+C oder Fehler
    Write-Host ""
    Write-Step "Beende Prozesse..."
    
    if (-not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if (-not $frontendProcess.HasExited) {
        Stop-Process -Id $frontendProcess.Id -Force -ErrorAction SilentlyContinue
    }
    
    # Warte kurz damit Prozesse sauber beendet werden
    Start-Sleep -Seconds 2
    
    Write-OK "Alle Prozesse beendet."
}
