# Hotel-Verwaltung mit PostgreSQL

## Übersicht

Die Hotels werden in der **PostgreSQL-Datenbank auf dev-universe.net** gespeichert. Die Anwendung verwendet automatisch das `PostgreSQLHotelRepository` für alle Hotel-Operationen.

## Datenbank-Schema

```sql
CREATE TABLE hotels (
    hotel_id TEXT PRIMARY KEY,              -- z.B. "hotel_stock_at"
    name TEXT NOT NULL,                     -- z.B. "stock"
    domain TEXT NOT NULL UNIQUE,            -- z.B. "www.stock.at"
    allowed_domains JSONB NOT NULL,         -- z.B. ["www.stock.at", "stock.at"]
    api_key TEXT NOT NULL,                  -- Eindeutiger API-Key
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);
```

## Automatische Hotel-Erstellung

Beim Crawling werden Hotels **automatisch angelegt**, falls sie noch nicht existieren:

```csharp
// In AdminController.cs, Methode CrawlMultipleHotels
var hotel = await _hotelRepository.GetByIdAsync(hotelId, cancellationToken);
if (hotel == null)
{
    hotel = new Hotel
    {
        HotelId = hotelId,
        Name = uri.Host.Replace("www.", "").Replace(".at", "").Replace(".com", ""),
        Domain = uri.Host,
        AllowedDomains = new List<string> { uri.Host, $"www.{uri.Host}" },
        ApiKey = Guid.NewGuid().ToString(),
        IsActive = true
    };
    await _hotelRepository.AddAsync(hotel);
}
```

## Hotels manuell importieren

Falls du die Hotels aus `hotel-urls.json` vorab in die Datenbank importieren möchtest:

```powershell
.\import-hotels-to-db.ps1
```

Dieses Script:
- Liest alle URLs aus `hotel-urls.json`
- Erstellt für jede URL einen Hotel-Eintrag
- Speichert die Hotels in PostgreSQL
- Generiert automatisch API-Keys

## Hotels crawlen

```powershell
.\start-crawling.ps1
```

Dieses Script:
1. Startet die Backend-API
2. Crawlt alle Websites aus `hotel-urls.json`
3. Legt Hotels automatisch an (falls noch nicht vorhanden)
4. Indexiert den Content in PostgreSQL

## Verwaltung über API

### Hotel abrufen

```http
GET /api/admin/hotels/{hotelId}
```

### Alle Hotels abrufen

```http
GET /api/admin/hotels
```

### Hotel anlegen/aktualisieren

```http
POST /api/admin/hotels
Content-Type: application/json

{
  "hotelId": "hotel_example_at",
  "name": "Example Hotel",
  "domain": "www.example.at",
  "allowedDomains": ["www.example.at", "example.at"],
  "apiKey": "your-api-key",
  "isActive": true
}
```

## Datenbank-Zugriff

**Server:** dev-universe.net:5432  
**Datenbank:** Bwchat  
**Benutzer:** bwchatuser  

Connection String (in appsettings):
```
Host=dev-universe.net;Port=5432;Database=Bwchat;Username=bwchatuser;Password=***;SSL Mode=Prefer;Trust Server Certificate=true
```

## Konfiguration

Die Repository-Implementierung wird in `Program.cs` registriert:

```csharp
// PostgreSQL wird verwendet (persistent auf IONOS-Server)
builder.Services.AddSingleton<IHotelRepository, PostgreSQLHotelRepository>();
```

## Hotels aus hotel-urls.json

Aktuell sind folgende Hotels konfiguriert:

1. stock.at
2. post-lermoos.at
3. nesslerhof.at
4. alpbacherhof.at
5. waldklause.at
6. engel-tirol.com
7. hochschober.com
8. uebergossenealm.at
9. alpenrose-cocoon.at
10. wartherhof.at
11. krallerhof.at
12. theresa.at
13. gmachl.at
14. alpenresort-schwarz.at
15. seefeld.sacher.com

Alle werden beim ersten Crawling automatisch in PostgreSQL angelegt.
