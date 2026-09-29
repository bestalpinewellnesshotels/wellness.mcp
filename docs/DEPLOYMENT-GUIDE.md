# 🚀 IONOS Server Deployment - Schritt für Schritt

## Überblick der Komponenten

```
┌─────────────────────────────────────────────────────────────────┐
│                         IONOS Server                            │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  Nginx (Port 80/443)                                    │    │
│  │  ├─ https://api.your-domain.com → :5001 (.NET API)      │    │
│  │  └─ https://chatgpt.your-domain.com → :3001 (Node MCP) │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ┌──────────────────────┐  ┌──────────────────────────┐        │
│  │ .NET API (Port 5001) │  │ Node MCP (Port 3001)     │        │
│  │ HotelChatbot.Api     │←─│ chatgpt/index.js         │        │
│  │ - RAG Chat           │  │ - ChatGPT Integration    │        │
│  │ - Recommendations    │  │ - MCP Protocol           │        │
│  │ - Voice Services     │  │ - Tool Definitions       │        │
│  └──────────┬───────────┘  └──────────────────────────┘        │
│             │                                                    │
│             └─────────────────────────┐                         │
└───────────────────────────────────────┼─────────────────────────┘
                                        │
                                        ▼
                    ┌──────────────────────────────────┐
                    │ PostgreSQL + pgvector            │
                    │ dev-universe.net:5432            │
                    │ - Datenbank: Bwchat              │
                    │ - 301 Content Chunks             │
                    │ - HNSW Index                     │
                    └──────────────────────────────────┘
```

---

## 📦 SCHRITT 1: Voraussetzungen prüfen

### Auf deinem lokalen PC:

```powershell
# Projekt-Files vorbereiten
cd C:\Users\gerha\Documents\Programmierung\Chatbot

# Alle Änderungen committen (falls Git genutzt wird)
git status
git add .
git commit -m "Production ready"
```

### Auf dem IONOS Server:

SSH-Verbindung herstellen:
```bash
ssh your-user@your-ionos-server.com
```

**Installierte Software prüfen:**
```bash
# .NET SDK installieren (Version 10.0 oder höher benötigt)
dotnet --version

# Node.js installieren (Version 18+ benötigt)
node --version
npm --version

# Nginx installieren
nginx -v

# Certbot für HTTPS installieren
certbot --version
```

**Falls nicht installiert:**
```bash
# .NET 10 Preview installieren
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0

# Node.js installieren
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get install -y nodejs

# Nginx & Certbot installieren
sudo apt-get update
sudo apt-get install -y nginx certbot python3-certbot-nginx

# PM2 für Node.js Process Management
sudo npm install -g pm2
```

---

## 📁 SCHRITT 2: Files auf den Server übertragen

### Option A: Mit SCP (von lokalem PC):

```powershell
# Projekt als ZIP packen
cd C:\Users\gerha\Documents\Programmierung\Chatbot
Compress-Archive -Path src, chatgpt, appsettings.Production.json -DestinationPath hotelchatbot.zip

# Auf Server hochladen
scp hotelchatbot.zip your-user@your-server:/home/your-user/
```

### Option B: Mit Git:

```bash
# Auf dem Server:
cd /home/your-user
git clone https://your-repo-url.git hotelchatbot
cd hotelchatbot
```

### Option C: Mit FTP/SFTP (z.B. FileZilla):
- Verbinde zu deinem IONOS Server
- Lade `src/` und `chatgpt/` Ordner hoch nach `/home/your-user/hotelchatbot/`

---

## 🔧 SCHRITT 3: .NET API deploymen

```bash
cd /home/your-user/hotelchatbot/src/HotelChatbot.Api

# Production appsettings erstellen
nano appsettings.Production.json
```

**Inhalt von appsettings.Production.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "ConnectionStrings": {
    "PostgreSQL": "Host=dev-universe.net;Port=5432;Database=Bwchat;Username=bwchatuser;Password=YOUR_PASSWORD;SSL Mode=Prefer;Trust Server Certificate=true"
  },
  
  "OpenAI": {
    "Endpoint": "https://codeaustriaazureopenai.openai.azure.com/",
    "ApiKey": "YOUR_API_KEY",
    "DeploymentName": "gpt-4.1-mini",
    "EmbeddingDeploymentName": "text-embedding-3-small",
    "EmbeddingDimensions": 1536
  },

  "Chat": {
    "MinConfidenceScore": 0.7,
    "MaxRetrievalResults": 5,
    "MaxConversationHistory": 10
  },
  
  "SpeechService": {
    "SubscriptionKey": "YOUR_SPEECH_KEY",
    "ServiceRegion": "germanywestcentral"
  }
}
```

**API builden und starten:**
```bash
# Restore & Build
dotnet restore
dotnet build -c Release

# 🎭 WICHTIG: Playwright Browser installieren (für JavaScript-Websites)
pwsh bin/Release/net8.0/playwright.ps1 install chromium --with-deps

# Falls PowerShell nicht verfügbar:
sudo apt-get install -y powershell
pwsh bin/Release/net8.0/playwright.ps1 install chromium --with-deps

# Alternativ: Manuelle Installation (siehe docs/PLAYWRIGHT-SETUP.md)
sudo apt-get install -y libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 \
    libcups2 libdrm2 libdbus-1-3 libxkbcommon0 libxcomposite1 libxdamage1 \
    libxfixes3 libxrandr2 libgbm1 libasound2 libpango-1.0-0 libcairo2

# Als Service mit Systemd einrichten
sudo nano /etc/systemd/system/hotelchatbot-api.service
```

**Inhalt der Service-Datei:**
```ini
[Unit]
Description=HotelChatbot API
After=network.target

[Service]
Type=notify
User=your-user
WorkingDirectory=/home/your-user/hotelchatbot/src/HotelChatbot.Api
ExecStart=/usr/bin/dotnet run --configuration Release --urls "http://localhost:5001"
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

**Service aktivieren:**
```bash
sudo systemctl daemon-reload
sudo systemctl enable hotelchatbot-api
sudo systemctl start hotelchatbot-api
sudo systemctl status hotelchatbot-api
```

**Logs prüfen:**
```bash
sudo journalctl -u hotelchatbot-api -f
```

---

## 🟢 SCHRITT 4: Node.js MCP Server deploymen

```bash
cd /home/your-user/hotelchatbot/chatgpt

# Dependencies installieren
npm install --production

# Environment konfigurieren
cp .env.example .env
nano .env
```

**Inhalt der .env Datei:**
```env
PORT=3001
API_BASE_URL=http://localhost:5001
# API_KEY=optional_api_key_hier
```

**Mit PM2 als Service starten:**
```bash
# Start mit PM2
pm2 start index.js --name hotelchatbot-mcp

# Auto-Start bei Reboot aktivieren
pm2 save
pm2 startup

# Status prüfen
pm2 status
pm2 logs hotelchatbot-mcp
```

**Wichtige PM2 Befehle:**
```bash
pm2 restart hotelchatbot-mcp  # Restart
pm2 stop hotelchatbot-mcp     # Stop
pm2 delete hotelchatbot-mcp   # Remove
pm2 logs hotelchatbot-mcp     # Logs anzeigen
pm2 monit                     # Monitoring Dashboard
```

---

## 🌐 SCHRITT 5: Nginx Reverse Proxy konfigurieren

```bash
sudo nano /etc/nginx/sites-available/hotelchatbot
```

**Nginx Konfiguration:**
```nginx
# .NET API
server {
    listen 80;
    server_name api.your-domain.com;

    location / {
        proxy_pass http://localhost:5001;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Timeouts für lange Requests
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
}

# Node.js MCP Server (für ChatGPT)
server {
    listen 80;
    server_name chatgpt.your-domain.com;

    location / {
        proxy_pass http://localhost:3001;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # SSE-spezifische Headers
        proxy_buffering off;
        proxy_cache off;
        chunked_transfer_encoding off;
        proxy_read_timeout 3600s;
        proxy_connect_timeout 3600s;
        
        # CORS Headers
        add_header 'Access-Control-Allow-Origin' '*' always;
        add_header 'Access-Control-Allow-Methods' 'GET, POST, OPTIONS' always;
        add_header 'Access-Control-Allow-Headers' 'Content-Type, Authorization' always;
    }
}
```

**Nginx aktivieren:**
```bash
# Symlink erstellen
sudo ln -s /etc/nginx/sites-available/hotelchatbot /etc/nginx/sites-enabled/

# Test
sudo nginx -t

# Reload
sudo systemctl reload nginx
```

---

## 🔒 SCHRITT 6: HTTPS mit Let's Encrypt einrichten

```bash
# SSL-Zertifikate für beide Domains erstellen
sudo certbot --nginx -d api.your-domain.com -d chatgpt.your-domain.com

# Folge den Prompts:
# 1. E-Mail eingeben
# 2. Terms akzeptieren
# 3. "Redirect HTTP to HTTPS" wählen
```

**Auto-Renewal aktivieren:**
```bash
# Testen ob Auto-Renewal funktioniert
sudo certbot renew --dry-run

# Cronjob prüfen (sollte automatisch eingerichtet sein)
sudo crontab -l
```

---

## ✅ SCHRITT 7: Deployment testen

### Test 1: .NET API Health Check
```bash
curl http://localhost:5001/api/chat/health
curl https://api.your-domain.com/api/chat/health
```

**Erwartete Antwort:**
```json
{
  "status": "healthy",
  "timestamp": "2026-02-16T..."
}
```

### Test 2: Node.js MCP Server
```bash
curl http://localhost:3001/
curl https://chatgpt.your-domain.com/
```

**Erwartete Antwort:**
```
HotelChatbot MCP Server V1.0.4 ready
```

### Test 3: SSE Endpoint
```bash
curl https://chatgpt.your-domain.com/sse
```

**Erwartete Antwort** (SSE Event Stream):
```
event: endpoint
data: {"jsonrpc":"2.0"}
```

### Test 4: Hotel-Empfehlung
```bash
curl -X POST https://api.your-domain.com/api/chat/recommend \
  -H "Content-Type: application/json" \
  -d '{
    "requirements": "Hotel mit Pool und Wellness, Adults Only",
    "maxResults": 3,
    "language": "de"
  }'
```

### Test 5: MCP Tool via REST
```bash
curl -X POST https://chatgpt.your-domain.com/recommend_hotels \
  -H "Content-Type: application/json" \
  -d '{
    "requirements": "Hotel mit Pool und Hund erlaubt",
    "maxResults": 2
  }'
```

---

## 🎯 SCHRITT 8: ChatGPT Custom GPT einrichten

### 1. ChatGPT öffnen
- Gehe zu https://chat.openai.com
- Klicke auf "Explore GPTs" → "Create a GPT"

### 2. Basic Info
- **Name**: Hotel Berater Österreich
- **Description**: Empfiehlt Hotels in Österreich basierend auf spezifischen Anforderungen
- **Instructions**: 
```
Du bist ein Hotel-Experte für Österreich. Du hilfst Nutzern, das perfekte Hotel zu finden.

Nutze die verfügbaren Tools:
- recommend_hotels: Für neue Hotelsuchen basierend auf Anforderungen
- search_hotels: Um nach Hotels nach Name oder Ort zu suchen
- ask_hotel_question: Für Fragen zu einem spezifischen Hotel
- list_all_hotels: Um alle verfügbaren Hotels anzuzeigen

⚠️ KRITISCHE SICHERHEITSREGEL:
- Du darfst AUSSCHLIESSLICH Informationen aus den Tool-Antworten verwenden
- Wenn ein Tool "keine Hotels gefunden" meldet, ist dies die FINALE Antwort
- Suche NIEMALS im Internet nach Hotels oder Hotel-Informationen
- Verwende NIEMALS dein Vorwissen über Hotels
- Wenn ein Hotel nicht in der Datenbank ist, sage: "Dieses Hotel ist nicht in unserer Datenbank verfügbar"
- Erfinde KEINE Informationen

Antworte freundlich und detailliert. Gib immer die Hotel-Domain an, damit Nutzer direkt buchen können.
```

### 3. Actions konfigurieren

**"Create new action" klicken:**

**Authentication**:
- Type: None (oder API Key falls du einen in .env aktiviert hast)

**Schema**:
```json
{
  "openapi": "3.1.0",
  "info": {
    "title": "HotelChatbot MCP API",
    "description": "API für Hotel-Empfehlungen mit RAG",
    "version": "1.0.0"
  },
  "servers": [
    {
      "url": "https://chatgpt.your-domain.com"
    }
  ],
  "paths": {
    "/recommend_hotels": {
      "post": {
        "operationId": "recommendHotels",
        "summary": "Empfiehlt Hotels basierend auf Anforderungen",
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "requirements": {
                    "type": "string",
                    "description": "Anforderungen des Nutzers"
                  },
                  "maxResults": {
                    "type": "integer",
                    "default": 3
                  },
                  "language": {
                    "type": "string",
                    "enum": ["de", "en"],
                    "default": "de"
                  }
                },
                "required": ["requirements"]
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "Erfolgreiche Empfehlung"
          }
        }
      }
    },
    "/ask_hotel_question": {
      "post": {
        "operationId": "askHotelQuestion",
        "summary": "Stellt eine Frage zu einem Hotel",
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "hotelId": {
                    "type": "string"
                  },
                  "question": {
                    "type": "string"
                  },
                  "language": {
                    "type": "string",
                    "default": "de"
                  }
                },
                "required": ["hotelId", "question"]
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "Antwort auf Frage"
          }
        }
      }
    },
    "/list_all_hotels": {
      "get": {
        "operationId": "listAllHotels",
        "summary": "Listet alle Hotels auf",
        "responses": {
          "200": {
            "description": "Liste aller Hotels"
          }
        }
      }
    }
  }
}
```

**Server URL ersetzen**: `https://chatgpt.your-domain.com` → durch deine echte Domain ersetzen!

### 4. GPT testen
```
"Ich suche ein Hotel mit Pool und Wellness, Adults Only, in Tirol"
```

---

## 🔄 Updates deploymen

**Wenn du Änderungen am Code machst:**

```bash
# .NET API aktualisieren
cd /home/your-user/hotelchatbot/src/HotelChatbot.Api
git pull  # oder neue Files hochladen
dotnet build -c Release
sudo systemctl restart hotelchatbot-api

# Node.js MCP aktualisieren
cd /home/your-user/hotelchatbot/chatgpt
git pull  # oder neue Files hochladen
npm install
pm2 restart hotelchatbot-mcp
```

---

## 🐛 Troubleshooting

### API startet nicht:
```bash
# Logs prüfen
sudo journalctl -u hotelchatbot-api -n 100

# Port 5001 Check
sudo netstat -tulpn | grep 5001

# PostgreSQL Verbindung testen
psql -h dev-universe.net -U bwchatuser -d Bwchat
```

### MCP Server Probleme:
```bash
# Logs prüfen
pm2 logs hotelchatbot-mcp

# Process Status
pm2 status

# Port 3001 Check
sudo netstat -tulpn | grep 3001
```

### Nginx Probleme:
```bash
# Config testen
sudo nginx -t

# Logs prüfen
sudo tail -f /var/log/nginx/error.log
sudo tail -f /var/log/nginx/access.log

# Reload
sudo systemctl reload nginx
```

### SSL-Probleme:
```bash
# Zertifikate erneuern
sudo certbot renew --force-renewal

# Nginx neustarten
sudo systemctl restart nginx
```

---

## 📊 Monitoring & Wartung

### Wichtige Befehle:

```bash
# System Status
sudo systemctl status hotelchatbot-api
pm2 status

# Logs live verfolgen
sudo journalctl -u hotelchatbot-api -f
pm2 logs hotelchatbot-mcp --lines 100

# Disk Space
df -h

# Memory Usage
free -h

# CPU Usage
top
```

### Performance Monitoring:
```bash
# PM2 Monitoring Dashboard
pm2 monit

# Nginx Stats
sudo cat /var/log/nginx/access.log | wc -l
```

---

## ✅ Checkliste: Deployment abgeschlossen

- [ ] PostgreSQL läuft auf dev-universe.net mit pgvector
- [ ] .NET SDK 10.0 auf Server installiert
- [ ] Node.js 18+ auf Server installiert
- [ ] Nginx installiert und konfiguriert
- [ ] .NET API läuft als systemd Service auf Port 5001
- [ ] Node.js MCP läuft mit PM2 auf Port 3001
- [ ] Nginx Reverse Proxy konfiguriert (2 Domains)
- [ ] HTTPS aktiviert mit Let's Encrypt
- [ ] Alle Tests erfolgreich (API, MCP, SSE)
- [ ] ChatGPT Custom GPT konfiguriert und getestet
- [ ] Monitoring & Logs funktionieren

---

## 🎉 Fertig!

**Deine URLs:**
- API: `https://api.your-domain.com`
- MCP: `https://chatgpt.your-domain.com`
- ChatGPT: In ChatGPT unter "My GPTs"

**Testen:**
Öffne dein Custom GPT und schreibe:
```
"Zeig mir alle verfügbaren Hotels"
"Ich suche ein Hotel mit Pool und Wellness"
"Was bietet das Hotel Stock an?"
```
