#!/usr/bin/env powershell
<#
.SYNOPSIS
    Migriert Hoteldaten von dev-universe.net nach bestalpinedb1 auf dem Produktivserver.
#>

[CmdletBinding()]
param(
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $root "deploy\production.settings.json"
$devSettingsPath = Join-Path $root "src\HotelChatbot.Api\appsettings.json"

function Write-Step($text)  { Write-Host "  > $text" -ForegroundColor Yellow }
function Write-OK($text)    { Write-Host "  + $text" -ForegroundColor Green }
function Write-Err($text)   { Write-Host "  X $text" -ForegroundColor Red }

function Get-SshIdentityArgs {
    $keyPath = Join-Path $env:USERPROFILE ".ssh\id_ed25519_bestalpine"
    if (Test-Path $keyPath) { return @("-i", $keyPath, "-o", "IdentitiesOnly=yes") }
    return @()
}

function Invoke-SshCommand {
    param([string]$Target, [string]$Command)
    $sshArgs = @(Get-SshIdentityArgs) + @("-o", "StrictHostKeyChecking=accept-new", $Target, $Command)
    & ssh @sshArgs
    if ($LASTEXITCODE -ne 0) { throw "SSH fehlgeschlagen" }
}

function Invoke-SshCapture {
    param([string]$Target, [string]$Command)
    $sshArgs = @(Get-SshIdentityArgs) + @("-o", "StrictHostKeyChecking=accept-new", $Target, $Command)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & ssh @sshArgs 2>&1 | ForEach-Object { "$_" }
        return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

if (-not (Test-Path $configPath)) { throw "Fehlt: deploy\production.settings.json" }
if (-not (Test-Path $devSettingsPath)) { throw "Fehlt: appsettings.json" }

$prod = Get-Content $configPath -Raw | ConvertFrom-Json
$dev = Get-Content $devSettingsPath -Raw | ConvertFrom-Json
$devConn = $dev.ConnectionStrings.PostgreSQL
if ($devConn -match "Host=([^;]+).*Database=([^;]+).*Username=([^;]+).*Password=([^;]+)") {
    $devHost = $Matches[1]; $devDb = $Matches[2]; $devUser = $Matches[3]; $devPass = $Matches[4]
} else {
    throw "Dev-ConnectionString konnte nicht gelesen werden"
}

$sshTarget = "$($prod.SshUser)@$($prod.SshHost)"

Write-Host ""
Write-Host "Datenbank-Migration: $devHost/$devDb -> localhost/$($prod.PostgresDatabase)" -ForegroundColor Cyan
Write-Host ""

if (-not $NonInteractive) {
    $confirm = Read-Host "Migration starten? [J/n]"
    if ($confirm -eq "n" -or $confirm -eq "N") { exit 0 }
}

Write-Step "Dump von dev-universe.net erstellen und in Produktiv-DB importieren..."
$remoteScript = @"
set -e
export PGPASSWORD='$($devPass -replace "'", "'\\''")'
DUMP="/tmp/bwchat-migrate.dump"
pg_dump -h '$devHost' -p 5432 -U '$devUser' -d '$devDb' --no-owner --no-acl -Fc -f "`$DUMP"
export PGPASSWORD='$($prod.PostgresPassword -replace "'", "'\\''")'
psql -h '$($prod.PostgresHost)' -U '$($prod.PostgresUser)' -d '$($prod.PostgresDatabase)' -c 'CREATE EXTENSION IF NOT EXISTS vector;'
pg_restore -h '$($prod.PostgresHost)' -U '$($prod.PostgresUser)' -d '$($prod.PostgresDatabase)' --clean --if-exists --no-owner --no-acl "`$DUMP" 2>/dev/null || true
HOTELS=`$(psql -h '$($prod.PostgresHost)' -U '$($prod.PostgresUser)' -d '$($prod.PostgresDatabase)' -tAc 'SELECT COUNT(*) FROM hotels;' 2>/dev/null || echo 0)
CHUNKS=`$(psql -h '$($prod.PostgresHost)' -U '$($prod.PostgresUser)' -d '$($prod.PostgresDatabase)' -tAc 'SELECT COUNT(*) FROM content_chunks;' 2>/dev/null || echo 0)
rm -f "`$DUMP"
if [ "`$HOTELS" -eq 0 ]; then echo 'MIGRATION_FAILED no hotels'; exit 1; fi
echo "MIGRATION_OK hotels=`$HOTELS chunks=`$CHUNKS"
"@ -replace "`r", ""

$result = Invoke-SshCapture $sshTarget $remoteScript
Write-Host $result.Output
if ($result.ExitCode -ne 0) { throw "Datenbank-Migration fehlgeschlagen" }
Write-OK "Migration abgeschlossen."
