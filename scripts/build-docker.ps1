# Docker Build & Run Script für HotelChatbot.Api

Write-Host "Building Docker Image for HotelChatbot.Api..." -ForegroundColor Green

# Build Docker Image
docker build -t hotelchatbot-api:latest -f src/HotelChatbot.Api/Dockerfile src/

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nDocker Image erfolgreich erstellt!" -ForegroundColor Green
    
    Write-Host "`n=== Docker Commands ===" -ForegroundColor Magenta
    
    Write-Host "`n1. Lokaler Test:" -ForegroundColor Cyan
    Write-Host "docker run -d -p 5001:8080 --name hotelchatbot-api hotelchatbot-api:latest" -ForegroundColor Gray
    
    Write-Host "`n2. Mit Environment-Variablen:" -ForegroundColor Cyan
    Write-Host "docker run -d -p 5001:8080 \" -ForegroundColor Gray
    Write-Host "  -e ASPNETCORE_ENVIRONMENT=Production \" -ForegroundColor Gray
    Write-Host "  -e 'ConnectionStrings__PostgreSQL=Host=dev-universe.net;...' \" -ForegroundColor Gray
    Write-Host "  --name hotelchatbot-api hotelchatbot-api:latest" -ForegroundColor Gray
    
    Write-Host "`n3. Mit docker-compose:" -ForegroundColor Cyan
    Write-Host "docker-compose up -d" -ForegroundColor Gray
    
    Write-Host "`n4. Image auf Server laden:" -ForegroundColor Cyan
    Write-Host "docker save hotelchatbot-api:latest | gzip > hotelchatbot-api.tar.gz" -ForegroundColor Gray
    Write-Host "scp hotelchatbot-api.tar.gz user@server:/tmp/" -ForegroundColor Gray
    Write-Host "# Auf dem Server:" -ForegroundColor Yellow
    Write-Host "docker load < /tmp/hotelchatbot-api.tar.gz" -ForegroundColor Gray
    
    Write-Host "`n5. Logs anzeigen:" -ForegroundColor Cyan
    Write-Host "docker logs -f hotelchatbot-api" -ForegroundColor Gray
    
    Write-Host "`n6. Container stoppen:" -ForegroundColor Cyan
    Write-Host "docker stop hotelchatbot-api" -ForegroundColor Gray
    Write-Host "docker rm hotelchatbot-api" -ForegroundColor Gray
    
} else {
    Write-Host "`nDocker Build fehlgeschlagen!" -ForegroundColor Red
    exit 1
}
