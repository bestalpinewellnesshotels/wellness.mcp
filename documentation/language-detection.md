# Sprachwahl in der Konversation

**Stand:** 31. August 2026  
**Betrifft:** lokale Spracherkennung (Klassifizierer) in der Chat-Pipeline

Nachbau durch ein anderes Modell: **`documentation/absolut-immobilien.md` Teil IV** (vollständig, inkl. Softmax-Falle und Polyglot-Liste). Diese Datei ist die Kurzfassung.

---

## 1. Warum die Sprache nach jeder Eingabe geprüft wird

Die Sprache kommt nicht mehr von einem LLM-Call, sondern von einem lokalen n-Gramm-Klassifizierer. Der Lauf ist billig genug, um **nach jeder User-Eingabe** zu erfolgen – nicht nur nach dem ersten Satz.

Ein Client-Feld `language` (MCP/API) überschreibt die Erkennung **nicht** mehr. Folge-Turns hängen an der **Session** (`sessionId`), nicht an einem einmalig gemerkten ISO-Code im Client.

Ohne `sessionId` gilt jede Nachricht als erster Satz (inkl. möglicher Nachfrage). Deshalb muss ChatGPT `sessionId` aus dem vorherigen Tool-Ergebnis wieder mitgeben.

---

## 2. Regeln

### 2.1 Erster Satz – Gewichte nah beieinander

Sätze wie `Hotels in Salzburg` sind in mehreren Sprachen gültig (Deutsch, Englisch, Niederländisch/Flämisch). Der n-Gramm-Klassifizierer kann solche Kurzphasen trotzdem einer Sprache zuordnen (oft Niederländisch, Katalanisch oder Rätoromanisch mit hoher Softmax-Wahrscheinlichkeit).

**Es wird nicht nachgefragt.** ChatGPT hat keine zuverlässige Sprachauswahl-UI (kein Elicitation-Picker); eine Tool-Antwort `language_clarify` würde die Suche stoppen und in einer Schleife hängen. Deshalb:

1. Deutsche Marker (`mit`, `und`, `ich`, `suche`, …) → Deutsch, sperren
2. Geteilter Hotel-Wortschatz oder uneindeutige Kernsprachen → Client-Hint (`language`, z. B. ChatGPT-Gesprächssprache) oder **Deutsch**
3. Exotische Top-Sprache (`rm`, `ca`, …) bei Hotelphrasen → wie 2., niemals Rumantsch/Katalanisch
4. Eindeutige Kernsprache (`de`/`en`/`nl`/`it`/`fr`) → diese Sprache, sperren

Eine offene alte Nachfrage (`PendingCodes`) wird mit `Deutsch`/`English`/… (auch wenn nicht in der Pending-Liste) oder mit `Ja`/`Yes` aufgelöst; sonst läuft die ursprüngliche Suche auf Deutsch weiter.

### 2.2 Erster Satz eindeutig – Folgesätze kleben an dieser Sprache

War die Sprache im ersten Satz **eindeutig**, wird sie in der Session gesperrt.

Kommt danach ein Satz, bei dem eine *andere* Sprache die höchste Wahrscheinlichkeit hat, die Wahrscheinlichkeit der **ersten** Sprache aber **weiterhin hoch** ist (nur nicht die höchste), bleibt die Sprache des ersten Satzes.

Dasselbe gilt für geteilte Kurzphrasen: erster Satz klar Deutsch, Folgesatz `Hotels in Salzburg` (Klassifizierer oft Niederländisch) → Antwort weiter auf Deutsch.

Wechselt der User klar in eine andere trainierte Sprache (hohe Konfidenz, erste Sprache nicht mehr hoch), wird umgestellt.

### 2.3 Fallback: Deutsch

Zu wenig Text, leerer Text oder unsichere lateinische Zeichenfolge ohne klare Sprache → **Deutsch**. Diese Wahl wird **nicht** gesperrt; der nächste Satz darf neu entscheiden.

### 2.4 Nicht erkannte Sprache (z. B. Japanisch)

Der Klassifizierer kennt europäische Sprachen. Steht der Text überwiegend in einer **nicht trainierten Schrift** (Japanisch, Chinesisch, Arabisch, Hebräisch, Thai, …), antwortet das System auf **Englisch**.

Die Session-Sprache wird dabei nicht auf Englisch umgeschrieben. Ein späterer deutscher Satz kann wieder Deutsch setzen.

---

## 3. Ablauf in der Pipeline

```
User-Text
    → lokaler Language-Klassifizierer (jede Eingabe)
    → Gesprächsregeln (Session: Sperre / Nachfrage)
    → bei Nachfrage: Antwort, Stopp
    → Ethical → Intent → Translate/Search/Answer in der gewählten Sprache
```

Die gewählte Sprache steuert Filler, Translate-Skip (nur bei `de`), Answer-Prompt `{language}` und Reject-Texte (Deutsch vs. Englisch).

---

## 4. Technische Anker

| Teil | Ort |
|------|-----|
| Klassifizierer | `src/HotelChatbot.Infrastructure/Classifiers/Language/` |
| Scores + Schriftfilter | `LanguageClassifier`, `ScriptFilter.IsUnsupportedScript` |
| Gesprächsregeln | `src/HotelChatbot.Application/Services/ConversationLanguagePolicy.cs` |
| Nachfrage-Texte / Aliase | `LanguageClarification.cs` |
| Pipeline | `ChatService.RunSafetyGatesAsync` |
| Session-Zustand | System-Nachricht `__conversation_language__:` |
| MCP `sessionId` | Tool `get_response` / `get_hotel_details` |

**Schwellen (Policy, nicht Klassifizierer):**

- Kandidat für „nah beieinander“: Wahrscheinlichkeit ≥ 0,10 **und** Abstand zum Führenden ≤ 0,15 **oder** mindestens 50 % des Führenden
- höchstens 4 Sprachen in der Nachfrage
- „erste Sprache noch hoch“ im Folgesatz: Wahrscheinlichkeit ≥ 0,12 **oder** sie gehört zur Close-Gruppe

Klassifizierer-intern: `MinConfidence = 0,36`, `MinMargin = 0,04` – darunter `IsAmbiguous` statt hartem Deutsch-Fallback.

---

## 5. Beispiele

| Turn | User | Verhalten |
|------|------|-----------|
| 1 | `Hotels in Salzburg` | Deutsch (kein Ask), Hint `en` → Englisch |
| 2 | `Deutsch` | nur relevant bei alter Pending-Session |
| 1 | `Ich suche ein Wellnesshotel mit Sauna in Tirol.` | de, eindeutig, sperren |
| 2 | `Hotels in Salzburg` | bleibt de (sticky), obwohl nl/en höher liegen können |
| 1 | `京都のホテルを探しています` | Antwort auf Englisch |
| 1 | `xyz` / zu kurz | Deutsch, nicht sperren |

---

## 6. Tests

`src/HotelChatbot.Tests/ConversationLanguagePolicyTests.cs` deckt Nachfrage, Sticky, Sprachwechsel, Japanisch → Englisch, Fallback Deutsch und die Auflösung der Nachfrage ab.
