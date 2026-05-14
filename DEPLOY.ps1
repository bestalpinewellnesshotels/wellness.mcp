#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Hotel Chatbot - Build-Script fuer Linux-Deployment.

.DESCRIPTION
    1. Baut das Frontend (Chatbot-Widget & Demo-Seite)
    2. Baut die .NET API fuer Linux (self-contained)
    3. Erstellt publish/linux-x64 Verzeichnis zum manuellen Kopieren

.NOTES
    Voraussetzung: Node.js muss installiert sein (fuer Frontend-Build).
    Nach dem Build manuell kopieren:
    - Inhalt von publish\linux-x64\ -> auf Server nach /opt/hotelchatbot/
    - Inhalt von chatgpt\ -> auf Server nach /opt/hotelchatbot/chatgpt/
#>

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Write-Header($text) {
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
}
function Write-Step($text)  { Write-Host "  > $text" -ForegroundColor Yellow }
function Write-OK($text)    { Write-Host "  + $text" -ForegroundColor Green }
function Write-Err($text)   { Write-Host "  X $text" -ForegroundColor Red }

$buildOutput = Join-Path $root "publish\linux-x64"
$chatgptDir = Join-Path $root "chatgpt"

# -----------------------------------------------------------------------------
# SCHRITT 1: Frontend bauen
# -----------------------------------------------------------------------------
Write-Header "Schritt 1/2 - Frontend bauen"

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Err "dotnet nicht gefunden. Bitte .NET SDK installieren."
    exit 1
}

if (-not (Get-Command "npm" -ErrorAction SilentlyContinue)) {
    Write-Err "npm nicht gefunden. Bitte Node.js installieren."
    exit 1
}

# Frontend bauen
Write-Step "Baue Frontend (Chatbot-Widget und Demo-Seite)..."
$frontendDir = Join-Path $root "frontend"
Push-Location $frontendDir

# Dependencies installieren
if (-not (Test-Path "node_modules")) {
    Write-Step "Installiere Frontend-Dependencies..."
    & npm install --silent
    if ($LASTEXITCODE -ne 0) {
        Write-Err "npm install fehlgeschlagen!"
        Pop-Location
        exit 1
    }
}

# Production Build
& npm run build
if ($LASTEXITCODE -ne 0) {
    Write-Err "Frontend-Build fehlgeschlagen!"
    Pop-Location
    exit 1
}
Pop-Location
Write-OK "Frontend gebaut -> frontend\dist\"

# Frontend nach wwwroot kopieren
Write-Step "Kopiere Frontend nach wwwroot..."
$wwwroot = Join-Path $root "src\HotelChatbot.Api\wwwroot"
$frontendDist = Join-Path $frontendDir "dist"
$demoHtml = Join-Path $frontendDir "demo.html"

# Erstelle demo Unterverzeichnis in wwwroot
$demoDir = Join-Path $wwwroot "demo"
if (-not (Test-Path $demoDir)) {
    New-Item -Path $demoDir -ItemType Directory -Force | Out-Null
}

# Kopiere chatbot.js
Copy-Item -Path (Join-Path $frontendDist "chatbot.js") -Destination $demoDir -Force
# Kopiere demo.html als index.html
Copy-Item -Path $demoHtml -Destination (Join-Path $demoDir "index.html") -Force

Write-OK "Frontend nach wwwroot\demo\ kopiert"

# -----------------------------------------------------------------------------
# SCHRITT 2: Backend bauen
# -----------------------------------------------------------------------------
Write-Header "Schritt 2/2 - Backend fuer Linux bauen"

Write-Step "Kompiliere HotelChatbot.Api fuer linux-x64..."
& dotnet publish "src\HotelChatbot.Api\HotelChatbot.Api.csproj" `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained true `
    --output $buildOutput `
    /p:PublishSingleFile=false `
    --nologo -v quiet

if ($LASTEXITCODE -ne 0) {
    Write-Err "Build fehlgeschlagen! Abbruch."
    exit 1
}
Write-OK "Backend-Build erfolgreich -> publish\linux-x64\"

# -----------------------------------------------------------------------------
# FERTIG - Bereit zum manuellen Kopieren
# -----------------------------------------------------------------------------
Write-Header "Build abgeschlossen!"
Write-Host ""
Write-OK "Folgende Verzeichnisse sind bereit zum Kopieren:"
Write-Host ""
Write-Host "  1. Backend (API, Admin, Demo):" -ForegroundColor Cyan
Write-Host "     $buildOutput" -ForegroundColor White
Write-Host "     -> Kopiere diesen Inhalt auf den Server nach /opt/hotelchatbot/" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. MCP-Server (ChatGPT Integration):" -ForegroundColor Cyan
Write-Host "     $chatgptDir" -ForegroundColor White
Write-Host "     -> Kopiere dieses Verzeichnis auf den Server nach /opt/hotelchatbot/chatgpt/" -ForegroundColor Gray
Write-Host ""
Write-Host "Enthalten im Backend:" -ForegroundColor Yellow
Write-Host "  - HotelChatbot.Api (ausfuehrbare Datei)" -ForegroundColor Gray
Write-Host "  - wwwroot/admin/ (Admin-Oberflaeche)" -ForegroundColor Gray
Write-Host "  - wwwroot/demo/ (Demo-Seite mit Chatbot)" -ForegroundColor Gray
Write-Host ""
Write-Host "Nach dem Kopieren auf dem Server:" -ForegroundColor Yellow
Write-Host "  chmod +x /opt/hotelchatbot/HotelChatbot.Api" -ForegroundColor Gray
Write-Host "  cd /opt/hotelchatbot" -ForegroundColor Gray
Write-Host "  ./HotelChatbot.Api" -ForegroundColor Gray

