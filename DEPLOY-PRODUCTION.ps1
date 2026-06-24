#!/usr/bin/env powershell
<#
.SYNOPSIS
    Ein Script fuer Build + Deploy auf den Best-Alpine-Produktivserver.

.DESCRIPTION
    Beim ersten Start werden fehlende Zugangsdaten abgefragt und lokal gespeichert.
    Danach genuegt meist:  .\DEPLOY-PRODUCTION.ps1

.PARAMETER Reconfigure
    Konfiguration erneut abfragen.

.PARAMETER BuildOnly
    Nur lokal bauen, nichts hochladen.

.PARAMETER SkipBuild
    Vorhandenen Build aus publish/linux-x64 deployen.

.PARAMETER NonInteractive
    Kein Menue, direkt deployen (fuer CI/CD).

.PARAMETER MigrateDatabase
    Nach dem Deploy Datenbank von dev-universe.net importieren.

.EXAMPLE
    .\DEPLOY-PRODUCTION.ps1

.EXAMPLE
    .\DEPLOY-PRODUCTION.ps1 -NonInteractive -MigrateDatabase
#>

[CmdletBinding()]
param(
    [switch]$Reconfigure,
    [switch]$BuildOnly,
    [switch]$SkipBuild,
    [switch]$NonInteractive,
    [switch]$MigrateDatabase
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$configPath = Join-Path $root "deploy\production.settings.json"
$buildOutput = Join-Path $root "publish\linux-x64"
$stagingDir = Join-Path $root "publish\deploy-staging"
$mcpStaging = Join-Path $stagingDir "mcp"
$templatePath = Join-Path $root "deploy\appsettings.Production.template.json"

function Write-Header($text) {
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
}
function Write-Step($text)  { Write-Host "  > $text" -ForegroundColor Yellow }
function Write-OK($text)    { Write-Host "  + $text" -ForegroundColor Green }
function Write-Warn($text)  { Write-Host "  ! $text" -ForegroundColor DarkYellow }
function Write-Err($text)   { Write-Host "  X $text" -ForegroundColor Red }

function Test-CliCommand($name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

function Get-DefaultConfig {
    $devSettingsPath = Join-Path $root "src\HotelChatbot.Api\appsettings.json"
    $dev = $null
    if (Test-Path $devSettingsPath) {
        $dev = Get-Content $devSettingsPath -Raw | ConvertFrom-Json
    }

    return [ordered]@{
        SshHost                   = "mcp.bestalpine2.ms.mynet.at"
        SshUser                   = "mcp"
        PublicUrl                 = "https://mcp.bestalpine2.ms.mynet.at"
        RemoteAppDir              = "/data/web/mcp/home/app"
        RemoteMcpDir              = "/data/web/mcp/home/mcp"
        PostgresHost              = "localhost"
        PostgresPort              = 5432
        PostgresDatabase          = "bestalpinedb1"
        PostgresUser              = "bestalpine"
        PostgresPassword          = ""
        OpenAiEndpoint            = if ($dev.OpenAI.Endpoint) { [string]$dev.OpenAI.Endpoint } else { "" }
        OpenAiApiKey              = if ($dev.OpenAI.ApiKey) { [string]$dev.OpenAI.ApiKey } else { "" }
        OpenAiDeploymentName      = if ($dev.OpenAI.DeploymentName) { [string]$dev.OpenAI.DeploymentName } else { "gpt-4.1-mini" }
        OpenAiEmbeddingDeployment = if ($dev.OpenAI.EmbeddingDeploymentName) { [string]$dev.OpenAI.EmbeddingDeploymentName } else { "text-embedding-3-small" }
        OpenAiEmbeddingDimensions = if ($dev.OpenAI.EmbeddingDimensions) { [string]$dev.OpenAI.EmbeddingDimensions } else { "1536" }
        SpeechSubscriptionKey     = if ($dev.SpeechService.SubscriptionKey) { [string]$dev.SpeechService.SubscriptionKey } else { "" }
        SpeechServiceRegion       = if ($dev.SpeechService.ServiceRegion) { [string]$dev.SpeechService.ServiceRegion } else { "germanywestcentral" }
        ElevenLabsApiKey          = if ($dev.ElevenLabs.ApiKey) { [string]$dev.ElevenLabs.ApiKey } else { "" }
        ElevenLabsVoiceId         = if ($dev.ElevenLabs.VoiceId) { [string]$dev.ElevenLabs.VoiceId } else { "" }
        AdminPassword             = ""
        HotelPassword             = ""
        TokenSecret               = ""
        ApiPort                   = 8080
        McpPort                   = 3001
    }
}

function Load-DeployConfig {
    if (-not (Test-Path $configPath)) { return $null }
    $json = Get-Content $configPath -Raw | ConvertFrom-Json
    $cfg = Get-DefaultConfig
    foreach ($prop in $json.PSObject.Properties) {
        $cfg[$prop.Name] = $prop.Value
    }
    return $cfg
}

function Save-DeployConfig {
    param([hashtable]$Config)

    $dir = Split-Path $configPath -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $Config | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding UTF8
    Write-OK "Konfiguration gespeichert: deploy\production.settings.json"
}

function Read-PlainText {
    param(
        [string]$Prompt,
        [string]$Default = "",
        [switch]$Secret
    )

    if ($Default) {
        $label = if ($Secret) { "$Prompt [gespeichert, Enter=behalten]" } else { "$Prompt [$Default]" }
    } else {
        $label = $Prompt
    }

    if ($Secret) {
        $secure = Read-Host $label -AsSecureString
        $value = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        )
        if ([string]::IsNullOrWhiteSpace($value) -and $Default) { return $Default }
        return $value
    }

    $value = Read-Host $label
    if ([string]::IsNullOrWhiteSpace($value)) { return $Default }
    return $value
}

function New-RandomSecret {
    param([int]$Length = 24)
    $bytes = New-Object byte[] $Length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','').Replace('/','').Substring(0, [Math]::Min($Length, 32))
}

function Test-ConfigComplete {
    param([hashtable]$Config)

    $missing = @()
    foreach ($key in @("PostgresPassword", "OpenAiEndpoint", "OpenAiApiKey", "AdminPassword", "HotelPassword", "TokenSecret")) {
        if ([string]::IsNullOrWhiteSpace([string]$Config[$key])) {
            $missing += $key
        }
    }
    return $missing
}

function Initialize-DeployConfig {
    param(
        [hashtable]$Existing,
        [switch]$Force
    )

    $cfg = if ($Existing) { $Existing.Clone() } else { Get-DefaultConfig }
    $missing = Test-ConfigComplete $cfg

    if (-not $Force -and $missing.Count -eq 0) {
        return $cfg
    }

    Write-Header "Konfiguration"
    if ($Force) {
        Write-Host "  Alle Werte koennen neu eingegeben werden (Enter = aktueller Wert)." -ForegroundColor Gray
    } else {
        Write-Host "  Beim ersten Deploy fehlen noch einige Werte." -ForegroundColor Gray
    }
    Write-Host ""

    $cfg.SshHost = Read-PlainText "SSH Host" $cfg.SshHost
    $cfg.SshUser = Read-PlainText "SSH User" $cfg.SshUser
    $cfg.PublicUrl = Read-PlainText "Oeffentliche MCP-URL" $cfg.PublicUrl
    $cfg.RemoteAppDir = Read-PlainText "Remote API-Verzeichnis" $cfg.RemoteAppDir
    $cfg.RemoteMcpDir = Read-PlainText "Remote MCP-Verzeichnis" $cfg.RemoteMcpDir

    Write-Host ""
    Write-Host "  PostgreSQL (auf dem Server):" -ForegroundColor Gray
    $cfg.PostgresHost = Read-PlainText "  Host" $cfg.PostgresHost
    $cfg.PostgresPort = [int](Read-PlainText "  Port" ([string]$cfg.PostgresPort))
    $cfg.PostgresDatabase = Read-PlainText "  Datenbank" $cfg.PostgresDatabase
    $cfg.PostgresUser = Read-PlainText "  User" $cfg.PostgresUser
    $cfg.PostgresPassword = Read-PlainText "  Passwort" $cfg.PostgresPassword -Secret

    Write-Host ""
    Write-Host "  OpenAI / Azure OpenAI:" -ForegroundColor Gray
    if (-not $Force -and [string]::IsNullOrWhiteSpace($cfg.OpenAiApiKey)) {
        $useDev = Read-Host "  Dev-Werte aus appsettings.json uebernehmen? [J/n]"
        if ($useDev -ne "n" -and $useDev -ne "N") {
            $defaults = Get-DefaultConfig
            $cfg.OpenAiEndpoint = $defaults.OpenAiEndpoint
            $cfg.OpenAiApiKey = $defaults.OpenAiApiKey
            $cfg.OpenAiDeploymentName = $defaults.OpenAiDeploymentName
            $cfg.OpenAiEmbeddingDeployment = $defaults.OpenAiEmbeddingDeployment
            $cfg.OpenAiEmbeddingDimensions = $defaults.OpenAiEmbeddingDimensions
        }
    }
    $cfg.OpenAiEndpoint = Read-PlainText "  Endpoint" $cfg.OpenAiEndpoint
    $cfg.OpenAiApiKey = Read-PlainText "  API Key" $cfg.OpenAiApiKey -Secret
    $cfg.OpenAiDeploymentName = Read-PlainText "  Chat Deployment" $cfg.OpenAiDeploymentName
    $cfg.OpenAiEmbeddingDeployment = Read-PlainText "  Embedding Deployment" $cfg.OpenAiEmbeddingDeployment
    $cfg.OpenAiEmbeddingDimensions = Read-PlainText "  Embedding Dimensions" ([string]$cfg.OpenAiEmbeddingDimensions)

    Write-Host ""
    Write-Host "  Admin (/admin):" -ForegroundColor Gray
    if ([string]::IsNullOrWhiteSpace($cfg.AdminPassword)) {
        $gen = Read-Host "  Zufaellige Admin-Passwoerter erzeugen? [J/n]"
        if ($gen -ne "n" -and $gen -ne "N") {
            $cfg.AdminPassword = New-RandomSecret
            $cfg.HotelPassword = New-RandomSecret
            $cfg.TokenSecret = New-RandomSecret 32
            Write-OK "Admin-Passwoerter wurden erzeugt und werden gespeichert."
        }
    }
    if ([string]::IsNullOrWhiteSpace($cfg.AdminPassword)) {
        $cfg.AdminPassword = Read-PlainText "  Admin-Passwort" "" -Secret
        $cfg.HotelPassword = Read-PlainText "  Hotel-Passwort" "" -Secret
        $cfg.TokenSecret = Read-PlainText "  Token Secret" "" -Secret
    }

    $cfg.ApiPort = [int](Read-PlainText "Interner API-Port" ([string]$cfg.ApiPort))
    $cfg.McpPort = [int](Read-PlainText "Interner MCP-Port" ([string]$cfg.McpPort))

    Save-DeployConfig $cfg
    return $cfg
}

function Get-SshIdentityArgs {
    $keyPath = Join-Path $env:USERPROFILE ".ssh\id_ed25519_bestalpine"
    if (Test-Path $keyPath) {
        return @("-i", $keyPath, "-o", "IdentitiesOnly=yes")
    }
    return @()
}

function Invoke-SshCapture {
    param(
        [string]$Target,
        [string]$Command
    )
    $sshArgs = @(Get-SshIdentityArgs) + @("-o", "StrictHostKeyChecking=accept-new", $Target, $Command)
    $output = & ssh @sshArgs 2>&1 | ForEach-Object { "$_" }
    return @{
        ExitCode = $LASTEXITCODE
        Output   = ($output -join "`n")
    }
}

function Get-RemoteNodeBootstrapScript {
    return @'
if command -v node >/dev/null 2>&1; then
  command -v node
  exit 0
fi
export NVM_DIR="$HOME/.nvm"
if [ -s "$NVM_DIR/nvm.sh" ]; then
  . "$NVM_DIR/nvm.sh"
  if ! command -v node >/dev/null 2>&1; then
    nvm install 20
    nvm alias default 20
    nvm use 20
  fi
  command -v node
  exit 0
fi
curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.2/install.sh | bash
. "$NVM_DIR/nvm.sh"
nvm install 20
nvm alias default 20
nvm use 20
command -v node
'@ -replace "`r", ""
}

function Test-ProductionDeployment {
    param(
        [hashtable]$Config,
        [string]$SshTarget
    )

    Write-Header "Produktions-Checks"
    $failed = $false

    $status = Invoke-SshCapture $SshTarget "systemctl --user is-active hotelchatbot-api.service mcp.service; echo '---'; systemctl --user --no-pager --lines=5 status hotelchatbot-api.service; echo '---'; systemctl --user --no-pager --lines=5 status mcp.service"

    Write-Host $status.Output

    $api = Invoke-SshCapture $SshTarget "curl -sf http://127.0.0.1:$($Config.ApiPort)/health"
    if ($api.ExitCode -eq 0) {
        Write-OK "API /health: $($api.Output.Trim())"
    } else {
        Write-Err "API /health fehlgeschlagen"
        $failed = $true
        $logs = Invoke-SshCapture $SshTarget "journalctl --user -u hotelchatbot-api.service -n 15 --no-pager"
        Write-Host $logs.Output -ForegroundColor DarkGray
    }

    $mcp = Invoke-SshCapture $SshTarget "curl -sf http://127.0.0.1:$($Config.McpPort)/health"
    if ($mcp.ExitCode -eq 0) {
        Write-OK "MCP /health: $($mcp.Output.Trim())"
    } else {
        Write-Err "MCP /health fehlgeschlagen"
        $failed = $true
        $logs = Invoke-SshCapture $SshTarget "journalctl --user -u mcp.service -n 15 --no-pager"
        Write-Host $logs.Output -ForegroundColor DarkGray
    }

    if ($Config.PublicUrl) {
        $publicUrl = "$($Config.PublicUrl.TrimEnd('/'))/health"
        try {
            $response = Invoke-WebRequest -Uri $publicUrl -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop
            Write-OK "Oeffentliche URL $publicUrl -> HTTP $($response.StatusCode)"
        } catch {
            Write-Warn "Oeffentliche URL noch nicht erreichbar: $publicUrl"
            Write-Host "  $($_.Exception.Message)" -ForegroundColor DarkGray
            Write-Host "  Reverse Proxy bei Mynet noetig: $($Config.PublicUrl) -> http://127.0.0.1:$($Config.McpPort)" -ForegroundColor DarkGray
        }
    }

    if ($failed) {
        throw "Produktions-Checks fehlgeschlagen. Logs oben pruefen."
    }
}

function Invoke-SshCommand {
    param(
        [string]$Target,
        [string]$Command
    )
    $sshArgs = @(Get-SshIdentityArgs) + @("-o", "StrictHostKeyChecking=accept-new", $Target, $Command)
    & ssh @sshArgs
    if ($LASTEXITCODE -ne 0) {
        throw "SSH fehlgeschlagen: $Command"
    }
}

function Invoke-ScpRecursive {
    param(
        [string]$Source,
        [string]$Destination
    )
    $scpArgs = @(Get-SshIdentityArgs) + @("-r", $Source, "${Destination}")
    & scp @scpArgs
    if ($LASTEXITCODE -ne 0) {
        throw "SCP fehlgeschlagen: $Source -> $Destination"
    }
}

function Test-SshAccess {
    param([string]$Target)

    $sshArgs = @(Get-SshIdentityArgs) + @("-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "StrictHostKeyChecking=accept-new", $Target, "echo ok")
    & ssh @sshArgs 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Show-SshHelp {
    param([string]$Target)

    Write-Warn "SSH ohne Passwort funktioniert noch nicht."
    Write-Host ""
    Write-Host "  Einmalig SSH-Key einrichten (empfohlen):" -ForegroundColor Yellow
    Write-Host "  ssh-keygen -t ed25519 -f `"`$env:USERPROFILE\.ssh\id_ed25519_bestalpine`" -N `"`"`"" -ForegroundColor Gray
    Write-Host "  type `"`$env:USERPROFILE\.ssh\id_ed25519_bestalpine.pub`" | ssh $Target `"mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys`"" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Alternativ: Deploy trotzdem starten - ssh/scp fragen dann nach dem Passwort." -ForegroundColor Gray
    Write-Host ""
}

function Build-Application {
    Write-Header "Build"

    if (-not (Test-CliCommand "dotnet")) {
        throw "dotnet nicht gefunden. Bitte .NET SDK installieren."
    }
    if (-not (Test-CliCommand "npm")) {
        throw "npm nicht gefunden. Bitte Node.js installieren."
    }

    Write-Step "Frontend bauen..."
    $frontendDir = Join-Path $root "frontend"
    Push-Location $frontendDir
    try {
        if (-not (Test-Path "node_modules")) {
            & npm install --silent
            if ($LASTEXITCODE -ne 0) { throw "npm install fehlgeschlagen" }
        }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend-Build fehlgeschlagen" }
    } finally {
        Pop-Location
    }

    $wwwroot = Join-Path $root "src\HotelChatbot.Api\wwwroot"
    $demoDir = Join-Path $wwwroot "demo"
    if (-not (Test-Path $demoDir)) {
        New-Item -ItemType Directory -Path $demoDir -Force | Out-Null
    }
    Copy-Item (Join-Path $frontendDir "dist\chatbot.js") $demoDir -Force
    Copy-Item (Join-Path $frontendDir "demo.html") (Join-Path $demoDir "index.html") -Force
    Write-OK "Frontend nach wwwroot\demo kopiert"

    Write-Step "API fuer Linux bauen..."
    if (Test-Path $buildOutput) {
        Remove-Item -Recurse -Force $buildOutput
    }
    & dotnet publish "src\HotelChatbot.Api\HotelChatbot.Api.csproj" `
        --configuration Release `
        --runtime linux-x64 `
        --self-contained true `
        --output $buildOutput `
        /p:PublishSingleFile=false `
        --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }
    Write-OK "Build fertig: publish\linux-x64"
}

function New-ProductionAppSettings {
    param([hashtable]$Config)

    if (-not (Test-Path $templatePath)) {
        throw "Template fehlt: deploy\appsettings.Production.template.json"
    }

    $template = Get-Content $templatePath -Raw
    $pgConn = "Host=$($Config.PostgresHost);Port=$($Config.PostgresPort);Database=$($Config.PostgresDatabase);Username=$($Config.PostgresUser);Password=$($Config.PostgresPassword);SSL Mode=Prefer;Trust Server Certificate=true"

    $content = $template `
        -replace '\{\{POSTGRES_CONNECTION_STRING\}\}', ($pgConn -replace '\\','\\\\') `
        -replace '\{\{OPENAI_ENDPOINT\}\}', $Config.OpenAiEndpoint `
        -replace '\{\{OPENAI_API_KEY\}\}', $Config.OpenAiApiKey `
        -replace '\{\{OPENAI_DEPLOYMENT\}\}', $Config.OpenAiDeploymentName `
        -replace '\{\{OPENAI_EMBEDDING_DEPLOYMENT\}\}', $Config.OpenAiEmbeddingDeployment `
        -replace '\{\{OPENAI_EMBEDDING_DIMENSIONS\}\}', $Config.OpenAiEmbeddingDimensions `
        -replace '\{\{SPEECH_KEY\}\}', $Config.SpeechSubscriptionKey `
        -replace '\{\{SPEECH_REGION\}\}', $Config.SpeechServiceRegion `
        -replace '\{\{ELEVENLABS_KEY\}\}', $Config.ElevenLabsApiKey `
        -replace '\{\{ELEVENLABS_VOICE\}\}', $Config.ElevenLabsVoiceId `
        -replace '\{\{ADMIN_PASSWORD\}\}', $Config.AdminPassword `
        -replace '\{\{HOTEL_PASSWORD\}\}', $Config.HotelPassword `
        -replace '\{\{TOKEN_SECRET\}\}', $Config.TokenSecret

    $content | Set-Content (Join-Path $buildOutput "appsettings.Production.json") -Encoding UTF8
    Write-OK "appsettings.Production.json erzeugt"
}

function Publish-ToServer {
    param([hashtable]$Config)

    if (-not (Test-CliCommand "ssh") -or -not (Test-CliCommand "scp")) {
        throw 'OpenSSH fehlt. Windows: Einstellungen - Apps - Optionale Features - OpenSSH Client'
    }

    $sshTarget = "$($Config.SshUser)@$($Config.SshHost)"

    if (-not (Test-SshAccess $sshTarget)) {
        Show-SshHelp $sshTarget
        $cont = Read-Host "Trotzdem fortfahren? [j/N]"
        if ($cont -ne "j" -and $cont -ne "J") { exit 0 }
    }

    if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
    New-Item -ItemType Directory -Path $mcpStaging -Force | Out-Null
    Copy-Item (Join-Path $root "chatgpt\index.js") $mcpStaging
    Copy-Item (Join-Path $root "chatgpt\package.json") $mcpStaging

    Write-Header "Upload nach $sshTarget"

    Write-Step "Server vorbereiten..."
    $setupScript = @"
mkdir -p '$($Config.RemoteAppDir)' '$($Config.RemoteMcpDir)'
if command -v loginctl >/dev/null 2>&1; then loginctl enable-linger `$(whoami) 2>/dev/null || true; fi
"@ -replace "`r", ""
    Invoke-SshCommand $sshTarget $setupScript

    Write-Step "API hochladen (kann einige Minuten dauern)..."
    Invoke-ScpRecursive "$buildOutput/*" "${sshTarget}:$($Config.RemoteAppDir)/"

    Write-Step "MCP hochladen..."
    Invoke-ScpRecursive "$mcpStaging/*" "${sshTarget}:$($Config.RemoteMcpDir)/"

    Write-Step "Node.js pruefen/installieren, npm und systemd..."
    $nodeBootstrap = Get-RemoteNodeBootstrapScript
    $nodeResult = Invoke-SshCapture $sshTarget $nodeBootstrap
    if ($nodeResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($nodeResult.Output)) {
        throw "Node.js konnte auf dem Server nicht installiert werden. Ausgabe: $($nodeResult.Output)"
    }
    $nodeBin = ($nodeResult.Output -split "`n" | Where-Object { $_ -match '/node$' } | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($nodeBin)) {
        throw "Node-Binary nicht gefunden nach Bootstrap: $($nodeResult.Output)"
    }
    Write-OK "Node.js: $nodeBin"

    $apiService = (Get-Content (Join-Path $root "deploy\systemd\hotelchatbot-api.service") -Raw) `
        -replace '\{\{REMOTE_APP_DIR\}\}', $Config.RemoteAppDir `
        -replace '\{\{API_PORT\}\}', $Config.ApiPort
    $mcpService = (Get-Content (Join-Path $root "deploy\systemd\mcp.service") -Raw) `
        -replace '\{\{REMOTE_MCP_DIR\}\}', $Config.RemoteMcpDir `
        -replace '\{\{REMOTE_APP_DIR\}\}', $Config.RemoteAppDir `
        -replace '\{\{API_PORT\}\}', $Config.ApiPort `
        -replace '\{\{MCP_PORT\}\}', $Config.McpPort `
        -replace '\{\{NODE_BIN\}\}', $nodeBin

    $apiB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($apiService))
    $mcpB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($mcpService))

    $remoteScript = @"
set -e
export NVM_DIR="`$HOME/.nvm"
[ -s "`$NVM_DIR/nvm.sh" ] && . "`$NVM_DIR/nvm.sh"
chmod +x '$($Config.RemoteAppDir)/HotelChatbot.Api'
cd '$($Config.RemoteMcpDir)'
npm install --omit=dev --no-audit --no-fund
mkdir -p `$HOME/.config/systemd/user
echo '$apiB64' | base64 -d > `$HOME/.config/systemd/user/hotelchatbot-api.service
echo '$mcpB64' | base64 -d > `$HOME/.config/systemd/user/mcp.service
systemctl --user daemon-reload
systemctl --user enable hotelchatbot-api.service mcp.service
systemctl --user restart hotelchatbot-api.service
sleep 5
systemctl --user restart mcp.service
sleep 2
"@ -replace "`r", ""
    Invoke-SshCommand $sshTarget $remoteScript

    Test-ProductionDeployment -Config $Config -SshTarget $sshTarget

    if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
}

function Show-Summary {
    param([hashtable]$Config)

    Write-Header "Fertig"
    Write-OK "API intern: http://127.0.0.1:$($Config.ApiPort)"
    Write-OK "MCP intern: http://127.0.0.1:$($Config.McpPort)"
    Write-Host ""
    Write-Host "  ChatGPT Connector URL:" -ForegroundColor Yellow
    Write-Host "  $($Config.PublicUrl)/sse" -ForegroundColor White
    Write-Host ""
    Write-Host "  Falls von aussen noch 502 kommt, Mynet bitten:" -ForegroundColor Yellow
    Write-Host "  $($Config.PublicUrl) -> http://127.0.0.1:$($Config.McpPort)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Logs auf dem Server:" -ForegroundColor Yellow
    Write-Host "  journalctl --user -u hotelchatbot-api.service -f" -ForegroundColor Gray
    Write-Host "  journalctl --user -u mcp.service -f" -ForegroundColor Gray
}

# =============================================================================
# MAIN
# =============================================================================

Write-Header "BestWellness Production Deploy"

$cfg = Load-DeployConfig
$hasConfig = ($null -ne $cfg)

if ($Reconfigure -or -not $hasConfig -or (Test-ConfigComplete $cfg).Count -gt 0) {
    $cfg = Initialize-DeployConfig -Existing $cfg -Force:$Reconfigure
} elseif (-not $NonInteractive -and -not $BuildOnly -and -not $SkipBuild) {
    Write-Host ""
    Write-Host "  Gespeicherte Konfiguration: $($cfg.SshUser)@$($cfg.SshHost)" -ForegroundColor Gray
    Write-Host "  [Enter] Deploy starten   [E] Konfiguration   [B] Nur bauen   [Q] Beenden" -ForegroundColor Gray
    $choice = Read-Host "  Auswahl"
    switch ($choice.ToUpper()) {
        "E" { $cfg = Initialize-DeployConfig -Existing $cfg -Force; $Reconfigure = $true }
        "B" { $BuildOnly = $true }
        "Q" { exit 0 }
        default { }
    }
}

try {
    if (-not $SkipBuild) {
        Build-Application
    } elseif (-not (Test-Path (Join-Path $buildOutput "HotelChatbot.Api"))) {
        throw "Kein Build in publish\linux-x64. Starten Sie ohne -SkipBuild."
    }

    New-ProductionAppSettings $cfg

    if ($BuildOnly) {
        Write-OK "Nur Build - kein Upload."
        exit 0
    }

    Publish-ToServer $cfg
    Show-Summary $cfg

    if ($MigrateDatabase) {
        & (Join-Path $root "scripts\migrate-production-database.ps1") -NonInteractive
    }
}
catch {
    Write-Err $_.Exception.Message
    exit 1
}
