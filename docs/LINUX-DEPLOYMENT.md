# Linux Deployment - Schnellstart

## 🚀 Option 1: Self-Contained Binary (empfohlen für direkte Installation)

### Auf Windows erstellen:
```powershell
.\publish-linux.ps1
```

### Auf Linux erstellen:
```bash
chmod +x publish-linux.sh
./publish-linux.sh
```

**Ergebnis:** ZIP/TAR mit kompletter .NET Runtime (keine .NET Installation auf Server nötig)

**Deployment:**
```bash
# Auf Linux Server:
unzip hotelchatbot-api-linux-*.zip -d /opt/hotelchatbot
cd /opt/hotelchatbot

# Executable Permission setzen (WICHTIG!)
chmod +x HotelChatbot.Api

# Konfiguration anpassen (optional - Standardwerte bereits gesetzt)
nano appsettings.Production.json

# Starten der nativen Binary (NICHT die DLL!)
./HotelChatbot.Api

# Läuft jetzt auf http://localhost:8080
# Health Check: curl http://localhost:8080/api/chat/health
```

**⚠️ WICHTIG:** 
- Starte `./HotelChatbot.Api` (native Binary)
- **NICHT** `dotnet HotelChatbot.Api.dll` (würde system-weite .NET suchen)
- Das Self-Contained Package enthält die komplette Runtime

---

## 🐳 Option 2: Docker Container (empfohlen für IONOS/Cloud)

### Auf Windows:
```powershell
.\build-docker.ps1
```

### Auf Linux:
```bash
chmod +x build-docker.sh
./build-docker.sh
```

**Lokaler Test:**
```bash
docker run -d -p 5001:8080 --name hotelchatbot-api hotelchatbot-api:latest
curl http://localhost:5001/api/chat/health
```

**Deployment auf Server:**
```bash
# Image exportieren
docker save hotelchatbot-api:latest | gzip > hotelchatbot-api.tar.gz

# Auf Server kopieren
scp hotelchatbot-api.tar.gz user@server:/tmp/

# Auf Server laden
ssh user@server
docker load < /tmp/hotelchatbot-api.tar.gz

# Starten
docker run -d \
  -p 5001:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  --name hotelchatbot-api \
  --restart unless-stopped \
  hotelchatbot-api:latest
```

---

## 🎯 Option 3: Docker Compose (Komplett-Setup mit MCP)

```bash
# Auf Server
cd /opt/hotelchatbot
docker-compose up -d

# Logs
docker-compose logs -f

# Status
docker-compose ps

# Stoppen
docker-compose down
```

**Services:**
- API: http://localhost:5001
- MCP: http://localhost:3001

---

## 🔧 Als Systemd Service (Native Binary)

Nach Self-Contained Publish:

```bash
sudo nano /etc/systemd/system/hotelchatbot-api.service
```

```ini
[Unit]
Description=HotelChatbot API
After=network.target

[Service]
Type=notify
User=www-data
WorkingDirectory=/opt/hotelchatbot
ExecStart=/opt/hotelchatbot/HotelChatbot.Api
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable hotelchatbot-api
sudo systemctl start hotelchatbot-api
sudo systemctl status hotelchatbot-api
```

---

## ✅ Systemanforderungen

### Self-Contained Binary:
- ✅ **Keine .NET Installation nötig** (Runtime ist im Package enthalten)
- Linux x64 (Ubuntu 18.04+, Debian 9+, CentOS 7+, etc.)
- 512 MB RAM minimum, 1 GB empfohlen
- 200 MB Disk Space
- **Start**: `./HotelChatbot.Api` (native Binary, nicht die DLL!)

### Docker:
- Docker Engine 20.10+
- Linux Kernel 3.10+
- 512 MB RAM minimum, 1 GB empfohlen

---

## 🧪 Testen

```bash
# Health Check
curl http://localhost:5001/api/chat/health

# Empfehlung testen
curl -X POST http://localhost:5001/api/chat/recommend \
  -H "Content-Type: application/json" \
  -d '{
    "requirements": "Hotel mit Pool und Wellness",
    "maxResults": 3,
    "language": "de"
  }'
```

---

## 📝 Hinweise

1. **Self-Contained** ist größer (~80 MB) aber einfacher zu deployen
2. **Docker** ist kleiner und einfacher zu updaten
3. **Docker Compose** startet API + MCP Server zusammen

Für detaillierte Nginx/SSL-Konfiguration siehe [DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md)
