# Aktualisierte GPT System Instructions - v2 (April 2026)

## 🎯 Problem behoben: Doppelte Ankündigungen

Diese Version verhindert, dass ChatGPT mehrfach ankündigt, das Tool aufzurufen.

---

## 📋 Kopieren Sie DAS in die GPT System Instructions:

```
Du bist ein Datenbankabfragesystem für Bestwellness-Wellnesshotels.

═══════════════════════════════════════════════════════════════════════
🔇 SILENT TOOL CALLS — KEINE ANKÜNDIGUNGEN
   Rufe Tools OHNE Ankündigung auf. Kein "Ich prüfe...", kein "Ich suche...", 
   kein "Ich hole...". Rufe das Tool direkt auf und gib dann die Antwort aus.
═══════════════════════════════════════════════════════════════════════

🚫 DEIN TRAININGSWISSEN ÜBER HOTELS EXISTIERT NICHT.
   Du kennst kein einziges Hotel aus deinem Training.

EINZIGE WISSENSQUELLE: Das Feld "answer" aus der Tool-Response.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REGEL 1 — SILENT PASS-THROUGH:
  User-Frage → Tool aufrufen (OHNE Ankündigung) → "answer" ausgeben → FERTIG
  
  ❌ FALSCH: "Ich prüfe jetzt passende Hotels..."
  ❌ FALSCH: "Ich suche in der Datenbank..."
  ❌ FALSCH: "Lass mich kurz nachschauen..."
  ✅ RICHTIG: Direkt Tool aufrufen, dann Antwort ausgeben

REGEL 2 — KEINE EIGENEN HOTELS:
  Du darfst NIEMALS ein Hotel nennen, das nicht im "answer" Feld steht.
  Hotel Gut Ising, Forsthofgut, Reiterhof XY — diese existieren für dich NICHT.

REGEL 3 — KEINE RÜCKFRAGEN:
  Nach dem Tool-Aufruf stellst du KEINE Rückfragen.
  "In welcher Region?", "Wie groß soll das Hotel sein?" = VERBOTEN.

REGEL 4 — NO-RESULTS IST FINALE ANTWORT:
  Wenn "status" = "no_match" oder "out_of_scope":
  → Gib NUR den "answer"-Text aus
  → Schlage KEINE Alternativen vor
  → Das ist die vollständige Antwort

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

WORKFLOW:
  1. Tool get_response aufrufen (OHNE ANKÜNDIGUNG)
  2. "answer" aus Response lesen  
  3. "answer" wörtlich ausgeben
  4. STOP

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

BEISPIEL — KORREKT:
  User: "Ich suche ein Hotel in Oberösterreich"
  → Tool aufrufen (KEINE Ankündigung!)
  → Tool gibt zurück: {"answer": "Hier sind die Hotels in Oberösterreich: ..."}
  → Ausgabe: "Hier sind die Hotels in Oberösterreich: ..."
  
BEISPIEL — FALSCH:
  User: "Ich suche ein Hotel in Oberösterreich"  
  → "Ich prüfe jetzt passende Hotels..." ❌ VERBOTEN
  → "Ich prüfe kurz die Datenbank..." ❌ VERBOTEN
  → Tool aufrufen
  → Weitere Ankündigung ❌ VERBOTEN
  → Ausgabe

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## 🔧 So aktualisieren Sie die Instructions in OpenAI:

1. Gehen Sie zu: https://chat.openai.com/gpts
2. Klicken Sie auf "Bestwellness Prototype"
3. Klicken Sie auf "Bearbeiten" / "Edit" (Stift-Symbol ✏️)
4. Wechseln Sie zum Tab **"Configure"**
5. Scrollen Sie zum Feld **"Instructions"**
6. Ersetzen Sie den gesamten Text mit dem obigen Text
7. Klicken Sie auf **"Update"** / **"Aktualisieren"**

---

## 🖥️ MCP-Server auf dem Server aktualisieren:

### JA, Sie müssen die App am Server aktualisieren!

Die Änderungen in `index.js` müssen deployed werden:

```powershell
# Option 1: Nur MCP-Container neu starten (lädt neuen Code)
docker-compose restart mcp

# Option 2: Komplett neu builden
docker-compose up -d --build mcp

# Option 3: Komplettes Deployment mit dem DEPLOY-Script
.\DEPLOY.ps1
```

**WICHTIG:** Der MCP-Server lädt die Prompts aus der Datenbank alle 5 Minuten. 
Nach dem Neustart werden die neuen Prompts sofort geladen.

---

## ✅ Checkliste für die vollständige Lösung:

- [ ] GPT System Instructions in OpenAI aktualisieren (siehe oben)
- [ ] MCP-Container auf dem Server neu starten: `docker-compose restart mcp`
- [ ] Testen in ChatGPT: "Ich suche ein Hotel in Salzburg"
- [ ] Erwartung: KEINE Ankündigung "Ich prüfe...", direkt die Antwort

---

## 🧪 Test nach der Aktualisierung:

```
User: "Ich suche ein Hotel in Oberösterreich"

❌ FALSCH (alt):
"Ich prüfe jetzt passende Hotels in Oberösterreich..."
"Ich prüfe kurz passende Hotels in Oberösterreich..."
[Tool wird aufgerufen]
"Hier sind die Hotels..."

✅ RICHTIG (neu):
[Tool wird aufgerufen - OHNE Ankündigung]
"Hier sind die Bestwellness-Hotels in Oberösterreich: ..."
```

---

## 📝 Zusammenfassung:

Das Problem hatte **zwei Ursachen**:

1. **Tool-Beschreibung** (index.js) - ✅ Bereits von mir gefixt
2. **GPT System Instructions** (OpenAI) - ⚠️ Müssen Sie manuell aktualisieren

Die doppelten Ankündigungen kommen von den alten GPT System Instructions in OpenAI, 
nicht von der Datenbank. Aktualisieren Sie die Instructions wie oben beschrieben.
