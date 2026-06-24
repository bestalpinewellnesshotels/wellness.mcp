# OpenAI Custom GPT - System Instructions Anleitung

## Wo finde ich die System Instructions?

### Sie befinden sich aktuell hier:
❌ **Settings/Einstellungen** → Zeigt nur App-Infos, URL, Autorisierung
- Hier können Sie KEINE Instructions eingeben

### Sie müssen hierhin:
✅ **GPT Editor/Builder** → Hier sind die Instructions

---

## 🔧 So gelangen Sie zum GPT Editor:

### Variante A: Aus dem Screenshot-Bereich

1. **Klicken Sie oben links auf "Zurück"** (siehe Pfeil in Ihrem Screenshot)
2. Sie gelangen zur App-Übersicht
3. **Klicken Sie auf Ihre App** "Bestwellness Prototype"
4. **Klicken Sie auf "Bearbeiten" oder das Stift-Symbol** (✏️)
5. Sie befinden sich jetzt im **GPT Builder**

### Variante B: Direkt über URL

Gehen Sie zu:
```
https://chat.openai.com/gpts/editor/[IHRE-GPT-ID]
```

Oder einfach:
```
https://chat.openai.com/gpts
```
→ Ihre Apps werden angezeigt → Klick auf App → "Bearbeiten"

---

## 📝 Im GPT Builder - So sieht es aus:

```
┌─────────────────────────────────────────────────────────────┐
│  GPT Builder                                                 │
├──────────────────┬──────────────────────────────────────────┤
│                  │                                           │
│  Create          │  Configure                                │
│                  │                                           │
├──────────────────┼──────────────────────────────────────────┤
│                  │                                           │
│  [Preview]       │  Name: Bestwellness Prototype             │
│                  │                                           │
│  Testbereich     │  Description: ...                         │
│  zum Ausprobieren│                                           │
│                  │  ┌────────────────────────────────────┐   │
│                  │  │ Instructions: ← HIER!              │   │
│                  │  │                                    │   │
│                  │  │ [Großes Textfeld]                  │   │
│                  │  │                                    │   │
│                  │  │ Hier kopieren Sie die System       │   │
│                  │  │ Instructions aus der Datei ein     │   │
│                  │  │                                    │   │
│                  │  └────────────────────────────────────┘   │
│                  │                                           │
│                  │  Conversation starters: ...               │
│                  │                                           │
│                  │  Knowledge: [Upload files]                │
│                  │                                           │
│                  │  Capabilities:                            │
│                  │  ☐ Web Browsing                           │
│                  │  ☐ DALL-E Image Generation                │
│                  │  ☐ Code Interpreter                       │
│                  │                                           │
│                  │  Actions: ← Ihre Tools/API               │
│                  │  [Create new action]                      │
│                  │                                           │
└──────────────────┴──────────────────────────────────────────┘
```

---

## ✅ Schritt-für-Schritt: System Instructions eingeben

### Schritt 1: GPT Editor öffnen
1. Gehen Sie zu: https://chat.openai.com/gpts
2. Finden Sie "Bestwellness Prototype"
3. Klicken Sie auf die App
4. Klicken Sie auf **"Bearbeiten"** oder **"Edit"** oder das Stift-Symbol ✏️

### Schritt 2: Zum Tab "Configure" wechseln
- Oben sehen Sie zwei Tabs: **Create** und **Configure**
- Klicken Sie auf **"Configure"**

### Schritt 3: Instructions-Feld finden
- Scrollen Sie nach unten
- Sie sehen ein großes Textfeld: **"Instructions"**
- Möglicherweise steht schon etwas drin

### Schritt 4: System Instructions einfügen

Öffnen Sie die Datei:
```
OPENAI-INTEGRATION-INSTRUCTIONS.md
```

Kopieren Sie den gesamten Abschnitt unter:
**"🤖 GPT System Instructions (KRITISCH!)"**

Das ist der Text, der mit beginnt:
```
Du bist ein spezialisierter Hotel-Informationssassistent...
═════════════════════════════════════════════════════════════════
⚠️  ABSOLUTE VERHALTENSREGELN (HÖCHSTE PRIORITÄT) ⚠️
═════════════════════════════════════════════════════════════════
...
```

### Schritt 5: In das Instructions-Feld einfügen
- Markieren Sie den alten Text (falls vorhanden)
- Fügen Sie den kopierten Text ein (Strg+V)
- Der Text sollte jetzt im Instructions-Feld stehen

### Schritt 6: Actions konfigurieren
- Scrollen Sie weiter nach unten zum Bereich **"Actions"**
- Hier müssen Ihre 4 Tools konfiguriert sein:
  - `ask_hotel_question`
  - `recommend_hotels`
  - `search_hotels`
  - `list_all_hotels`

### Schritt 7: Speichern
- Klicken Sie oben rechts auf **"Update"** oder **"Aktualisieren"**
- Fertig!

---

## 🧪 Nach dem Speichern - Testen

### Im Preview-Bereich (links):
Testen Sie kritische Anfragen:

**Test 1 (sollte funktionieren):**
```
Du: "Welche Hotels haben einen Pool?"
Erwartung: Liste von Hotels mit Pool
```

**Test 2 (KRITISCH - sollte KEINE externen Hotels nennen):**
```
Du: "Welche Hotels akzeptieren American Express?"
Erwartung: "Keine Hotels gefunden..."
FALSCH wäre: "Ich empfehle das Hotel Sacher..." ❌
```

**Test 3 (KRITISCH - kein Trainingswissen verwenden):**
```
Du: "Erzähl mir über das Hotel Sacher in Wien"
Erwartung: "Nicht in unserer Datenbank"
FALSCH wäre: Details über Hotel Sacher ❌
```

---

## 🔍 Unterschied: Settings vs. Builder

### Settings (wo Sie gerade sind):
- **Zweck**: App-Verwaltung, Berechtigungen, Verknüpfungen
- **Zugriff auf**: URL, Autorisierung, Datenschutz
- **KEINE Instructions hier!**

### Builder/Editor (wo Sie hin müssen):
- **Zweck**: App-Konfiguration, Verhalten definieren
- **Zugriff auf**: Instructions, Tools, Actions, Capabilities
- **Hier sind die System Instructions!**

---

## 📸 Visuelle Unterscheidung

### Sie sind HIER (Screenshot):
```
┌─────────────────────────────────────┐
│ ← Zurück                            │
│                                      │
│ B  Bestwellness Prototype    [DEV]  │
│                                      │
│ Einstellungen                        │
│                                      │
│ Info                                 │
│ Verbunden am: 17. Feb. 2026         │
│ URL: https://mcp.bestalpine2.ms.mynet.at/sse │
│ Autorisierung: Keinen               │
│                                      │
│ Aktionen                            │
└─────────────────────────────────────┘
```

### Sie müssen HIERHIN:
```
┌─────────────────────────────────────┐
│ GPT Builder                          │
├──────────┬──────────────────────────┤
│ Create   │ Configure    ← HIER!     │
├──────────┼──────────────────────────┤
│ Preview  │ Name: ...                │
│          │ Description: ...         │
│ [Chat]   │ Instructions: [...]  ←!  │
│          │ Capabilities: ...        │
│          │ Actions: ...         ←!  │
└──────────┴──────────────────────────┘
```

---

## 🆘 Falls Sie den Builder nicht finden:

### Möglichkeit 1: Über "Meine GPTs"
1. Klicken Sie auf Ihr Profilbild (oben rechts)
2. Wählen Sie "Meine GPTs" oder "My GPTs"
3. Ihre App erscheint
4. Klicken Sie auf "Bearbeiten"

### Möglichkeit 2: Über die Seitenleiste
1. In ChatGPT klicken Sie links in der Seitenleiste
2. Suchen Sie nach "GPTs" oder "Explore GPTs"
3. Gehen Sie zu "Your GPTs"
4. Finden Sie Ihre App → Bearbeiten

### Möglichkeit 3: Direkte URL
```
https://chat.openai.com/gpts/editor
```

---

## 🎯 Zusammenfassung

**Problem**: Sie sind in den Settings, nicht im Builder
**Lösung**: Zurück → App auswählen → "Bearbeiten" klicken

**Dann**:
1. Tab "Configure" öffnen
2. Runterscrollen zum Feld "Instructions"
3. System Instructions aus OPENAI-INTEGRATION-INSTRUCTIONS.md einfügen
4. Actions konfigurieren (Ihre 4 Tools)
5. Speichern
6. Testen!

---

**Die System Instructions sind das Herzstück Ihrer App - sie steuern das Verhalten!**
