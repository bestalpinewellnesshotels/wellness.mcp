#!/bin/bash
# Publish-Script für Linux-Deployment
# Erstellt Self-Contained Build mit .NET Runtime

echo "Building HotelChatbot.Api for Linux x64 (self-contained)..."

SOURCE_PATH="src/HotelChatbot.Api"
OUTPUT_PATH="publish/linux-x64"

# Clean previous builds
if [ -d "$OUTPUT_PATH" ]; then
    echo "Cleaning previous build..."
    rm -rf "$OUTPUT_PATH"
fi

# Publish for Linux x64 with runtime included
echo "Publishing..."
dotnet publish "$SOURCE_PATH" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$OUTPUT_PATH" \
    /p:PublishSingleFile=false \
    /p:IncludeNativeLibrariesForSelfExtract=true

if [ $? -eq 0 ]; then
    echo ""
    echo "Build erfolgreich!"
    echo "Output: $OUTPUT_PATH"
    
    # Create deployment package
    echo ""
    echo "Erstelle Deployment-Paket..."
    TIMESTAMP=$(date +"%Y%m%d-%H%M%S")
    ZIP_FILE="hotelchatbot-api-linux-$TIMESTAMP.tar.gz"
    
    tar -czf "$ZIP_FILE" -C "$OUTPUT_PATH" .
    
    echo ""
    echo "Deployment-Paket erstellt: $ZIP_FILE"
    echo "Größe: $(du -h $ZIP_FILE | cut -f1)"
    
    echo ""
    echo "=== Deployment-Anleitung ==="
    echo "1. Kopiere $ZIP_FILE auf deinen Linux-Server:"
    echo "   scp $ZIP_FILE user@server:/home/user/"
    echo ""
    echo "2. Auf dem Server entpacken:"
    echo "   tar -xzf $ZIP_FILE -C /opt/hotelchatbot"
    echo ""
    echo "3. Konfiguration anpassen:"
    echo "   nano /opt/hotelchatbot/appsettings.Production.json"
    echo ""
    echo "4. Executable Permission setzen:"
    echo "   chmod +x /opt/hotelchatbot/HotelChatbot.Api"
    echo ""
    echo "5. Starten:"
    echo "   cd /opt/hotelchatbot"
    echo "   ./HotelChatbot.Api"
    echo ""
    echo "Oder als Service (siehe DEPLOYMENT-GUIDE.md)"
else
    echo ""
    echo "Build fehlgeschlagen!"
    exit 1
fi
