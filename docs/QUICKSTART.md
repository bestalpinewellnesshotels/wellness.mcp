# 🚀 Quick Start Guide

## Voraussetzungen

- .NET 10 SDK
- Node.js 18+ & npm
- Docker Desktop (für Qdrant)
- Azure OpenAI oder OpenAI API Key

## 1. Qdrant Vector Database starten

```bash
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant:latest
```

Qdrant UI: http://localhost:6333/dashboard

## 2. Backend konfigurieren

1. Navigiere zu `src/HotelChatbot.Api/`
2. Erstelle `appsettings.Development.json`:

```json
{
  "OpenAI": {
    "Endpoint": "https://your-instance.openai.azure.com/",
    "ApiKey": "YOUR_API_KEY",
    "DeploymentName": "gpt-4"
  },
  "Qdrant": {
    "Endpoint": "http://localhost:6333",
    "ApiKey": ""
  }
}
```

## 3. Backend starten

```bash
cd src/HotelChatbot.Api
dotnet run
```

API läuft auf: https://localhost:5001
Swagger UI: https://localhost:5001/swagger

## 4. Frontend bauen

```bash
cd frontend
npm install
npm run build
```

Output: `frontend/dist/chatbot.js`

## 5. Demo testen

Öffne `frontend/demo.html` im Browser.

**Wichtig:** Demo-HTML muss über einen Webserver laufen (wegen CORS):

```bash
# Mit Python
cd frontend
python -m http.server 8000

# Mit Node.js (http-server)
npx http-server frontend -p 8000
```

Demo: http://localhost:8000/demo.html

## 6. In deine Webseite integrieren

```html
<script 
  src="path/to/chatbot.js"
  data-hotel-id="hotel_demo"
  data-api-base="https://localhost:5001"
  data-theme="light"
  data-position="bottom-right">
</script>
```

## Test-Hotel anlegen

Für Development gibt es ein In-Memory Hotel mit ID `hotel_demo`.
Für Produktion muss ein Hotel in der Datenbank angelegt werden.

## Content crawlen und indexieren

1. Crawle Hotel-Webseite und erstelle Content-Chunks
2. Generiere Embeddings (OpenAI Embeddings API)
3. Speichere in Qdrant Vector Store

Beispiel-Endpoint (muss noch implementiert werden):
```bash
POST /api/admin/index-content
{
  "hotelId": "hotel_demo",
  "url": "https://example-hotel.com",
  "crawlDepth": 2
}
```

## Troubleshooting

### Backend startet nicht
- Prüfe ob Qdrant läuft: http://localhost:6333/dashboard
- Prüfe appsettings.Development.json
- Prüfe OpenAI API Key

### CORS-Fehler
- Stelle sicher dass Frontend über http-server läuft
- Prüfe CORS-Konfiguration in Program.cs

### Widget erscheint nicht
- Öffne Browser Console (F12)
- Prüfe ob chatbot.js geladen wurde
- Prüfe data-hotel-id und data-api-base Attribute

### Keine Antworten vom Bot
- Prüfe ob Content für Hotel indexiert wurde
- Prüfe Qdrant Collections: http://localhost:6333/dashboard
- Prüfe Backend Logs

## Nächste Schritte

1. ✅ Content Crawler implementieren
2. ✅ Admin-Panel für Hotel-Verwaltung
3. ✅ Produktions-Datenbank (PostgreSQL)
4. ✅ Monitoring & Analytics
5. ✅ Rate Limiting implementieren
6. ✅ Speech-to-Text aktivieren

## Support

Bei Fragen siehe:
- [README.md](README.md) - Vollständige Dokumentation
- [architecture.md](architecture.md) - Architektur-Details
