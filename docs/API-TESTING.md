# API Testing - bestewellness-api.dev-universe.net

## 🧪 Test-Commands für PowerShell/Terminal

### 1. Health Check
```powershell
curl https://bestewellness-api.dev-universe.net/api/chat/health
```

### 2. Hotel-Empfehlung testen
```powershell
curl -X POST https://bestewellness-api.dev-universe.net/api/chat/recommend `
  -H "Content-Type: application/json" `
  -d '{
    "requirements": "Hotel mit Pool und Wellness, Adults Only",
    "maxResults": 3,
    "language": "de"
  }'
```

### 3. Alle Hotels auflisten
```powershell
curl https://bestewellness-api.dev-universe.net/api/admin/hotels
```

### 4. Chat mit einem Hotel
```powershell
curl -X POST https://bestewellness-api.dev-universe.net/api/chat `
  -H "Content-Type: application/json" `
  -d '{
    "hotelId": "hotel_stock_at",
    "message": "Welche Zimmertypen gibt es?",
    "language": "de"
  }'
```

### 5. Root Endpoint (API-Info)
```powershell
curl https://bestewellness-api.dev-universe.net/
```

---

## 🌐 Tools zum Testen

### Option 1: PowerShell (Invoke-RestMethod)
```powershell
# Health Check
Invoke-RestMethod -Uri "https://bestewellness-api.dev-universe.net/api/chat/health"

# Hotel-Empfehlung
$body = @{
    requirements = "Hotel mit Pool und Wellness"
    maxResults = 3
    language = "de"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "https://bestewellness-api.dev-universe.net/api/chat/recommend" -Body $body -ContentType "application/json"
```

### Option 2: Browser
Öffne direkt im Browser:
- https://bestewellness-api.dev-universe.net/
- https://bestewellness-api.dev-universe.net/api/chat/health
- https://bestewellness-api.dev-universe.net/api/admin/hotels

### Option 3: Postman
1. Öffne Postman
2. Neue Collection erstellen
3. Requests importieren (siehe unten)

### Option 4: VS Code REST Client
Erstelle `test.http`:

```http
### Health Check
GET https://bestewellness-api.dev-universe.net/api/chat/health

### Hotel-Empfehlung
POST https://bestewellness-api.dev-universe.net/api/chat/recommend
Content-Type: application/json

{
  "requirements": "Hotel mit Pool und Wellness, Adults Only",
  "maxResults": 3,
  "language": "de"
}

### Chat
POST https://bestewellness-api.dev-universe.net/api/chat
Content-Type: application/json

{
  "hotelId": "hotel_stock_at",
  "message": "Welche Zimmertypen gibt es?",
  "language": "de"
}

### Alle Hotels
GET https://bestewellness-api.dev-universe.net/api/admin/hotels
```

---

## 📋 Postman Collection (Import)

```json
{
  "info": {
    "name": "HotelChatbot API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Health Check",
      "request": {
        "method": "GET",
        "header": [],
        "url": "https://bestewellness-api.dev-universe.net/api/chat/health"
      }
    },
    {
      "name": "Recommend Hotels",
      "request": {
        "method": "POST",
        "header": [{"key": "Content-Type", "value": "application/json"}],
        "body": {
          "mode": "raw",
          "raw": "{\n  \"requirements\": \"Hotel mit Pool und Wellness\",\n  \"maxResults\": 3,\n  \"language\": \"de\"\n}"
        },
        "url": "https://bestewellness-api.dev-universe.net/api/chat/recommend"
      }
    },
    {
      "name": "Chat with Hotel",
      "request": {
        "method": "POST",
        "header": [{"key": "Content-Type", "value": "application/json"}],
        "body": {
          "mode": "raw",
          "raw": "{\n  \"hotelId\": \"hotel_stock_at\",\n  \"message\": \"Welche Zimmertypen gibt es?\",\n  \"language\": \"de\"\n}"
        },
        "url": "https://bestewellness-api.dev-universe.net/api/chat"
      }
    },
    {
      "name": "List All Hotels",
      "request": {
        "method": "GET",
        "header": [],
        "url": "https://bestewellness-api.dev-universe.net/api/admin/hotels"
      }
    }
  ]
}
```

---

## 🎯 Schnelltest-Script

Speichere als `test-api.ps1`:

```powershell
$baseUrl = "https://bestewellness-api.dev-universe.net"

Write-Host "Testing HotelChatbot API..." -ForegroundColor Green

# 1. Health Check
Write-Host "`n1. Health Check..." -ForegroundColor Cyan
try {
    $health = Invoke-RestMethod "$baseUrl/api/chat/health"
    Write-Host "✓ API is healthy: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "✗ Health check failed: $_" -ForegroundColor Red
}

# 2. List Hotels
Write-Host "`n2. List Hotels..." -ForegroundColor Cyan
try {
    $hotels = Invoke-RestMethod "$baseUrl/api/admin/hotels"
    Write-Host "✓ Found $($hotels.Count) hotels" -ForegroundColor Green
    $hotels | Select-Object hotelId, name | Format-Table
} catch {
    Write-Host "✗ List hotels failed: $_" -ForegroundColor Red
}

# 3. Recommend Hotels
Write-Host "`n3. Test Recommendation..." -ForegroundColor Cyan
try {
    $body = @{
        requirements = "Hotel mit Pool und Wellness"
        maxResults = 2
        language = "de"
    } | ConvertTo-Json
    
    $result = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/chat/recommend" -Body $body -ContentType "application/json"
    Write-Host "✓ Recommendation successful" -ForegroundColor Green
    Write-Host "  Summary: $($result.summary)" -ForegroundColor Gray
} catch {
    Write-Host "✗ Recommendation failed: $_" -ForegroundColor Red
}

Write-Host "`nTests completed!" -ForegroundColor Green
```

Ausführen:
```powershell
.\test-api.ps1
```
