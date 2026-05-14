# 🎭 Playwright Browser Installation

## Übersicht

Der `PlaywrightCrawlerService` verwendet Chromium im Headless-Modus, um JavaScript-intensive Websites (wie SPAs) zu crawlen. Die Browser-Binaries müssen separat installiert werden.

---

## 🖥️ Lokale Installation (Windows)

### Nach dem ersten Build:

```powershell
# Im Projekt-Root ausführen:
cd C:\Users\gerha\Documents\Programmierung\Bwha\wellness.mcp

# Nur Chromium installieren (empfohlen):
pwsh src/HotelChatbot.Api/bin/Debug/net8.0/playwright.ps1 install chromium

# Oder alle Browser (nicht nötig):
pwsh src/HotelChatbot.Api/bin/Debug/net8.0/playwright.ps1 install
```

### Browser-Speicherort (Windows):
```
C:\Users\{Username}\AppData\Local\ms-playwright\
├── chromium-1161\
│   └── chrome-win\
│       └── headless_shell.exe
```

---

## 🐧 Linux Server Installation

### Variante 1: Mit .NET (empfohlen)

Nach dem Deployment der Anwendung:

```bash
cd /var/www/hotelchatbot

# Playwright-Skript ausführen
pwsh bin/Release/net8.0/playwright.ps1 install chromium --with-deps

# Falls PowerShell nicht verfügbar:
dotnet tool install --global PowerShell
export PATH="$PATH:$HOME/.dotnet/tools"
pwsh bin/Release/net8.0/playwright.ps1 install chromium --with-deps
```

### Variante 2: Manuelle Installation der Dependencies

Falls das Playwright-Skript nicht funktioniert:

```bash
# System-Dependencies installieren
sudo apt-get update
sudo apt-get install -y \
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libdbus-1-3 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libasound2 \
    libpango-1.0-0 \
    libcairo2

# Chromium-Browser manuell herunterladen
export PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright
mkdir -p $PLAYWRIGHT_BROWSERS_PATH

# Browser-Binary herunterladen (Version muss mit NuGet-Package übereinstimmen)
wget https://playwright.azureedge.net/builds/chromium/1161/chromium-linux.zip
unzip chromium-linux.zip -d $PLAYWRIGHT_BROWSERS_PATH/chromium-1161/
rm chromium-linux.zip

# Umgebungsvariable setzen
export PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright
```

### Browser-Speicherort (Linux):
```
/home/{user}/.cache/ms-playwright/  # Standard
# oder
/opt/ms-playwright/                  # Custom-Installation
```

---

## 🐳 Docker Installation

Das **Dockerfile** ist bereits aktualisiert und installiert Playwright automatisch:

```dockerfile
# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final

# Playwright dependencies installieren
RUN apt-get update && apt-get install -y \
    libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 \
    libcups2 libdrm2 libdbus-1-3 libxkbcommon0 \
    libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
    libgbm1 libasound2 libpango-1.0-0 libcairo2 \
    && rm -rf /var/lib/apt/lists/*

# Chromium installieren
RUN PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    dotnet exec HotelChatbot.Api.dll playwright.ps1 install chromium --with-deps
```

### Docker-Build testen:

```bash
# Image bauen
docker build -t hotelchatbot-api .

# Container starten
docker run -p 8080:8080 hotelchatbot-api

# Logs prüfen
docker logs <container-id>
```

---

## ✅ Verifizierung

### Nach der Installation prüfen:

**Windows:**
```powershell
# Browser-Pfad prüfen
dir "$env:LOCALAPPDATA\ms-playwright\chromium-1161\chrome-win\headless_shell.exe"
```

**Linux:**
```bash
# Browser-Pfad prüfen
ls -la ~/.cache/ms-playwright/chromium-1161/chrome-linux/chrome
# oder
ls -la /opt/ms-playwright/chromium-1161/chrome-linux/chrome
```

### Anwendung testen:

1. Anwendung starten
2. Admin-Interface öffnen: `http://localhost:5000/admin/prompts.html`
3. Zum "Crawling"-Tab wechseln
4. Hotel mit JavaScript-Website auswählen (z.B. seefeld.sacher.com)
5. "Ausgewählte crawlen" klicken

**Erwartete Logs:**
```
warn: 🚀 STARTE CRAWLING FÜR https://seefeld.sacher.com/
info: Playwright Chromium gestartet
info: Playwright gecrawlt: https://seefeld.sacher.com/ (1/45)
```

---

## 🚨 Fehlerbehebung

### Fehler: "Executable doesn't exist"

**Problem:** Browser-Binaries wurden nicht installiert.

**Lösung:**
```bash
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
```

### Fehler: "Protocol error (Target.setAutoAttach)"

**Problem:** Veraltete Chromium-Version oder fehlende Dependencies.

**Lösung:**
```bash
# Browser neu installieren
rm -rf ~/.cache/ms-playwright
pwsh bin/Debug/net8.0/playwright.ps1 install chromium --with-deps
```

### Fehler: "Failed to launch browser"

**Problem:** Fehlende System-Libraries (Linux).

**Lösung:**
```bash
# Alle Dependencies installieren
sudo apt-get install -y $(cat /tmp/playwright-deps.txt)

# Oder manuell:
sudo apt-get install -y \
    libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 \
    libcups2 libdrm2 libxcomposite1 libxdamage1 libgbm1
```

### Docker: "PLAYWRIGHT_BROWSERS_PATH not set"

**Problem:** Umgebungsvariable fehlt.

**Lösung:**
```dockerfile
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
```

---

## 📊 Performance-Hinweise

### Nur Chromium installieren:
```bash
# ✅ Empfohlen (ca. 150 MB)
playwright.ps1 install chromium

# ❌ Nicht nötig (ca. 600 MB)
playwright.ps1 install  # Installiert alle Browser
```

### Headless-Modus ist Standard:
```csharp
_browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true,  // ✅ Kein UI, schneller
    Args = new[] { "--disable-dev-shm-usage", "--no-sandbox" }
});
```

### Docker-Image-Größe optimieren:
```dockerfile
# Nach Playwright-Installation aufräumen
RUN apt-get autoremove -y && rm -rf /var/lib/apt/lists/*
```

---

## 📚 Weitere Informationen

- [Playwright .NET Documentation](https://playwright.dev/dotnet/)
- [Browser Installation](https://playwright.dev/dotnet/docs/browsers)
- [Docker Best Practices](https://playwright.dev/dotnet/docs/docker)
