# Hotel Chatbot Plattform

Produktionsreife Chatbot-Plattform für Hotels mit RAG (Retrieval-Augmented Generation).
Antworten basieren ausschließlich auf gecrawlten Hotel-Webseiteninhalten.

## 📋 Architektur

Siehe [architecture.md](architecture.md) für die vollständige Architekturbeschreibung.

### Backend (ASP.NET Core WebAPI)
```
src/
├── HotelChatbot.Domain/          # Domain Entities & Interfaces
├── HotelChatbot.Application/     # Application Services & DTOs
├── HotelChatbot.Infrastructure/  # Implementations (Vector Store, OpenAI, etc.)
└── HotelChatbot.Api/            # Web API Controllers & Middleware
```

### Frontend (Web Component)
```
frontend/
├── src/
│   ├── chatbot.ts               # Entry Point
│   ├── components/              # Web Components
│   ├── services/                # API Communication
│   ├── types/                   # TypeScript Types
│   ├── templates/               # HTML Templates
│   └── styles/                  # CSS Styles
└── dist/                        # Build Output
```

## 🚀 Setup

### Backend

1. **Projekt bauen:**
```bash
cd src
dotnet restore
dotnet build
```

2. **Konfiguration (appsettings.json):**
```json
{
  "OpenAI": {
    "Endpoint": "https://your-openai-endpoint.openai.azure.com/",
    "ApiKey": "your-api-key",
    "DeploymentName": "gpt-4"
  },
  "Qdrant": {
    "Endpoint": "http://localhost:6333",
    "ApiKey": ""
  }
}
```

3. **API starten:**
```bash
cd src/HotelChatbot.Api
dotnet run
```

API läuft auf: `https://localhost:5001`

### Frontend

1. **Dependencies installieren:**
```bash
cd frontend
npm install
```

2. **Build:**
```bash
npm run build
```

Output: `frontend/dist/chatbot.js`

3. **Development:**
```bash
npm run dev  # Watch mode
```

## 🔧 Integration

Fügen Sie das Widget in Ihre Hotel-Webseite ein:

```html
<script 
  src="https://cdn.example.com/chatbot.js"
  data-hotel-id="hotel_123"
  data-api-base="https://api.example.com"
  data-theme="light"
  data-position="bottom-right">
</script>
```

## 🏗️ Technologie-Stack

### Backend
- .NET 10 (LTS)
- ASP.NET Core WebAPI
- Azure OpenAI / OpenAI API (Chat Completion)
- Qdrant Vector Database (Vector Store)
- Clean Architecture Pattern

### Frontend
- TypeScript
- Web Components (Custom Elements)
- Shadow DOM (vollständige Isolation)
- Vanilla JS (kein Framework)
- Rollup (Bundling)

## 🔒 Security Features

- ✅ API-Keys nur serverseitig
- ✅ Domain-Validierung
- ✅ Rate Limiting
- ✅ CORS-Konfiguration
- ✅ Session Management
- ✅ Input Sanitization

## 📦 RAG Pipeline

1. **Crawling**: Hotel-Webseiten werden gecrawlt und in Chunks aufgeteilt
2. **Embedding**: Text-Chunks werden vektorisiert (OpenAI Embeddings)
3. **Storage**: Vektoren werden in Qdrant Vector Store gespeichert
4. **Retrieval**: Bei Anfrage werden ähnliche Chunks gesucht
5. **Context Check**: Prüfung ob Kontext ausreichend ist
6. **Generation**: LLM generiert Antwort basierend auf Kontext

## 🚨 Wichtige Regeln

### VERBOTEN:
- ❌ Antworten ohne belegbaren Kontext
- ❌ Halluzinationen oder Annahmen
- ❌ API-Keys im Frontend
- ❌ Globale CSS/JS-Variablen im Widget
- ❌ Frameworks im Widget

### ERFORDERLICH:
- ✅ Shadow DOM für Widget
- ✅ Retrieval-Daten für jede Antwort
- ✅ Sprache der Antwort = Sprache der Anfrage
- ✅ Fehlerbehandlung und Logging
- ✅ Clean Code & Dokumentation

## 📝 API Endpoints

### Chat
```
POST /api/chat
Body: { sessionId, hotelId, message, isVoiceInput, language }
Response: { sessionId, messageId, response, language, confidenceScore, hasResponse }
```

### Voice (Speech-to-Text)
```
POST /api/voice/transcribe
Body: { hotelId, sessionId, audioDataBase64, audioFormat }
Response: { text, detectedLanguage, confidenceScore }
```

### Voice (Text-to-Speech)
```
POST /api/voice/synthesize
Body: { text, language, voice }
Response: audio/mpeg (Stream)
```

## 🧪 Testing

Backend testen:
```bash
cd src/HotelChatbot.Api
dotnet test
```

Frontend Demo:
```bash
cd frontend
# Demo öffnen in Browser
start demo.html
```

## 📊 Monitoring & Logging

- Alle Conversations werden protokolliert
- Confidence Scores werden getrackt
- Retrieval-Quality Metriken
- API-Performance Monitoring

## 🔄 Deployment

### Backend
```bash
dotnet publish -c Release -o ./publish
# Deploy to Azure App Service, AWS, etc.
```

### Frontend
```bash
npm run build
# Upload dist/chatbot.js to CDN
```

## 📄 Lizenz

Proprietary - Alle Rechte vorbehalten

## 👤 Kontakt

Bei Fragen zur Architektur oder Implementierung siehe [architecture.md](architecture.md).
