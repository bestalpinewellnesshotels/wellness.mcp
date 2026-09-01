# Deployment-Package fuer ChatGPT MCP-Server erstellen

Write-Host "Erstelle MCP-Server Deployment-Package..." -ForegroundColor Green

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$packageName = "hotelchatbot-mcp-$timestamp.zip"
$tempDir = "temp_mcp_package"

# Cleanup altes temp Verzeichnis
if (Test-Path $tempDir) {
    Remove-Item -Recurse -Force $tempDir
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# Kopiere notwendige Dateien
Copy-Item "chatgpt\index.js" "$tempDir\"
Copy-Item "chatgpt\package.json" "$tempDir\"
Copy-Item "chatgpt\README.md" "$tempDir\"
# Simulation (sim-proxy / public) ist optional und nur fuer lokale Debug-UI noetig.

# Erstelle .env
"PORT=3001`nAPI_BASE_URL=http://localhost:5001" | Out-File -FilePath "$tempDir\.env" -Encoding UTF8

# Erstelle DEPLOY.md
$deployContent = @"
# MCP Server Deployment

## 1. Dateien auf Server kopieren
scp $packageName user@server:/home/user/

## 2. Auf Server entpacken
cd /opt
sudo mkdir -p hotelchatbot-mcp
cd hotelchatbot-mcp
sudo unzip /home/user/$packageName

## 3. .env anpassen
sudo nano .env
# Setze API_BASE_URL auf die URL der HotelChatbot.Api

## 4. Dependencies installieren
npm install

## 5. Mit PM2 starten
pm2 restart hotelchatbot-mcp 2>/dev/null || pm2 start index.js --name hotelchatbot-mcp
pm2 save

## 6. Verifizieren
curl http://localhost:3001
pm2 logs hotelchatbot-mcp

## 7. WICHTIG: ChatGPT Custom GPT aktualisieren!
Siehe GPT-INSTRUCTIONS.md im Hauptverzeichnis
"@
$deployContent | Out-File -FilePath "$tempDir\DEPLOY.md" -Encoding UTF8

# Erstelle Package
Compress-Archive -Path "$tempDir\*" -DestinationPath $packageName -Force

# Cleanup
Remove-Item -Recurse -Force $tempDir

$fileSize = [math]::Round((Get-Item $packageName).Length / 1KB, 2)

Write-Host ""
Write-Host "Deployment-Package erfolgreich erstellt!" -ForegroundColor Green
Write-Host "Datei: $packageName" -ForegroundColor Cyan
Write-Host "Groesse: $fileSize KB" -ForegroundColor Yellow

Write-Host ""
Write-Host "=== DEPLOYMENT SCHRITTE ===" -ForegroundColor Magenta
Write-Host "1. Kopiere $packageName auf deinen Linux-Server" -ForegroundColor White
Write-Host "   scp $packageName user@server:/home/user/" -ForegroundColor Gray

Write-Host ""
Write-Host "2. Entpacke und deploye auf dem Server (siehe DEPLOY.md im Package)" -ForegroundColor White

Write-Host ""
Write-Host "3. KRITISCH: Aktualisiere dein ChatGPT Custom GPT!" -ForegroundColor Red
Write-Host "   Oeffne: GPT-INSTRUCTIONS.md" -ForegroundColor White
Write-Host "   Kopiere die Instructions EXAKT in dein Custom GPT" -ForegroundColor White

Write-Host ""
Write-Host "=== LOKALER TEST ===" -ForegroundColor Magenta
Write-Host "Teste lokal, ob der MCP-Server korrekte Antworten liefert:" -ForegroundColor White
Write-Host "   .\test-mcp-responses.ps1" -ForegroundColor Cyan

Write-Host ""
Write-Host "Fertig!" -ForegroundColor Green
