# Manuelle Anleitung - Hotel Websites Crawlen und Indexieren

## Voraussetzungen
- Docker Desktop installiert und gestartet
- .NET 10 SDK installiert

## Schritte

### 1. Qdrant Vector Database starten
```powershell
docker run -d -p 6333:6333 -p 6334:6334 --name qdrant_chatbot qdrant/qdrant:latest
```

Qdrant Dashboard: http://localhost:6333/dashboard

### 2. Backend API starten
```powershell
cd src\HotelChatbot.Api
dotnet run
```

API läuft auf: https://localhost:5001
Swagger UI: https://localhost:5001/swagger

### 3. Hotels crawlen und indexieren

**Option A: Via Swagger UI**
1. Öffne https://localhost:5001/swagger
2. Navigiere zu `POST /api/admin/crawl-multiple-hotels`
3. Click "Try it out"
4. Füge folgenden JSON ein:

```json
{
  "urls": [
    "https://www.stock.at/",
    "https://www.post-lermoos.at/",
    "https://www.nesslerhof.at/",
    "https://www.alpbacherhof.at/",
    "https://www.waldklause.at/",
    "https://www.engel-tirol.com/",
    "https://www.hochschober.com/",
    "https://www.uebergossenealm.at/",
    "https://www.alpenrose.at/",
    "https://www.wartherhof.at/",
    "https://www.krallerhof.at/",
    "https://www.theresa.at/",
    "https://www.gmachl.at/",
    "https://www.alpenresort-schwarz.at/",
    "https://seefeld.sacher.com/"
  ],
  "maxDepth": 2,
  "maxPages": 50
}
```

5. Click "Execute"

**Option B: Via PowerShell**
```powershell
$json = Get-Content hotel-urls.json -Raw
Invoke-RestMethod -Uri "https://localhost:5001/api/admin/crawl-multiple-hotels" -Method Post -Body $json -ContentType "application/json" -SkipCertificateCheck
```

**Option C: Automatisches Script**
```powershell
.\start-crawling.ps1
```

## Was passiert?

1. ✅ Crawlt alle 15 Hotel-Websites
2. ✅ Extrahiert relevanten Content (entfernt Navigation, Footer, etc.)
3. ✅ Generiert Embeddings mit Azure OpenAI
4. ✅ Speichert in Qdrant Vector Database
5. ✅ Erstellt automatisch Hotels mit IDs wie `hotel_stock_at`

## Dauer
- Pro Website: ~1-2 Minuten (je nach Anzahl Seiten)
- Gesamt für 15 Hotels: **15-30 Minuten**

## Fortschritt überwachen
- Backend-Logs im Terminal
- Qdrant Dashboard: http://localhost:6333/dashboard → Collections → hotel_content

## Nach dem Crawling
Die Hotels sind sofort über den Chatbot erreichbar!

Test: POST /api/chat
```json
{
  "hotelId": "hotel_stock_at",
  "message": "Welche Zimmer habt ihr?"
}
```
