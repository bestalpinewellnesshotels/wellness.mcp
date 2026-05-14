# ARCHITECTURE.md

## Zweck
Dieses Dokument beschreibt die verbindliche Architektur der
Chatbot-Plattform. Alle Implementierungen müssen dieser Struktur folgen.

---

## 1. Gesamtarchitektur

Hotel-Webseite
  └─ <script src="chatbot.js" data-hotel-id="...">

Chatbot Widget (Web Component, Shadow DOM)
  └─ REST / WebSocket

ASP.NET Core WebAPI
  ├─ Chat API
  ├─ Speech API
  ├─ RAG Engine
  ├─ Vector Store
  └─ Summary & Logging

---

## 2. Frontend (Widget)

### Technologie
- Vanilla TypeScript
- Web Components (Custom Elements)
- Shadow DOM zwingend
- Ein einzelnes distributables Bundle (chatbot.js)

### Verantwortlichkeiten
- UI-Rendering
- User-Interaktion (Text & Voice)
- Session-Verwaltung im Browser
- Keine Business-Logik
- Keine Wissensverarbeitung

### Beispiel-Einbindung
```html
<script
  src="https://cdn.example.com/chatbot.js"
  data-hotel-id="hotel_123"
  data-api-base="https://api.example.com"
  data-theme="light"
  data-position="bottom-right">
</script>
```

### Projektstruktur
```
frontend/
├── src/
│   ├── chatbot.ts                 # Entry Point & Auto-Init
│   ├── components/
│   │   └── ChatbotWidget.ts       # Haupt Web Component
│   ├── services/
│   │   ├── ChatService.ts         # REST API Kommunikation
│   │   └── VoiceService.ts        # Speech-to-Text via Backend
│   ├── types/
│   │   └── ChatMessage.ts         # TypeScript Interfaces
│   ├── templates/
│   │   └── widget-template.ts     # HTML Templates
│   └── styles/
│       └── widget-styles.ts       # CSS (Shadow DOM)
├── dist/                          # Build Output
│   └── chatbot.js                 # Bundle für Distribution
├── package.json
├── tsconfig.json
└── rollup.config.js               # Build Configuration
```

---

## 3. Backend (ASP.NET Core)

### Technologie
- ASP.NET Core WebAPI (.NET 10 LTS)
- Clean Architecture mit strikter Schichtentrennung
- Azure OpenAI / OpenAI für Chat Completion
- Qdrant für Vector Store
- Entity Framework Core (optional für Persistierung)

### Schichtenarchitektur

```
HotelChatbot.Domain/
├── Entities/
│   ├── Hotel.cs                   # Hotel-Konfiguration
│   ├── ChatSession.cs             # Session-Verwaltung
│   ├── ChatMessage.cs             # Einzelne Nachricht
│   └── ContentChunk.cs            # Gecrawlter Content
├── Interfaces/
│   ├── IVectorStore.cs            # Vector Store Interface
│   ├── IChatCompletionService.cs  # LLM Interface
│   ├── ISpeechToTextService.cs    # STT Interface
│   ├── ITextToSpeechService.cs    # TTS Interface
│   ├── IHotelRepository.cs        # Hotel Repository
│   └── IChatSessionRepository.cs  # Session Repository
└── Enums/
    └── MessageRole.cs             # user, assistant, system

HotelChatbot.Application/
├── Services/
│   ├── ChatService.cs             # RAG Pipeline Logic
│   └── VoiceService.cs            # Voice Processing
└── DTOs/
    ├── ChatRequestDto.cs
    ├── ChatResponseDto.cs
    ├── VoiceToTextRequestDto.cs
    ├── VoiceToTextResponseDto.cs
    └── TextToVoiceRequestDto.cs

HotelChatbot.Infrastructure/
├── Services/
│   ├── OpenAIChatCompletionService.cs  # OpenAI Implementation
│   ├── SpeechToTextService.cs          # Azure Speech STT
│   └── TextToSpeechService.cs          # Azure Speech TTS
├── VectorStore/
│   ├── QdrantVectorStore.cs            # Qdrant Implementation
│   └── InMemoryVectorStore.cs          # In-Memory für Dev
└── Repositories/
    ├── InMemoryHotelRepository.cs
    └── InMemoryChatSessionRepository.cs

HotelChatbot.Api/
├── Controllers/
│   ├── ChatController.cs          # Chat Endpoints
│   └── VoiceController.cs         # Voice Endpoints
├── Middleware/
│   ├── RateLimitMiddleware.cs     # Rate Limiting
│   └── ErrorHandlingMiddleware.cs # Global Error Handler
├── Program.cs                     # DI Configuration
└── appsettings.json               # Configuration
```

---

## 4. RAG Pipeline

### Workflow
1. **User Query** → Frontend sendet an `/api/chat`
2. **Hotel Validation** → Prüfe Hotel-ID und Aktivstatus
3. **Session Management** → Erstelle/Lade Chat-Session
4. **Vector Retrieval** → Suche ähnliche Content-Chunks (Qdrant)
5. **Context Check** → Prüfe ob Kontext ausreichend ist
6. **LLM Generation** → Generiere Antwort mit OpenAI
7. **Response** → Sende Antwort an Frontend + Logging

### Wichtige Regeln
- ❌ **KEINE Antwort ohne Retrieval-Kontext**
- ❌ **KEINE Halluzinationen oder Annahmen**
- ✅ Confidence Score < 0.7 → keine Antwort
- ✅ Leerer Retrieval → "Keine Informationen verfügbar"
- ✅ Sprache Antwort = Sprache Anfrage

---

## 5. API Endpoints

### Chat
```
POST /api/chat
Content-Type: application/json

Request:
{
  "sessionId": "optional-session-id",
  "hotelId": "hotel_123",
  "message": "Wie ist die Adresse des Hotels?",
  "isVoiceInput": false,
  "language": "de"
}

Response:
{
  "sessionId": "abc-123-def",
  "messageId": "msg-456",
  "response": "Das Hotel befindet sich...",
  "language": "de",
  "confidenceScore": 0.85,
  "hasResponse": true,
  "timestamp": "2026-01-15T10:30:00Z"
}
```

### Voice (STT)
```
POST /api/voice/transcribe
Content-Type: application/json

Request:
{
  "hotelId": "hotel_123",
  "sessionId": "abc-123-def",
  "audioDataBase64": "base64-encoded-audio",
  "audioFormat": "audio/webm",
  "language": "de"
}

Response:
{
  "text": "Transkribierter Text",
  "detectedLanguage": "de",
  "confidenceScore": 0.92
}
```

### Voice (TTS)
```
POST /api/voice/synthesize
Content-Type: application/json

Request:
{
  "text": "Hallo, wie kann ich helfen?",
  "language": "de-DE",
  "voice": "de-DE-ConradNeural"
}

Response:
Content-Type: audio/mpeg
(Audio Stream)
```

---

## 6. Security

### API-Keys
- ✅ Alle API-Keys **NUR** serverseitig
- ✅ appsettings.json **NICHT** in Repository
- ✅ Secrets via Azure Key Vault / Environment Variables

### CORS
- ✅ Konfigurierbar pro Hotel (AllowedDomains)
- ✅ Strict Origin-Checking

### Rate Limiting
- ✅ Pro Session: 60 Requests/Minute
- ✅ Pro IP: 1000 Requests/Stunde
- ✅ Configurable in appsettings.json

### Input Validation
- ✅ Hotel-ID Validierung
- ✅ Message Length Limits
- ✅ Sanitization von User-Input

---

## 7. Configuration

### appsettings.json
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
  },
  "RateLimit": {
    "RequestsPerMinute": 60,
    "RequestsPerHour": 1000
  },
  "Chat": {
    "MinConfidenceScore": 0.7,
    "MaxRetrievalResults": 5,
    "MaxConversationHistory": 10
  }
}
```

---

## 8. Deployment

### Backend
```bash
dotnet publish -c Release -o ./publish
# Deploy to Azure App Service, AWS ECS, Docker, etc.
```

### Frontend
```bash
npm run build
# Upload dist/chatbot.js to CDN (CloudFlare, Azure CDN, etc.)
```

### Infrastructure Requirements
- ASP.NET Core Runtime 10.0
- Qdrant Vector Database (Docker: qdrant/qdrant)
- Azure OpenAI / OpenAI API Access

---

## 9. Monitoring & Logging

### Metrics
- Request Count
- Response Time
- Confidence Scores
- Retrieval Quality
- Error Rate

### Logging
- All Conversations (Chat History)
- User Queries & Responses
- Retrieval Results
- API Errors
- Rate Limit Violations

---

## 10. VERBOTEN

- ❌ SPA-Architektur für Widget
- ❌ React, Vue, Angular im Widget
- ❌ Globale CSS ohne Shadow DOM
- ❌ Globale JavaScript-Variablen
- ❌ API-Keys im Frontend
- ❌ Direkter Zugriff vom Frontend auf OpenAI/Qdrant
- ❌ Antworten ohne Retrieval-Kontext
- ❌ Halluzinationen oder Annahmen
- ❌ Hardcodierte Secrets
- ❌ Vermischung von Schichten

---

## 11. ERFORDERLICH

- ✅ Web Component mit Shadow DOM
- ✅ Vanilla TypeScript (Frontend)
- ✅ Clean Architecture (Backend)
- ✅ RAG mit Retrieval-Prüfung
- ✅ Session Management
- ✅ Mehrsprachigkeit (Auto-Detection)
- ✅ Fehlerbehandlung
- ✅ Rate Limiting
- ✅ Logging & Monitoring
- ✅ Produktionsreifer Code
- ✅ Dokumentation

---

**Ende der Architektur-Spezifikation**
