#!/bin/bash
# Docker Build & Run Script für HotelChatbot.Api

echo "Building Docker Image for HotelChatbot.Api..."

# Build Docker Image
docker build -t hotelchatbot-api:latest -f src/HotelChatbot.Api/Dockerfile src/

if [ $? -eq 0 ]; then
    echo ""
    echo "Docker Image erfolgreich erstellt!"
    
    echo ""
    echo "=== Docker Commands ==="
    
    echo ""
    echo "1. Lokaler Test:"
    echo "docker run -d -p 5001:8080 --name hotelchatbot-api hotelchatbot-api:latest"
    
    echo ""
    echo "2. Mit Environment-Variablen:"
    echo "docker run -d -p 5001:8080 \\"
    echo "  -e ASPNETCORE_ENVIRONMENT=Production \\"
    echo "  -e 'ConnectionStrings__PostgreSQL=Host=dev-universe.net;...' \\"
    echo "  --name hotelchatbot-api hotelchatbot-api:latest"
    
    echo ""
    echo "3. Mit docker-compose:"
    echo "docker-compose up -d"
    
    echo ""
    echo "4. Image auf Server laden:"
    echo "docker save hotelchatbot-api:latest | gzip > hotelchatbot-api.tar.gz"
    echo "scp hotelchatbot-api.tar.gz user@server:/tmp/"
    echo "# Auf dem Server:"
    echo "docker load < /tmp/hotelchatbot-api.tar.gz"
    
    echo ""
    echo "5. Logs anzeigen:"
    echo "docker logs -f hotelchatbot-api"
    
    echo ""
    echo "6. Container stoppen:"
    echo "docker stop hotelchatbot-api"
    echo "docker rm hotelchatbot-api"
    
else
    echo ""
    echo "Docker Build fehlgeschlagen!"
    exit 1
fi
