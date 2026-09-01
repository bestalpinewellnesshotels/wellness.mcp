#!/bin/bash
# Deployment-Package für ChatGPT MCP-Server erstellen

echo "Erstelle MCP-Server Deployment-Package..."

TIMESTAMP=$(date +%Y%m%d-%H%M%S)
PACKAGE_NAME="hotelchatbot-mcp-$TIMESTAMP.tar.gz"

# Erstelle temporäres Verzeichnis
TEMP_DIR="temp_mcp_package"
mkdir -p $TEMP_DIR

# Kopiere notwendige Dateien
cp chatgpt/index.js $TEMP_DIR/
cp chatgpt/package.json $TEMP_DIR/
cp chatgpt/README.md $TEMP_DIR/
cp chatgpt/.env.example $TEMP_DIR/.env
# Simulation (sim-proxy / public) ist optional und nur fuer lokale Debug-UI noetig.

echo "PORT=3001" >> $TEMP_DIR/.env
echo "API_BASE_URL=http://localhost:5001" >> $TEMP_DIR/.env

# Erstelle Deployment-Anleitung
cat > $TEMP_DIR/DEPLOY.md << 'EOF'
# MCP Server Deployment

## 1. Dateien auf Server kopieren
```bash
scp hotelchatbot-mcp-*.tar.gz user@server:/home/user/
```

## 2. Auf Server entpacken
```bash
cd /opt
sudo mkdir -p hotelchatbot-mcp
cd hotelchatbot-mcp
sudo tar -xzf /home/user/hotelchatbot-mcp-*.tar.gz
```

## 3. .env anpassen
```bash
sudo nano .env
```
Setze `API_BASE_URL` auf die URL der HotelChatbot.Api

## 4. Dependencies installieren
```bash
npm install
```

## 5. Mit PM2 starten
```bash
pm2 restart hotelchatbot-mcp || pm2 start index.js --name hotelchatbot-mcp
pm2 save
```

## 6. Verifizieren
```bash
curl http://localhost:3001
# Sollte antworten: "HotelChatbot MCP Server V1.0.4 ready"
```
EOF

# Erstelle Package
tar -czf $PACKAGE_NAME -C $TEMP_DIR .

# Cleanup
rm -rf $TEMP_DIR

echo "✓ Deployment-Package erstellt: $PACKAGE_NAME"
ls -lh $PACKAGE_NAME

echo ""
echo "=== Deployment Schritte ==="
echo "1. Kopiere $PACKAGE_NAME auf deinen Server"
echo "2. Folge den Anweisungen in DEPLOY.md (im Package enthalten)"
echo "3. Aktualisiere dein ChatGPT Custom GPT mit GPT-INSTRUCTIONS.md"
