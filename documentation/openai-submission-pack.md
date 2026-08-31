# OpenAI Plugin Submission Pack – Best Alpine Wellness Hotels

Stand: 2026-08-09  
MCP Server URL: `https://mcp.bestalpine2.ms.mynet.at/mcp`  
Auth: keine (öffentlich, read-only)

---

## 1. Listing (Portal-Felder)

| Feld | Vorschlag |
|------|-----------|
| Display name (≤30) | Best Alpine Wellness |
| Short description (≤30) | Alpine wellness hotel guide |
| Long description | Find and explore Best Alpine Wellness Hotels in Austria and South Tyrol. Search by region, wellness focus, dogs, adults-only and more. Get editorial hotel details from the official Best Alpine database. Read-only: no bookings, payments or inquiries. |
| Developer name | Best Alpine Wellness Hotels |
| Category | Travel (oder nächstpassende Portal-Kategorie) |
| Website | https://www.wellnesshotel.com/ |
| Privacy Policy | https://www.wellnesshotel.com/de/datenschutz |
| Terms | https://www.wellnesshotel.com/de/agb |
| Support | https://www.wellnesshotel.com/de/ueber-uns/kontakt-service |
| Support-Mail | info@bestwellnesshotels.at |
| Logo | Rundlogo (PNG-Upload) bzw. https://www.wellnesshotel.com/static/img/logo.svg |
| Starter prompts (max. 3) | siehe unten |

### Starter prompts

1. Finde Wellnesshotels im Salzburger Land  
2. Welche Hotels sind hundefreundlich?  
3. Erzähle mir mehr über das Hotel Stock  

---

## 2. Tool-Annotations & Justifications

Beide Tools:

```json
{
  "readOnlyHint": true,
  "openWorldHint": false,
  "destructiveHint": false
}
```

### `get_response` (Search hotels)

| Annotation | Wert | Justification (Portal) |
|------------|------|------------------------|
| readOnlyHint | true | Retrieves hotel recommendations from the internal Best Alpine database only. Does not create, update, delete or send data. |
| openWorldHint | false | Does not post to public platforms or change external user-facing systems. Reads only our private hotel content index. |
| destructiveHint | false | No irreversible actions; safe to retry. |

### `get_hotel_details` (Get hotel details)

| Annotation | Wert | Justification (Portal) |
|------------|------|------------------------|
| readOnlyHint | true | Answers questions about one hotel using approved indexed content. No writes, bookings or messages. |
| openWorldHint | false | Operates only on our internal hotel database for a given hotelId. |
| destructiveHint | false | Read-only lookup; safe to retry. |

---

## 3. Positive Testfälle (genau 5)

### P1 – Regionssuche Salzburger Land
- **User prompt:** Finde Wellnesshotels im Salzburger Land  
- **tools_triggered:** `get_response`  
- **Erwartetes Ergebnis:** Status ok; `hotels[]` mit stabilen `hotelId` und Namen (z. B. Stock, Nesslerhof, Übergossene Alm möglich); Antwort nur aus Datenbank; keine erfundenen Hotels  

### P2 – Strukturierte Filter (Hunde + Wellness)
- **User prompt:** Ich suche ein hundefreundliches Wellnesshotel mit Spa  
- **tools_triggered:** `get_response` (Felder z. B. `dogsAllowed=true`, `wellnessFocus=spa`)  
- **Erwartetes Ergebnis:** Treffer oder klares no_match; bei Treffern IDs + Namen; keine Preise/Verfügbarkeit erfinden  

### P3 – Detailfrage nach Suche (Stock)
- **User prompt:** (nach P1) Was bietet das Hotel Stock im Wellnessbereich?  
- **tools_triggered:** `get_response` dann `get_hotel_details` mit `hotelId=hotel_stock_at`  
- **Erwartetes Ergebnis:** Antwort zu Stock aus freigegebenen Inhalten; strukturiertes `hotel`-Objekt mit ID/Name; Quellen-URLs wenn vorhanden  

### P4 – Spa-/Sauna-Frage zu Nesslerhof
- **User prompt / Tool:** `get_hotel_details` mit `hotelId=hotel_nesslerhof_at`, Frage zu Spa/Sauna  
- **tools_triggered:** `get_hotel_details`  
- **Erwartetes Ergebnis:** Nur redaktionell belegte Infos oder explizit „nicht verfügbar“; kein Raten  

### P5 – Deutsche Formulierung Regionssuche
- **User prompt:** Welche Best Alpine Hotels eignen sich für eine Wellnessauszeit in den Alpen?  
- **tools_triggered:** `get_response`  
- **Erwartetes Ergebnis:** Stabiler Such-Flow, Hotels mit IDs, Antwort auf Deutsch  

---

## 4. Negative Testfälle (genau 3)

### N1 – Buchungs-/Schreibwunsch (Out of scope für V1)
- **User prompt:** Bitte buche mir für nächstes Wochenende ein Zimmer im Hotel Stock und schick eine Bestätigung per E-Mail  
- **Erwartung:** Keine Buchung/E-Mail; Assistent nutzt höchstens Lese-Tools oder lehnt ab; keine Schreiboperation  

### N2 – Ungültige Hotel-ID
- **Tool-Call:** `get_hotel_details` mit `hotelId=hotel_does_not_exist_xyz`, Frage beliebig  
- **Erwartung:** Verständliche Fehlermeldung; kein Stacktrace; keine internen URLs/Tokens  

### N3 – Preise/Verfügbarkeit raten lassen
- **User prompt:** Was kostet eine Übernachtung im Hotel Stock nächstes Wochenende und habt ihr noch Zimmer frei?  
- **Erwartung:** Keine erfundenen Preise/Verfügbarkeiten; Hinweis dass diese Daten nicht verfügbar sind bzw. nur freigegebene Infos  

---

## 5. Release Notes (Kurz)

Version 1.0 – Public read-only MCP for Best Alpine Wellness Hotels: Streamable HTTP `/mcp`, structured hotel search with stable IDs, hotel detail lookup, editorial metadata, no bookings.

---

## 6. Automatischer Smoke-Test (Prod)

```powershell
cd chatgpt
node scripts/run-submission-tests.mjs https://mcp.bestalpine2.ms.mynet.at
```

**Ergebnis 2026-08-09:** ALL CHECKS PASSED  
(Health, Initialize, Annotations, P1–P5, N1–N3)

## 7. Manuelle Checkliste vor Submit

- [x] `GET /health` → `{"status":"ok"}`
- [x] MCP initialize / tools/list / tools/call (Script)
- [x] P1–P5 und N1–N3 (Script; N1 = keine Write-Tools)
- [ ] Domain-Challenge-Token gesetzt
- [ ] Scan Tools im Portal
- [ ] Listing-URLs im Portal eingetragen
- [ ] Demo-Recording-URL (Portal verlangt das für Directory)
