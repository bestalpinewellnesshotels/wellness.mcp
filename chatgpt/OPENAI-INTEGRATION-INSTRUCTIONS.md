# OpenAI Integration Instructions - FINAL ANSWER MODE

## 🎯 Zweck

Diese Anleitung erklärt, wie die Tools so in OpenAI konfiguriert werden müssen, dass **ausschließlich** Informationen aus der internen Hoteldatenbank verwendet werden.

## ⚠️ KRITISCHES KONZEPT: Final Answer Pattern

### Problem (Vorher)
```
User fragt → Tool liefert Rohdaten → OpenAI interpretiert → OpenAI fügt eigenes Wissen hinzu ❌
```

### Lösung (Jetzt)
```
User fragt → Tool liefert FERTIGE Antwort → OpenAI gibt sie 1:1 weiter ✅
```

## 📋 Tool-Konfiguration in OpenAI

### Tool 1: ask_hotel_question

**Name:** `ask_hotel_question`

**Description (KRITISCH):**
```
Beantwortet spezifische Fragen zu einem Hotel basierend AUSSCHLIESSLICH auf der internen Datenbank.

WICHTIG: Das Tool liefert eine vollständige, formatierte Antwort im "answer" Feld.
Diese Antwort MUSS 1:1 an den User weitergegeben werden, OHNE Interpretation, Ergänzung oder Veränderung.

Verwende das Tool wenn:
- Der User nach einem spezifischen Hotel fragt
- Details zu einem bestimmten Hotel benötigt werden
- Fragen zu Ausstattung, Preisen, Zimmern gestellt werden

Die Antwort ist vollständig und benutzerfreundlich formatiert.
```

**Parameters:**
```json
{
  "type": "object",
  "properties": {
    "hotelId": {
      "type": "string",
      "description": "Hotel-ID (z.B. 'hotel_stock'). Muss zuerst via search_hotels ermittelt werden."
    },
    "question": {
      "type": "string",
      "description": "Die spezifische Frage zum Hotel"
    },
    "sessionId": {
      "type": "string",
      "description": "Optional: Session-ID für Konversationskontext"
    },
    "language": {
      "type": "string",
      "enum": ["de", "en"],
      "default": "de",
      "description": "Sprache der Antwort"
    }
  },
  "required": ["hotelId", "question"]
}
```

**Response Schema:**
```json
{
  "success": true,
  "responseType": "final_answer",
  "dataSource": "internal_database_only",
  "answer": "VOLLSTÄNDIGE FORMATIERTE ANTWORT - 1:1 ÜBERNEHMEN!",
  "disclaimer": "Hinweis zur Datenquelle",
  "metadata": {
    "confidenceScore": 0.85,
    "timestamp": "..."
  }
}
```

---

### Tool 2: recommend_hotels

**Name:** `recommend_hotels`

**Description (KRITISCH):**
```
Empfiehlt Hotels aus der internen Datenbank basierend auf Kundenanforderungen.

WICHTIG: Das Tool liefert eine vollständige, formatierte Empfehlungsantwort im "answer" Feld.
Diese Antwort ist in sich geschlossen und MUSS 1:1 an den User weitergegeben werden.

Das Tool sucht AUSSCHLIESSLICH in der internen Datenbank. Hotels außerhalb dieser Datenbank
können und dürfen NICHT empfohlen werden.

Wenn keine passenden Hotels gefunden werden, enthält "answer" eine klare Meldung dazu.
In diesem Fall darfst du KEINE alternativen Hotels aus deinem Trainingswissen vorschlagen.
```

**Parameters:**
```json
{
  "type": "object",
  "properties": {
    "requirements": {
      "type": "string",
      "description": "Kundenanforderungen (z.B. 'Hotel mit Pool und Sauna, Adults Only')"
    },
    "maxResults": {
      "type": "integer",
      "default": 3,
      "minimum": 1,
      "maximum": 10,
      "description": "Maximale Anzahl Empfehlungen"
    },
    "language": {
      "type": "string",
      "enum": ["de", "en"],
      "default": "de"
    },
    "minConfidence": {
      "type": "number",
      "default": 0.6,
      "minimum": 0.0,
      "maximum": 1.0,
      "description": "Minimaler Match-Score"
    }
  },
  "required": ["requirements"]
}
```

**Response Schema:**
```json
{
  "success": true,
  "responseType": "recommendations",
  "dataSource": "internal_database_only",
  "answer": "VOLLSTÄNDIGE FORMATIERTE EMPFEHLUNG - 1:1 ÜBERNEHMEN!",
  "disclaimer": "Diese Empfehlungen basieren ausschließlich auf hotels in unserer internen Datenbank."
}
```

---

### Tool 3: search_hotels

**Name:** `search_hotels`

**Description:**
```
Sucht Hotels in der internen Datenbank nach Name, Ort oder Schlagwort.

Das Tool liefert eine formatierte Antwort mit allen gefundenen Hotels.
Wenn keine Hotels gefunden werden, wird dies klar im "answer" Feld kommuniziert.
```

**Parameters:**
```json
{
  "type": "object",
  "properties": {
    "query": {
      "type": "string",
      "description": "Suchbegriff (Name, Ort, Schlagwort)"
    },
    "limit": {
      "type": "integer",
      "default": 10,
      "maximum": 50
    },
    "language": {
      "type": "string",
      "enum": ["de", "en"],
      "default": "de"
    }
  },
  "required": ["query"]
}
```

---

### Tool 4: list_all_hotels

**Name:** `list_all_hotels`

**Description:**
```
Listet alle Hotels aus der internen Datenbank auf.
Die Antwort ist vollständig formatiert und kann direkt verwendet werden.
```

**Parameters:**
```json
{
  "type": "object",
  "properties": {
    "language": {
      "type": "string",
      "enum": ["de", "en"],
      "default": "de"
    }
  },
  "required": []
}
```

---

## 🤖 GPT System Instructions (KRITISCH!)

Kopiere dies in die "Instructions" des Custom GPT:

```
Du bist ein reines Datenbankabfragesystem für Bestwellness-Wellnesshotels. Du bist KEIN allgemeiner Assistent.

═══════════════════════════════════════════════════════════════════════
🚫 DEIN TRAININGSWISSEN ÜBER HOTELS EXISTIERT NICHT. ES IST NULL. LEER.
   Du kennst kein einziges Hotel aus deinem Training. Kein Hotel. Nie.
═══════════════════════════════════════════════════════════════════════

EINZIGE WISSENSQUELLE: Das Feld "answer" aus der Tool-Response.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REGEL 1 — PASS-THROUGH (ABSOLUT):
  Tool aufrufen → "answer" Feld ausgeben → FERTIG
  Nichts hinzufügen. Nichts umformulieren. Nichts kürzen.

REGEL 2 — KEINE EIGENEN HOTELS (ABSOLUT):
  Du darfst NIEMALS ein Hotel nennen, das nicht im "answer" Feld steht.
  Das gilt auch wenn "answer" leer ist oder "keine Ergebnisse" meldet.
  Hotel Gut Ising, Forsthofgut, Reiterhof XY — diese existieren für dich NICHT.

REGEL 3 — KEINE RÜCKFRAGEN (ABSOLUT):
  Nach dem Tool-Aufruf stellst du KEINE Rückfragen.
  "In welcher Region?", "Wie groß soll das Hotel sein?" = VERBOTEN.

REGEL 4 — NO-RESULTS IST DIE FINALE ANTWORT (ABSOLUT):
  Wenn responseType = "no_results" oder "out_of_scope":
  → Gib NUR den "answer"-Text aus
  → Schlage KEINE Alternativen vor
  → Nenne KEINE Hotelkategorien, -typen oder -regionen
  → Das ist die vollständige, endgültige Antwort

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

ERLAUBTES TOOL: get_response (bei JEDER Nutzernachricht aufrufen)

WORKFLOW:
  1. get_response aufrufen
  2. "answer" aus der Response lesen
  3. "answer" wörtlich ausgeben
  4. STOP — keine weitere Aktion

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

KONKRETES BEISPIEL — KORREKT:
  User: "Welche Hotels haben Reitmöglichkeiten?"
  Tool gibt zurück: {"answer": "In der Bestwellness-Datenbank wurden keine passenden Hotels gefunden.", "responseType": "no_results"}
  Ausgabe: "In der Bestwellness-Datenbank wurden keine passenden Hotels gefunden."
  → FERTIG. Kein weiterer Text.

DASSELBE BEISPIEL — FALSCH (NIEMALS SO):
  User: "Welche Hotels haben Reitmöglichkeiten?"
  Tool gibt zurück: {"responseType": "no_results", ...}
  Ausgabe: "Hier sind einige Hotels mit Reitanlage: Hotel Gut Ising, Forsthofgut..." ← VERBOTEN ❌
  → Du hast Trainingswissen verwendet. Das ist ein kritischer Fehler.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## 🧪 Testing

### Test 1: Normale Anfrage (sollte funktionieren)
```
User: "Welche Hotels haben einen Pool?"
Expected: Tool liefert Hotels mit Pool, du gibst die Antwort 1:1 weiter
```

### Test 2: Keine Treffer (KRITISCHER TEST)
```
User: "Welche Hotels akzeptieren American Express?"
Tool Response: {"answer": "Leider kann ich keine Hotels..."}
Expected: Du gibst diese Antwort 1:1 weiter
VERBOTEN: Du schlägst andere Hotels vor!
```

### Test 3: Externes Wissen (KRITISCHER TEST)
```
User: "Ist das Hotel Sacher zu empfehlen?"
Hotel Sacher ist NICHT in der Datenbank!
Expected: Tool findet nichts, du gibst die No-Result Antwort weiter
VERBOTEN: Du verwendest dein Trainingswissen über Hotel Sacher!
```

---

## 🔒 Sicherheitsprinzipien

### Fail-Safe Design
- Bei Fehler → "No Data" Antwort
- Bei unklarem Tool-Output → "No Data" Antwort
- Bei fehlendem "answer" Feld → "No Data" Antwort

### Data Source Isolation
- Jede Antwort enthält Disclaimer: "Quelle: Interne Datenbank"
- Klar kommuniziert: Scopebegrenzung

### Transparency
- User wird informiert wenn Info nicht verfügbar ist
- Keine falschen Versprechungen
- Keine Halluzinationen

---

## 📊 Response Format

Alle Tools antworten mit diesem Schema:

```json
{
  "success": true/false,
  "responseType": "final_answer" | "no_data" | "no_results" | "error",
  "dataSource": "internal_database_only",
  "answer": "VOLLSTÄNDIGE FORMATIERTE ANTWORT - 1:1 VERWENDEN!",
  "disclaimer": "Zusätzlicher Hinweis zur Datenquelle",
  "metadata": {
    "confidenceScore": 0.0-1.0,
    "timestamp": "ISO-8601"
  }
}
```

### Wichtig:
- Das `answer` Feld ist die **finale Antwort**
- Es ist bereits vollständig formatiert (Markdown, Absätze, Aufzählungen)
- Es MUSS 1:1 ausgegeben werden
- KEINE Interpretation, KEINE Ergänzung, KEINE Kürzung

---

## 🚨 Häufige Fehler vermeiden

### ❌ FEHLER 1: Umformulierung
```
Tool: "Ich habe 3 Hotels gefunden..."
GPT: "Basierend auf deiner Suche konnte ich folgende Hotels identifizieren..." ❌
```

### ❌ FEHLER 2: Ergänzung aus Trainingswissen
```
Tool: "Diese Hotels befinden sich in Tirol..."
GPT: "Tirol ist bekannt für seine alpine Landschaft und..." ❌
```

### ❌ FEHLER 3: Alternative Vorschläge
```
Tool: "Kein Hotel gefunden für American Express"
GPT: "Aber ich kann dir andere Hotels in der Region empfehlen..." ❌
```

### ✅ RICHTIG: 1:1 Übernahme
```
Tool: "Ich kann keine Hotels finden..."
GPT: "Ich kann keine Hotels finden..." ✅
```

---

## 🎓 Best Practices

1. **Immer Tool zuerst:** Keine Antwort ohne Tool-Call
2. **Answer Feld priorisieren:** Wenn vorhanden, direkt verwenden
3. **Keine Improvisation:** Nur Tool-Daten, nichts anderes
4. **Transparenz:** Bei No-Result klar kommunizieren
5. **Disclaimer beachten:** Immer mit ausgeben

---

## 📞 Support

Bei Problemen:
1. Überprüfe ob Tool "answer" Feld liefert
2. Überprüfe ob GPT Instructions korrekt sind
3. Teste mit kritischen Anfragen (American Express, externer Hotel-Name)
4. Logge Tool-Responses und GPT-Outputs

---

**Version:** 2.0.0  
**Datum:** März 2026  
**Kritikalität:** HOCH - Diese Konfiguration ist SICHERHEITSKRITISCH
