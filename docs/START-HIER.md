# 🚀 Quick Start - Hotel Chatbot API starten

## 1. Qdrant starten (Terminal 1)
```powershell
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant:latest
```
✅ Qdrant Dashboard: http://localhost:6333/dashboard

## 2. Backend starten (Terminal 2)
```powershell
cd src\HotelChatbot.Api
dotnet run
```
✅ API: https://localhost:5001
✅ Swagger: https://localhost:5001/swagger

## 3. Swagger öffnen und Crawler starten

### Im Browser öffnen:
https://localhost:5001/swagger

### Crawler-Endpoint finden:
Suche nach: **"🚀 Crawl Multiple Hotels"**
- Controller: **Admin**
- Endpoint: `POST /api/admin/crawl-multiple-hotels`

### "Try it out" klicken

### Request-Body (Beispiel für 2 Hotels):
```json
{
  "urls": [
    "https://www.stock.at/",
    "https://www.post-lermoos.at/"
  ],
  "maxDepth": 2,
  "maxPages": 50
}
```

### Oder alle 15 Hotels auf einmal:
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

### "Execute" klicken

## ⏱️ Dauer
- **2 Hotels:** ~4-5 Minuten
- **15 Hotels:** ~20-30 Minuten

## 📊 Fortschritt überwachen
- **Backend-Terminal:** Zeigt Live-Logs
- **Swagger Response:** Zeigt Ergebnis für jedes Hotel
- **Qdrant Dashboard:** http://localhost:6333/dashboard → Collections → hotel_content

## ✅ Fertig!
Die Hotels sind jetzt im System und können per Chat abgefragt werden!

### Test im Swagger:
1. Suche: `POST /api/chat`
2. Request-Body:
```json
{
  "hotelId": "hotel_stock_at",
  "message": "Welche Zimmer bietet ihr an?",
  "language": "de"
}
```
3. Execute → Du bekommst eine RAG-basierte Antwort! 🎉
