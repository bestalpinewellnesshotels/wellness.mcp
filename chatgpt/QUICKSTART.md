# 🚀 Quick Start Guide

## Schnellstart in 3 Schritten

### 1. Dependencies installieren
```powershell
npm install
```

### 2. HotelChatbot.Api starten
```powershell
# In einem separaten Terminal
cd ..\src\HotelChatbot.Api
dotnet run
```

### 3. MCP Server starten
```powershell
npm start
```

✅ Server läuft auf: http://localhost:3001

## Erste Tests

### Server-Status prüfen
```powershell
curl http://localhost:3001
```

### Alle Hotels auflisten
```powershell
curl http://localhost:3001/list_all_hotels
```

### Hotels suchen
```powershell
curl -X POST http://localhost:3001/search_hotels `
  -H "Content-Type: application/json" `
  -d '{"query": "Tirol", "limit": 5}'
```

### Frage zu Hotel stellen
```powershell
# Zuerst die Hotel-ID mit search_hotels herausfinden
curl -X POST http://localhost:3001/ask_hotel_question `
  -H "Content-Type: application/json" `
  -d '{
    "hotelId": "hotel_stock",
    "question": "Welche Zimmertypen gibt es?",
    "language": "de"
  }'
```

## ChatGPT Integration

Siehe [README.md](README.md) für die vollständige Anleitung zur ChatGPT Custom GPT Konfiguration.

**SSE Endpoint für ChatGPT:**
```
http://localhost:3001/sse
```

Für Produktiv-Umgebung mit HTTPS und öffentlicher Domain.
