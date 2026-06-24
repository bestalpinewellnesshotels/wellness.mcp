# HotelChatbot MCP Server

MCP (Model Context Protocol) Server für ChatGPT Custom GPT Integration.
Ermöglicht ChatGPT den Zugriff auf die HotelChatbot-Datenbank mit RAG-basierter Hotelsuche und -beratung.

## 📋 Übersicht

Dieser MCP-Server fungiert als Brücke zwischen ChatGPT und der HotelChatbot.Api:

```
ChatGPT ← SSE → MCP Server (Node.js) ← HTTP → HotelChatbot.Api (.NET)
                 Port 3001                      Port 5001
```

## 🚀 Setup

### 1. Dependencies installieren

```bash
cd chatgpt
npm install
```

### 2. Umgebungsvariablen konfigurieren

Kopiere `.env.example` zu `.env`:

```bash
cp .env.example .env
```

Bearbeite `.env`:

```env
PORT=3001
API_BASE_URL=https://localhost:5001
# API_KEY=your_secret_key  # Optional
```

### 3. HotelChatbot.Api starten

Der MCP-Server benötigt eine laufende HotelChatbot.Api:

```bash
cd ../src/HotelChatbot.Api
dotnet run
```

API läuft auf: `https://localhost:5001`

### 4. MCP Server starten

```bash
npm start
```

Server läuft auf: `http://localhost:3001`

SSE Endpoint: `http://localhost:3001/sse`

### 5. Deployment (Produktiv)

**Produktiv-URL:** siehe [PRODUCTION.md](PRODUCTION.md)

| | URL |
|--|-----|
| MCP SSE (ChatGPT Connector) | `https://mcp.bestalpine2.ms.mynet.at/sse` |
| Health | `https://mcp.bestalpine2.ms.mynet.at/health` |

Deploy vom Projektroot:

```powershell
.\DEPLOY-PRODUCTION.ps1
```

Details: `docs/BESTALPINE-DEPLOYMENT.md`

Lokal/Entwicklung:

4. **.env anpassen**:
   ```env
   PORT=3001
   API_BASE_URL=https://your-api-domain.com
   API_KEY=production_secret_key
   ```

## 🔧 ChatGPT Custom GPT Konfiguration

### 1. Custom GPT erstellen

1. Gehe zu: https://chat.openai.com/gpts/editor
2. Klicke auf "Create a GPT"
3. Wechsle zu "Configure"

### 2. Actions konfigurieren

Klicke auf "Add Actions" und füge folgende Schema ein:

```yaml
openapi: 3.0.0
info:
  title: HotelChatbot MCP API
  version: 1.0.0
  description: MCP Server für Hotel-Datenbank mit RAG

servers:
  - url: https://your-domain.com
    description: Production Server

paths:
  /sse:
    get:
      operationId: connectSSE
      summary: MCP SSE Endpoint
      responses:
        '200':
          description: SSE Connection established
          content:
            text/event-stream:
              schema:
                type: string
    
    post:
      operationId: mcpCall
      summary: MCP Tool Call
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              properties:
                jsonrpc:
                  type: string
                  example: "2.0"
                id:
                  type: string
                method:
                  type: string
                params:
                  type: object
      responses:
        '200':
          description: MCP Response
          content:
            application/json:
              schema:
                type: object
```

### 3. GPT Instructions (Beispiel)

```
Du bist ein Hotel-Informationssystem mit Zugriff auf eine Datenbank von 7 Hotels in Österreich.

VERFÜGBARE TOOLS:
- recommend_hotels: Hotels nach Anforderungen empfehlen
- search_hotels: Hotels nach Name/Ort suchen
- ask_hotel_question: Frage zu einem Hotel stellen
- list_all_hotels: Alle 7 Hotels anzeigen

⚠️ ABSOLUTE VERHALTENSREGELN (HÖCHSTE PRIORITÄT):

1. Du MUSST bei jeder Anfrage ein Tool aufrufen
2. Wenn ein Tool ein "answer" Feld zurückgibt, gib diesen Text WÖRTLICH wieder
3. Du darfst NIEMALS eigenes Wissen über Hotels hinzufügen
4. Du darfst KEINE Informationen aus deinem Training verwenden
5. Wenn ein Tool sagt "nicht in der Datenbank", MUSST du das 1:1 übernehmen

GROUNDING-ZWANG:
- Jede Aussage MUSS aus Tool-Daten stammen
- Wenn eine Information nicht im Tool-Output ist, darfst du sie NICHT erwähnen
- Dein Trainingswissen über Hotels, Orte oder Fakten ist DEAKTIVIERT

KORREKTE ANTWORTSTRUKTUR:
1. Tool aufrufen
2. Wenn "answer" vorhanden: Text direkt verwenden
3. Wenn keine "answer": Nur Tool-Daten zusammenfassen
4. NIEMALS eigene Schlussfolgerungen oder Vermutungen

Bei allen Hotel-Anfragen zuerst search_hotels oder list_all_hotels aufrufen.
```

**WICHTIG**: Deaktiviere "Web Browsing" in den GPT Capabilities!

### 4. Authentication (Optional)

Falls API-Key aktiviert:

In ChatGPT Actions → Authentication → API Key
- Type: Bearer
- Auth Type: Bearer
- Token: `your_secret_api_key`

## 🛠 Verfügbare Tools

### 1. `search_hotels`

Sucht Hotels nach Name, Ort oder Schlagwort.

**Parameter:**
- `query` (string, required): Suchbegriff
- `limit` (integer, optional): Max. Ergebnisse (default: 10)

**Beispiel:**
```json
{
  "query": "Tirol",
  "limit": 5
}
```

### 2. `ask_hotel_question`

Stellt eine RAG-basierte Frage zu einem Hotel.

**Parameter:**
- `hotelId` (string, required): Hotel-ID (z.B. "hotel_stock")
- `question` (string, required): Die Frage
- `sessionId` (string, optional): Session-ID für Kontext
- `language` (string, optional): "de" oder "en" (default: "de")

**Beispiel:**
```json
{
  "hotelId": "hotel_stock",
  "question": "Welche Zimmertypen gibt es und was kosten sie?",
  "language": "de"
}
```

### 3. `list_all_hotels`

Gibt alle verfügbaren Hotels zurück.

**Parameter:** Keine

**Beispiel:**
```json
{}
```

## 🧪 Testen

### SSE Endpoint testen

```bash
curl -N http://localhost:3001/sse
```

### MCP Initialize testen

```bash
curl -X POST http://localhost:3001/sse \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "1",
    "method": "initialize"
  }'
```

### MCP Tools List testen

```bash
curl -X POST http://localhost:3001/sse \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "2",
    "method": "tools/list"
  }'
```

### Tool Call testen

```bash
curl -X POST http://localhost:3001/sse \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "3",
    "method": "tools/call",
    "params": {
      "name": "list_all_hotels",
      "arguments": {}
    }
  }'
```

### REST Endpoints (Alternative)

```bash
# Alle Hotels auflisten
curl http://localhost:3001/list_all_hotels

# Hotels suchen
curl -X POST http://localhost:3001/search_hotels \
  -H "Content-Type: application/json" \
  -d '{"query": "Tirol", "limit": 5}'

# Frage zu Hotel stellen
curl -X POST http://localhost:3001/ask_hotel_question \
  -H "Content-Type: application/json" \
  -d '{
    "hotelId": "hotel_stock",
    "question": "Welche Wellness-Angebote gibt es?",
    "language": "de"
  }'
```

## 📊 API-Anforderungen

Der MCP-Server benötigt folgende Endpoints in der HotelChatbot.Api:

### Erforderlich:
- `POST /api/chat` - Chat mit RAG (✅ vorhanden)

### Optional (falls vorhanden):
- `GET /api/admin/hotels` - Alle Hotels auflisten
- `GET /api/admin/hotels/search?query=...` - Hotels suchen

> **Hinweis:** Falls die Admin-Endpoints nicht existieren, nutzt der MCP-Server ein Fallback-Verhalten.

## 🔐 Sicherheit

### Produktion-Checklist:

- [ ] API-Key aktivieren und sicheres Secret verwenden
- [ ] CORS auf spezifische Origins beschränken
- [ ] HTTPS verwenden (nginx Reverse Proxy)
- [ ] Rate Limiting implementieren (z.B. express-rate-limit)
- [ ] Logging implementieren (z.B. winston)
- [ ] Environment-Variablen sicher verwalten

### API-Key aktivieren:

In `index.js` auskommentieren:

```javascript
const API_KEY = process.env.API_KEY;
app.use((req, res, next) => {
  const authHeader = req.headers.authorization;
  if (!authHeader || authHeader !== `Bearer ${API_KEY}`) {
    return res.status(401).json({ error: "Unauthorized" });
  }
  next();
});
```

## 📝 Entwicklung

### Watch Mode (Auto-Reload):

```bash
npm run dev
```

### Logs überwachen:

```bash
# Bei Verwendung von PM2
pm2 logs hotelchatbot-mcp
```

## 🐛 Troubleshooting

### "Konnte keine Verbindung zur Hotel-API herstellen"

- Prüfe ob HotelChatbot.Api läuft: `https://localhost:5001`
- Prüfe `.env` → `API_BASE_URL`
- Prüfe SSL-Zertifikat (bei HTTPS)

### "Hotels werden nicht gefunden"

- Stelle sicher, dass Hotels in der Datenbank sind
- Teste direkt: `curl https://localhost:5001/api/admin/hotels`
- Prüfe PostgreSQL-Verbindung

### "SSE Connection Failed"

- Firewall-Regeln prüfen
- Nginx-Konfiguration prüfen (buffering deaktivieren)
- Browser-Kompatibilität prüfen

## 📚 Weitere Ressourcen

- [MCP Specification](https://spec.modelcontextprotocol.io/)
- [OpenAI Custom GPTs Documentation](https://platform.openai.com/docs/guides/gpts)
- [HotelChatbot.Api Documentation](../README.md)

## 🤝 Support

Bei Fragen oder Problemen siehe die Hauptdokumentation oder öffne ein Issue.

---

**Version:** 1.0.0  
**Status:** Production Ready ✅
