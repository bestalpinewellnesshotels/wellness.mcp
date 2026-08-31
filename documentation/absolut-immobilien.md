# Klassifizierer-Protokoll (Ethical + Intent)

**Stand:** 31. August 2026  
**Projekt:** Best Alpine Wellness / `wellness.mcp` (Hotel-Chatbot)  
**Ziel dieses Dokuments:** Ein anderes Sprachmodell soll Befund, Mechanik und Nachbesserung **ohne Raten** an einem zweiten Ort (Klon, anderes Produkt, frischer Checkout) umsetzen können.

Verwandt: `documentation/language-detection.md` (Kurzfassung Sprache). Vollständige Nachbau-Anleitung Sprache: **Teil IV**. `sessionId` ist Voraussetzung für Verlauf und Sprach-Sticky (Teil III und IV).

| Teil | Thema | Anlass |
|------|--------|--------|
| I | Ethical-Classifier | `kennst du hotels in Salzburg` fälschlich `ethical_reject` |
| II | Intent-Classifier | Ski-in/Ski-out- und Hausnamen-Fragen fälschlich `out_of_scope` |
| III | Gesprächskontext / Retrieval | Verlauf fließt nicht in Suche; „Tirol“ → dann Hotels außerhalb Tirol |
| IV | Sprache nach jeder Eingabe | Klassifizierer statt LLM; Mehrdeutigkeit (`Hotels in Salzburg`); Sticky; Japanisch→EN |

# Teil I — Ethical-Classifier: False Positives bei harmlosen Hotel-Fragen

**Anlass:** User-Eingabe `kennst du hotels in Salzburg` wurde vom Ethical-Gate abgelehnt.  
**Sitzung Teil I:** Analyse und Erklärung; fail-open im Service ist der Soll-Stand (Datei vor Änderung immer lesen).

---

## 0. Auftrag an ein späteres Modell

Wenn der User harmlose Hotel-/Sachfragen mit `ethical_reject` sieht, **nicht** jede Formulierung in die TSV kopieren und hoffen. Zuerst prüfen:

1. Läuft `EthicalClassifierService.IsEthical` wirklich fail-open (Insult-Marker Pflicht, sonst OK)?
2. Ist die TSV noch schablonenhaft (`Guten Tag, höflich suche ich …` vs. `Du bist ein Idiot …`)?
3. Sitzt Out-of-Topic beim **Intent**-Classifier, nicht beim Ethical?

Dann nach Abschnitt 6 umsetzen. Tests in Abschnitt 7 müssen grün sein, bevor die Änderung fertig gilt.

**Nicht tun**

- Kein exaktes String-Matching als Hauptlogik („nur durchlassen, was in der TSV steht“).
- Ethical nicht zum Themenfilter machen (`Was ist die Hauptstadt von Frankreich?` ist ethical **OK**, Intent **out_of_scope**).
- Die TSV nicht weiter mit 30-Sprachen-Schablonen aufblasen; das verschärft N-Gramm-Bias.
- `HardRejectMarkers` nicht für normale Wörter wie `du`, `kennst`, `hotels` erweitern.

---

## 1. Was in dieser Sitzung passiert ist

User-Frage: *Muss jede Formulierung exakt in den Trainingsdaten stehen, damit die Klassifizierung funktioniert?*

Antwort: **Nein.** `BinaryTextClassifier` ist kein Lookup. Es ist Naive Bayes auf Zeichen-N-Grammen (1–4) plus Wort-Features `w:…`. Groß/Klein und Satzzeichen fallen weg. `Kennst du Hotels in Salzburg?` und `kennst du hotels in Salzburg` sind für das Modell fast identisch.

Trotzdem kann eine harmlose Kurzfrage `reject` werden, weil die TSV die Klasse `ok` fast nur als lange Höflichkeitsformel lehrt und `reject` fast nur als kurze Anrede mit `Du …`. Das Wort `du` und kurze direkte Sätze korrelieren dann mit Beleidigung.

In den Trainingsdaten stand zum Analysezeitpunkt bereits u. a.:

```
ok	Kennst du Hotels in Salzburg?
ok	Welche Hotels kennst du in Salzburg?
```

Ein naher Nachbar in der TSV reicht bei Naive Bayes **nicht**, wenn Hunderte `Du bist ein Idiot, nenn Hotels.`-Zeilen die Features `w:du` / `" du "` dominieren.

---

## 2. Pipeline (Reihenfolge nicht ändern)

In `ChatService.RunSafetyGatesAsync`:

```
User-Text
  → Language (lokal, jede Eingabe; siehe language-detection.md)
  → Ethical          ← nur Beleidigung/Toxizität
  → Intent           ← Hotel-/Wellness-Scope
  → ConversationConstraints / Catalog|Search / Answer
       (Region+Fokus in Retrieval — siehe Teil III)
```

Bei Ethical-Fail:

- `RejectReason = "ethical_reject"`
- Deutsch: *Ihre Anfrage konnte nicht verarbeitet werden. Bitte formulieren Sie Ihre Frage höflich und respektvoll.*
- Englisch nur wenn Session-Sprache `en` ist.

Datei: `src/HotelChatbot.Application/Services/ChatService.cs`  
(Suche nach `_ethicalClassifier.IsEthical` und `"ethical_reject"`.)

---

## 3. Dateien

| Rolle | Pfad |
|-------|------|
| Gate-API | `src/HotelChatbot.Domain/Interfaces/IEthicalClassifier.cs` — `bool IsEthical(string text)` |
| Implementierung | `src/HotelChatbot.Infrastructure/Classifiers/EthicalClassifierService.cs` |
| Naive Bayes | `src/HotelChatbot.Infrastructure/Classifiers/BinaryTextClassifier.cs` |
| Features | `src/HotelChatbot.Infrastructure/Classifiers/Language/CharNGramExtractor.cs` |
| Training | `src/HotelChatbot.Infrastructure/Classifiers/Data/ethical-training.tsv` |
| TSV-Pfad | `ClassifierDataLocator.Resolve(..., typeof(EthicalClassifierService))` — Datei muss ins Output (`Classifiers/Data/…`) kopiert werden |
| Intent (nicht Ethical) | `IntentClassifierService.cs` + `Data/intent-training.tsv` + ggf. `HotelDomainCues` |
| Tests | `src/HotelChatbot.Tests/ClassifierSmokeTests.cs` |

TSV-Format: UTF-8, Tab, Kopf `label	text`, Labels nur `ok` und `reject`. Leere Zeilen und `#` ignoriert der Loader.

---

## 4. Wie der Klassifizierer rechnet

`BinaryTextClassifier.TrainFromTsv(path, positiveLabel: "ok", negativeLabel: "reject", minPositiveProbability: 0.40)`

`CharNGramExtractor.Extract`:

- Unicode NFC, `ToLowerInvariant`
- Wörter = Buchstaben/Combining Marks; Punkt/Fragezeichen sind Trenner
- pro Wort: Feature `w:` + Wort (max. 14 Zeichen) und Zeichen-N-Gramme 1–4 auf `" " + wort + " "`

`Classify`: Summe log P(ngram|klasse) + Prior, Softmax. Weniger als 2 Buchstaben → negative Klasse (`reject`). Das gilt für den **nackten** Bayes, nicht für den Service-Wrapper.

`IsPositive` (nur Tests auf dem nackten Bayes): Label `ok` und p ≥ 0,40.

Der **Service** darf den nackten Bayes nicht ungefiltert als Gate nutzen. Siehe Abschnitt 5.

---

## 5. Soll-Verhalten von `EthicalClassifierService`

Ethical darf **nur** klare Beleidigung/Hass/Selbstverletzung sperren. Harmlose Hotel-, Smalltalk- und sogar Off-Topic-Sachfragen müssen durch. Scope ist Intent.

Ist-Stand nach späteren Anpassungen (Stand dieses Dokuments — vor Änderung immer die Datei lesen):

1. Leerer/Whitespace-Text → `true`.
2. `HardRejectMarkers` (Substring, lowercased) → sofort `false` (Slurs, `kill yourself`, `heil hitler`, …).
3. **Kein** Treffer in `SoftInsultMarkers` → sofort `true` (**fail-open**). Der Bayes läuft dann gar nicht. Damit ist `kennst du hotels in Salzburg` OK, unabhängig von der TSV.
4. Insult-Cue vorhanden **und** Bayes-Label `reject` **und** p ≥ `MinRejectConfidence` (aktuell 0,55) → `false`.
5. Sonst `true` (Insult-Cue, aber Bayes unsicher).

Kommentar in der Datei muss dieses fail-open beschreiben. `minPositiveProbability: 0.40` beim Trainieren ist für `IsEthical` egal; der Service nutzt `Classify` + eigene Schwelle.

Wenn ein späteres Modell die Soft-Marker **nicht** findet: das ist der Bug, der `kennst du hotels in Salzburg` erklärt hat. Dann Abschnitt 6.1 nachziehen.

---

## 6. Umsetzung, falls das Gate wieder harmlose Fragen sperrt

### 6.1 Pflicht: fail-open im Service (nicht nur TSV)

In `EthicalClassifierService.IsEthical` muss gelten: **ohne Insult-Hinweis kein Reject.**

`SoftInsultMarkers` als Substring-Liste, lowercased, Beispiele (nicht vollständig; an vorhandene Liste anschließen, keine Duplikate, keine Allerweltswörter):

- en: `idiot`, `stupid`, `fuck you`, `shut up`, `asshole`, `moron`, …
- de: `nutzlos`, `dummkopf`, `halt die fresse`, `fick dich`, `vollidiot`, …

`du` allein ist **kein** Insult-Marker.

`HardRejectMarkers` bleiben die schwere Blacklist (Slurs / klare Gewalt). Nicht mit Soft-Markern vermischen.

Schwelle: Reject nur bei Bayes-`reject` und p ≥ ca. 0,55–0,68. Lieber etwas fail-open als Hotel-Fragen blockieren.

### 6.2 TSV: Vielfalt statt Vollständigkeit

Nicht jede User-Formulierung eintragen. Braucht werden **Sprechweisen** in `ok`:

- informell, kurz, mit `du`/`kennst du`/`habt ihr`
- ohne Anredeformel
- Ortsnamen (Salzburg, Tirol, Achensee, …)
- EN analog (`do you know hotels`, `which hotels`)
- bewusst Off-Topic als `ok` (Hauptstadt, Wetter, Witz) — Ethical ≠ Intent

`reject` nur mit **Beleidigung + optional Hotel-Thema**, z. B. `Du bist so dumm, nenn mir Hotels in Salzburg`. Nicht: kurzer höflicher Satz als `reject`.

Schablonen `Guten Tag, höflich suche ich …` / `Du bist ein Idiot, nenn Hotels.` über Dutzende Sprachen hinweg **nicht** weiter vermehren. Sie sind die Ursache für `w:du` → reject.

Nach TSV-Änderung: API/Tests neu bauen, damit `ClassifierDataLocator` die Kopie im Output trifft. Prozess neu starten; der Bayes trainiert im Konstruktor.

### 6.3 Intent nicht mit Ethical verwechseln

Wenn die Antwort „nicht zum Thema“ ist, `intent-training.tsv` / `HotelDomainCues` / `IntentClassifierService` anfassen, nicht Ethical.

Intent hat eigenes fail-open über Domain-Cues (Ski, Piste, Hotelname, …). Das bleibt getrennt. **Vollständige Intent-Umsetzung: Teil II.**

### 6.4 Kein LLM-Ethical-Call reaktivieren

Es gibt historische Prompts (`pipeline.ethical_check` in `PostgreSQLSystemPromptRepository`, Backup unter `docs/prompt-backups/`). Die laufende Pipeline nutzt den **lokalen** Classifier. Nicht still auf LLM zurückschalten.

---

## 7. Tests (müssen nach jeder Ethical-Änderung gelten)

Datei: `src/HotelChatbot.Tests/ClassifierSmokeTests.cs`

`EthicalClassifierService_AllowsHarmlessQueries` — mindestens:

- `kennst du hotels in Salzburg` (exakt die gemeldete Formulierung, klein, ohne Fragezeichen)
- `Welche Hotels kennst du in Salzburg?` / ohne `?` / mit gemischter Großschreibung
- `Kennst du Hotels in Salzburg?`
- `Ich suche ein Wellnesshotel mit Sauna in Tirol.`
- `Was ist die Hauptstadt von Frankreich?` (ethical OK)
- `Looking for a wellness hotel with spa`

`EthicalClassifierService_RejectsAbuse`:

- `You are stupid and useless.`
- `Du bist ein Idiot, nenn Hotels.`
- `Fuck you, recommend a hotel`

Der nackte-Bayes-Test `EthicalClassifier_RejectsAbuse_AcceptsPolite` prüft nur den TSV-Bayes, nicht den Service. Service-Regressionen gehören in die `EthicalClassifierService_*`-Theories.

Befehl: `dotnet test src/HotelChatbot.Tests/HotelChatbot.Tests.csproj --filter EthicalClassifier`

---

## 8. Diagnose, wenn es wieder passiert

1. Pipeline-Trace / Log: `[Ethical] REJECT` vs. Intent `out_of_scope`. Falsches Gate → falsche Datei.
2. Log-Zeilen aus `EthicalClassifierService`: `REJECT (Hard-Marker)` vs. `REJECT (Insult+Classifier …)` vs. `OK (kein Insult-Marker)`.
3. Repro in einem Test mit dem **exakten** User-String, nicht einer höflichen Umschreibung.
4. TSV: kommt `du`/`kennst` fast nur unter `reject` vor? Dann Service-fail-open prüfen, nicht 200 neue `ok`-Zeilen.
5. Läuft ein alter Prozess ohne neu gebaute TSV/DLL?

---

## 9. Kurzantwort auf die ursprüngliche Fachfrage

| Irrtum | Korrekt |
|--------|---------|
| Jede Formulierung muss 1:1 in der TSV stehen | Nein. Generalisierung über N-Gramme/Wörter. |
| Eine ähnliche `ok`-Zeile reicht immer | Nein. Mehrheits-Features (`du` in Reject-Schablonen) gewinnen. |
| Ethical filtert Off-Topic | Nein. Nur Toxizität. Off-Topic = Intent. |
| Kurz + `du` = unhöflich = reject | Nein. Das war der False-Positive-Pfad. |

Robustheit kommt von **fail-open ohne Insult-Cue** plus **gemischten** `ok`-Beispielen, nicht von einer vollständigen Fragenliste.

---

# Teil II — Intent-Classifier: False Positives bei in-scope Hotel-/Urlaubsfragen

**Anlass:** User-Eingabe (ohne Fragezeichen, österreichisch *Schi*):

```
Mich interessiert ob es Hotels gibt, bei denen man mit den Schi bis zum Hoteleingang fahren kann
```

Antwort des Bots: *Dieser Assistent beantwortet ausschließlich Fragen zu BestWellness-Wellnesshotels. Ihre Anfrage liegt außerhalb des Themenbereichs.*

**Sitzung Teil II (31.08.2026):** Code und Trainingsdaten geändert.  
**User-Vorgaben, die ein späteres Modell binden:**

1. Die Frage **ist** in-scope. Ski-Zugang, Piste, Wandern, Reiten, Hotelnamen gehören zum Thema (Seed-Prompt `pipeline.logical_check`).
2. **Kein Workaround** für genau diesen Satz (kein 1:1-Eintrag als einzige Fix-Zeile).
3. Lösung **generisch**: Trainingsdaten und/oder Code.
4. Sprachen in der TSV **erhalten**. Ursprüngliche Vorgabe: **mindestens 64 `in_scope` und 64 `out_of_scope` pro Sprache**.
5. Kartesische Wiederholungen mit marginaler Variation (gleicher Satz, nur Ort/Amenity getauscht) **entfernen**, nicht durch sechs Restzeilen ersetzen.

Gleicher Wurzeldefekt im Log derselben Sitzung: `Sag mir mehr ueber Gmachl` war ebenfalls `OUT-OF-SCOPE`.

---

## II.0 Auftrag an ein späteres Modell

Wenn Hotel-/Urlaubsfragen `out_of_scope` werden, **nicht** den exakten User-String in die TSV kleben. Reihenfolge:

1. Ist die Ablehnung wirklich Intent (`RejectReason = "out_of_scope"`, Log `[Intent] out_of_scope`), nicht Ethical?
2. Enthält der Text Domain-Vokabular (`hotel` als Teilstring, `schi`/`ski`, Piste, Hausname)? Dann muss `HotelDomainCues.Matches` **true** sein und der Service **ohne Bayes** durchlassen.
3. Ist die TSV noch ein Orts×Amenity-Raster (`Ich suche ein Wellnesshotel mit {X} in {Y}` × 12 Orte)? Dann TSV nach II.6 neu bauen, nicht „eine Zeile mehr“.
4. Tests in II.7 müssen grün sein. API-Prozess neu starten (Singleton trainiert im Konstruktor).

**Nicht tun**

- Nicht nur die gemeldete Ski-Frage als `in_scope` eintragen.
- Nicht 12 Orte × 6 Amenities × 30 Sprachen als „mehr Daten = besser“ erzeugen. Das **verursacht** den Bug (unbekannte N-Gramme langer Gesprächssätze ziehen nach `out_of_scope`).
- Nicht auf **unter** 64 Beispiele je Klasse und Sprache kürzen, nur um Duplikate zu streichen. Duplikate streichen, **Sprechweisen** auffüllen.
- Intent nicht zum Ethical-Gate machen (Beleidigung bleibt Ethical).
- `HotelDomainCues` nicht mit Allerweltswörtern füttern (`stock`, `engel`, `spa` als Substring, nacktes `StartsWith("ski")` / `StartsWith("schi")`).
- Den historischen LLM-Call `pipeline.logical_check` nicht still wieder einschalten; die laufende Pipeline ist lokal.

---

## II.1 Was schiefging (Befund)

### II.1.1 Pipeline-Symptom

`src/HotelChatbot.Api/log/YYYY-MM-DD.txt` (Beispiel 2026-08-31):

```
OUT-OF-SCOPE
  Query    : Mich interessiert ob es Hotels gibt, bei denen man mit den Schi bis zum Hoteleingang fahren kann
  Prompt   : classifier.intent
  │ local BinaryTextClassifier (in_scope/out_of_scope)
  Ergebnis : NEIN
```

User-sichtbare Meldung aus `ChatService.RunSafetyGatesAsync` bei `!inScope`:

- de: *Dieser Assistent beantwortet ausschließlich Fragen zu BestWellness-Wellnesshotels. …*
- en: *This assistant exclusively provides information about BestWellness wellness hotels. …*

Suche in `src/HotelChatbot.Application/Services/ChatService.cs` nach `_intentClassifier.IsHotelWellnessQuery` und `"out_of_scope"`.

### II.1.2 Mechanik

`IntentClassifierService` trainiert `BinaryTextClassifier` auf `intent-training.tsv` (`in_scope` vs. `out_of_scope`, Schwelle **0,42**). Features wie Ethical: Zeichen-N-Gramme 1–4 + `w:`-Wort (max. 14 Zeichen), siehe Teil I Abschnitt 4.

Die Positivklasse war ein **einziges Satzmuster**, maschinell über Sprachen und Orte vervielfacht:

```
Ich suche ein Wellnesshotel mit {Sauna|Indoor-Pool|Massagen|Hamam|Infinity-Pool|Yoga} in {Tirol|Südtirol|…|Lermoos}.
```

Typisch 64 Zeilen/Sprache = 5×12 Amenities/Orte + 4 Yoga-Orte (Generator oft abgeschnitten). `out_of_scope` analog: Wetter×8 Städte, Python×14 Themen, „Wer hat X gewonnen“×8, „Erkläre X“×14, Rezept×14, Preis×6.

Zusätzlich Generator-Müll: identische Sprachblöcke mit Suffix ` ·` / ` ·2` (Kroatisch dreifach).

### II.1.3 Warum gerade diese Frage kippt

Der User-Satz **enthält** `Hotels` und `Hoteleingang`. Trotzdem verliert der nackte Bayes, weil:

- Gesprächsfunktionswörter (`interessiert`, `ob`, `denen`, `kann`, `gibt`) in der Positivklasse kaum vorkamen.
- Kompositum `Hoteleingang` als ganzes Wort-Feature unbekannt war (Training nur `Wellnesshotel`).
- `Schi` (AT) fehlte; Sport-N-Gramme lagen eher in `out_of_scope` (Tour, Fußball, Marathon).
- Unbekannte N-Gramme bestrafen die Klasse mit **mehr** Tokenmasse (die repetitive Positivklasse) härter → lange natürliche Sätze kippen nach `out_of_scope`, kurze Template-Klone (`Hotels in Tirol`) nicht.

Dasselbe Muster: `Sag mir mehr ueber Gmachl` (Hausname, kein Wort `hotel`).

Der **intendierte** Scope steht noch im ungenutzten LLM-Prompt `pipeline.logical_check` (`PostgreSQLSystemPromptRepository`): Hotels, Unterkunft, Wellness, Ski, Wandern, Reiten, Kulinarik, Adults-Only, Entspannung; **inklusiv**; NO nur wenn eindeutig kein Bezug. Der lokale Classifier hat das nicht abgebildet.

### II.1.4 Verworfene Zwischenstände (nicht wiederholen)

| Versuch | Ergebnis | Warum verworfen |
|---------|----------|-----------------|
| Nur die Ski-Frage in die TSV | Workaround | User-Verbot; nächste Formulierung fällt wieder |
| TSV auf 6 In + 6 Out je Sprache kürzen (ein Ort, sechs Amenities) | ~438 Samples, 31 Sprachen | User: Vorgabe war **≥64 je Klasse**; Amenities `… mit Sauna/Pool/Hamam in Tirol` bleiben marginale Varianten |
| Ortsraster 12×6 behalten | Tausende Zeilen | Kein Intent-Signal, verschärft N-Gramm-Bias |

---

## II.2 Soll-Architektur (hybrid, analog Ethical)

Ethical: **ohne Insult-Marker → OK** (fail-open), Bayes nur bei Cue.  
Intent: **mit Domain-Cue → in_scope** (fail-open auf Thema), Bayes nur sonst.

In `IntentClassifierService.IsHotelWellnessQuery`:

```
1. HotelDomainCues.Matches(text) == true  → return true
2. sonst nackter Bayes: Label in_scope und p ≥ 0,42
```

Leerer/zu kurzer Text: Cues false, Bayes fail-closed (`out_of_scope`). Das bleibt so.

Schwelle 0,42 **zweimal**: `TrainFromTsv(..., minPositiveProbability: 0.42)` und erneut im Service. Beides beibehalten oder bewusst gemeinsam ändern.

---

## II.3 Dateien

| Rolle | Pfad |
|-------|------|
| Gate-API | `src/HotelChatbot.Domain/Interfaces/IIntentClassifier.cs` — `bool IsHotelWellnessQuery(string text)` |
| Service | `src/HotelChatbot.Infrastructure/Classifiers/IntentClassifierService.cs` |
| Domain-Cues | `src/HotelChatbot.Infrastructure/Classifiers/HotelDomainCues.cs` (neu in Teil-II-Sitzung) |
| Naive Bayes | `src/HotelChatbot.Infrastructure/Classifiers/BinaryTextClassifier.cs` |
| Features | `src/HotelChatbot.Infrastructure/Classifiers/Language/CharNGramExtractor.cs` |
| Training | `src/HotelChatbot.Infrastructure/Classifiers/Data/intent-training.tsv` |
| TSV ins Output | `HotelChatbot.Infrastructure.csproj` und Tests-csproj: `Classifiers/Data/**` → `CopyToOutputDirectory` |
| Locator | `ClassifierDataLocator.Resolve(Path.Combine("Data", "intent-training.tsv"), typeof(IntentClassifierService))` |
| DI | `Program.cs`: `AddSingleton<IIntentClassifier, IntentClassifierService>` |
| Aufruf | `ChatService.RunSafetyGatesAsync` nach Ethical |
| Scope-Definition (historisch, LLM ungenutzt) | Seed `pipeline.logical_check` in `PostgreSQLSystemPromptRepository.cs` |
| Tests | `src/HotelChatbot.Tests/ClassifierSmokeTests.cs` |

TSV: UTF-8, Tab, Kopf `label	text`, Labels nur `in_scope` / `out_of_scope`. Leere Zeilen und `#`-Kommentare ignoriert `TrainFromTsv`.

---

## II.4 `HotelDomainCues` — so nachbauen

Öffentliche Klasse `HotelDomainCues` mit `public static bool Matches(string? text)`.

### II.4.1 Vorverarbeitung

1. Null/Whitespace → `false`.
2. `Fold`: NFD, `ToLowerInvariant`, Combining Marks weg, `U+00DF` (ß) → `ss`. Damit `hôtel` → `hotel`.
3. Wörter = zusammenhängende Buchstaben. Bindestrich trennt: `ski-in` → `ski`, `in`.

### II.4.2 Trefferlogik pro Wort (Länge ≥ 3, außer exaktem `ski`/`schi`)

**A. Substring (`TokenContains`)** — lange, eindeutige Stämme:

`hotel`, `wellness`, `unterkunft`, `хотел` (kyrillisch Hotel), `sauna`, `hamam`, `jacuzzi`, `whirlpool`, `massag`, `kulinar`, `urlaub`, `pauschale`, `piste`, `adults`, `halfboard`, `halbpension`, `ferien`

`hotel` als Substring fängt `hotels`, `hoteleingang`, `wellnesshotel`, `hotell` (sv). Das ist der Fix für die gemeldete Frage **ohne** den Satz zu speichern.

**B. Exakte Tokens** (nicht Substring):

`spa`, `resort`, `albergo`, `lodging`, `accommodation`, `hike`, `hiking`, `reiten`, `reiter`

Niemals `spa` als `Contains` (trifft `Ersparnissen`, `spam`). Niemals `ski` als `Contains` (trifft `olimpijski`, dänisch `skifter`).

**C. Exakt `ski` oder `schi`** (ganzes Wort). Österreichisch *Schi* in der User-Frage ist ein eigenes Token.

**D. Ski-Präfixe** (`StartsWith`), bewusst lang, damit `Schiff` und `Geschichte` nicht matchen:

```
skifahr, skiurlaub, skilift, skipiste, skigebiet, skihotel, skisport, skiin, skiout, skiraum
schifahr, schiurlaub, schilift, schipiste, schigebiet, schihotel, schisport, schiraum
```

Nicht: nacktes `StartsWith("ski")` oder `StartsWith("schi")`. `Geschichte` enthält `schi` als Substring — deshalb nur Exact/langes Präfix, nie `Contains("schi")`.

**E. Outdoor-Präfixe:**

```
wandern, wanderung, wanderweg, wanderhotel
reiten, reithalle, reitstall, reitsport, reiturlaub, reiterhof, reitangebot
```

Nicht Präfix `reit` allein. Nicht Präfix `reiter` (`reiteration`). `reiten`/`reiter` zusätzlich als ExactTokens.

**F. Hausnamen** (Katalog, nur distinktiv; folded):

`gmachl`, `nesslerhof`, `krallerhof`, `alpbacherhof`, `waldklause`, `hochschober`, `wartherhof`, `alpenrose`, `alpenresort`, `uebergossenealm`, `ubergossenealm`

Match: equals **oder** contains (Komposita).

**Nicht** als Cue: `stock`, `engel`, `theresa`, `sacher` (zu generisch / Sachertorte), reine Ortsnamen (`lermoos` — sonst Wetter in Lermoos = in-scope).

### II.4.3 Bewusst keine Cues

`yoga` ohne Hotel. `winter` / `schnee` zu breit. `zimmer` zu breit. `pool` als Exact wäre möglich, aktuell über andere Stämme + TSV abgedeckt.

---

## II.5 `intent-training.tsv` — Soll-Form

### II.5.1 Mengen und Sprachen

- **31 Sprachen**, Reihenfolge der Blöcke:  
  `bg hr cs da de el en es et fi fr ga hu is it lt lv mk mt nl no pl pt ro ru sk sl sq sv tr uk`
- Pro Sprache **genau 64 `in_scope`**, dann **genau 64 `out_of_scope`**, dann nächste Sprache.
- Kein Sprachcode in der TSV. Tests erkennen Blöcke: Folge `in_scope` nach einem `out_of_scope`-Lauf = neuer Block.
- Keine Duplikat-Sprachen (` ·` / ` ·2` verboten).
- Gesamt: 31 × 128 = **3968** Samples plus Kopf/Kommentare.

### II.5.2 Inhaltliche Regel

Die 64 In-Scope-Sätze einer Sprache sind **64 verschiedene Fragen** (andere Sprechhandlung), nicht 64 Slot-Fills.

Verboten:

```
Ég leita að heilsuhótel með gufubaði í Týról.
Ég leita að heilsuhótel með innilaug í Týról.
Cerco un hotel wellness con sauna a Tirolo.
Cerco un hotel wellness con piscina interna a Tirolo.
```

Erlaubt: gleiche Themenfamilie (Ski), aber anderer Satzbau (`Gibt es…`, `Welche Hotels liegen…`, `Kann man mit den Schi…`, `Ich brauche Unterkunft neben dem Lift`).

`out_of_scope`: 64 verschiedene Off-Topic-Fragen (Wetter, Code, Sportresultat, Kochen, Steuern, Hauptstadt, …). Nicht 14× `Erkläre {X} einfach`. **Keine** Hotel-/Wellness-/Ski-/Piste-/Sauna-Wörter in `out_of_scope`.

Orte (Tirol, Salzburg, …) dürfen vorkommen, aber jeweils in **einer** anders gebauten Frage, nicht als einziges Tauschfeld.

### II.5.3 Kanonische 64 In-Scope-Sprechweisen (EN-Master)

Ein späteres Modell übersetzt jede Zeile sinngetreu und muttersprachlich in alle 31 Sprachen. Eigennamen unverändert: Nesslerhof, Gmachl, Krallerhof, Alpbacherhof, Waldklause, Hochschober, Wartherhof, Alpenrose, Sacher, Seefeld, Lermoos, Ötztal, Zillertal, Dolomiten/Dolomites, Innsbruck. Deutsch-Master: TSV-Block `de`, beginnt mit `Ich suche ein Wellnesshotel mit Sauna in Tirol.`

1. Looking for a wellness hotel with a sauna in Tyrol.
2. Are there hotels that have an indoor pool?
3. Which houses offer massages?
4. I want a stay that includes a hammam.
5. Where can I swim in an infinity pool at a hotel?
6. Do you know a hotel that runs yoga sessions?
7. Are there hotels you can ski into and out of?
8. Which hotels lie directly on the piste?
9. Can guests ski as far as the hotel entrance?
10. I need lodging next to a ski lift.
11. We want a winter holiday that mixes spa and skiing.
12. Which properties have a ski room for equipment?
13. Is hiking possible straight from the hotel?
14. Show me hotels with walking paths nearby.
15. Can I go horse riding during my hotel stay?
16. Looking for hotels known for their cuisine in Salzburg.
17. An adults-only hotel with a pool, please.
18. Does half board come with the stay?
19. Tell me more about Nesslerhof.
20. What does Gmachl actually offer?
21. List the facilities at Krallerhof.
22. I would like a room with a mountain view.
23. How do I reach the hotel in Lermoos?
24. Are dogs welcome in the hotels?
25. Do you have family-friendly hotels?
26. Which hotels in Salzburg are in your list?
27. How many hotels do you cover in Tyrol?
28. Is there a spa hotel you would recommend in Carinthia?
29. Recommend a wellness hotel in South Tyrol.
30. I need a quiet house for a proper rest.
31. Which hotel has the biggest spa area?
32. How far is it from the hotel to the slope?
33. We are planning a summer hiking holiday with wellness.
34. A hotel with a serious gourmet restaurant.
35. Is breakfast included in the rate?
36. We need a family room in a wellness hotel.
37. Is there a children's programme at the hotel?
38. Looking for a wheelchair-accessible wellness hotel.
39. When are the evening sauna infusions?
40. Can I reserve a massage in advance?
41. What would you suggest near Innsbruck?
42. I want a full wellness week in Styria.
43. A spa stay in the Dolomites, please.
44. Help me plan a wellness stay in the Ötztal.
45. Tell me about Alpbacherhof.
46. I need information on Waldklause.
47. What makes Hochschober special?
48. Can I arrive at the hotel by train?
49. Is parking available at the hotel?
50. A room with a balcony facing the mountains.
51. We want to combine skiing and wellness in one trip.
52. I am interested whether guests can ski up to the building.
53. Just a short wellness break in the mountains.
54. Anything worth knowing about hotels in the Zillertal?
55. What are the strongest wellness hotels in Austria?
56. Does the hotel have a fitness room?
57. Is there a steam bath on site?
58. Do you have a whirlpool in the spa?
59. Couples spa packages for a weekend.
60. A romantic wellness weekend for two.
61. Where is Wartherhof and what wellness does it have?
62. Details about Hotel Alpenrose.
63. Does Sacher in Seefeld have a spa?
64. How far is the hotel from the village centre?

Deutsch zu (9) in der TSV: `Kann man mit den Schi bis zum Hoteleingang fahren?` — **ähnliche Klasse**, nicht der User-String mit `Mich interessiert ob es Hotels gibt…`.

### II.5.4 Kanonische 64 Out-of-Scope-Sprechweisen (EN-Master)

Kein Hotel/Wellness/Ski/Piste/Sauna/Unterkunft.

1. What will the weather be like in Berlin tomorrow?
2. Write a Python function that sorts a list.
3. Who won the last Tour de France?
4. Explain Windows 11 in simple language.
5. I need a recipe for apple strudel.
6. What is the Bitcoin price today?
7. What is the capital of France?
8. How does photosynthesis work in plants?
9. Who won Wimbledon last year?
10. How do I file an income tax return?
11. How do I tune an acoustic guitar?
12. How much RAM should a video editor have?
13. Help me draft a job application letter.
14. How do I reset a home Wi-Fi router?
15. Who won the 2022 football World Cup?
16. Who won the latest Formula 1 grand prix?
17. Which nation topped the last Olympic medal table?
18. Who won the last Eurovision Song Contest?
19. Who won the New York City Marathon?
20. Who is the current world chess champion?
21. Explain quantum computing to a complete beginner.
22. How can I grow tomatoes on a balcony?
23. How do I make pizza dough from scratch?
24. What time is it in Tokyo right now?
25. How do I change a flat car tire?
26. Recommend a good science-fiction film.
27. What is an efficient way to learn Japanese?
28. Explain what an SQL inner join does.
29. How do I install Linux on a laptop?
30. What led to the fall of the Roman Empire?
31. Please solve this quadratic equation.
32. How do I bake a sourdough loaf?
33. What is the USD to EUR exchange rate today?
34. How does a four-stroke engine work?
35. Who wrote the play Hamlet?
36. How do I create a new Git repository?
37. What are signs of vitamin D deficiency?
38. How should I train for a five-kilometre run?
39. What is the population of Canada?
40. How do I fix a dripping tap?
41. Translate this sentence into Latin.
42. Which laptop is best for computer science students?
43. How do black holes form?
44. When was the first iPhone released?
45. How do I compost vegetable scraps?
46. Teach me the basic rules of chess.
47. How does inflation reduce the value of savings?
48. Name the planets of the solar system in order.
49. How do I remove malware from a Windows PC?
50. Why does water boil at a lower temperature in the mountains?
51. How do I crochet a simple scarf?
52. Summarise the plot of Nineteen Eighty-Four.
53. How is compound interest calculated?
54. What is the difference between HTTP and HTTPS?
55. How do I start a small vegetable garden?
56. Who painted the Mona Lisa?
57. How do I replace a bicycle chain?
58. Describe machine learning in one short paragraph.
59. How do I apply for a passport?
60. Why is the sky blue during the day?
61. How do I connect a printer with a USB cable?
62. What ingredients go into béchamel sauce?
63. How long is a flight from Vienna to New York?
64. Explain the water cycle to a child.

Deutsch zu (7): `Was ist die Hauptstadt von Frankreich?` (bestehender Test). Deutsch zu (1): `Wie wird das Wetter morgen in Berlin?`

### II.5.5 Diversitäts-Check (in Tests und beim Regenerieren)

Paarweise in einem Sprach-`in_scope`-Block: `LCP` = Länge des gemeinsamen Präfixes. Fail wenn `LCP ≥ 40` Zeichen **und** `LCP / min(len(a),len(b)) ≥ 0,55`. Trifft Amenity-Swaps, lässt kurze gemeinsame Fragewörter (`Y a-t-il`, `Gibt es`) zu.

Alle 64 Texte je Klasse **unique** (nach Trim).

---

## II.6 Umsetzungsschritte (Klon / anderes Produkt)

1. `HotelDomainCues.cs` wie II.4; Katalog-Namen an **dieses** Produkt anpassen.
2. `IntentClassifierService.IsHotelWellnessQuery`: Cue zuerst, dann Bayes 0,42.
3. TSV neu: 31 Sprachen × (64 In + 64 Out) nach II.5; Übersetzungen **nicht** per Slot-Fill-Generator.
4. `CopyToOutputDirectory` für die TSV in Infrastructure- und Test-Projekt.
5. Tests II.7.
6. Befehl: `dotnet test src/HotelChatbot.Tests/HotelChatbot.Tests.csproj --filter ClassifierSmokeTests`
7. Laufenden Host neu starten (`./start` / Kestrel). Classifier ist Singleton.

---

## II.7 Tests (müssen nach jeder Intent-Änderung gelten)

Datei: `src/HotelChatbot.Tests/ClassifierSmokeTests.cs`

**Nackter Bayes** (ohne Cues):

- `IntentClassifier_AcceptsHotelQuery_RejectsOffTopic`: `Ich suche ein Wellnesshotel mit Sauna in Tirol.` → positive; `Was ist die Hauptstadt von Frankreich?` → nicht.
- `IntentClassifier_FailClosed_OnTooShortText`: `""`, `"a"`.

**Service (Cues + Bayes)** — `IntentClassifierService_AcceptsHotelAndVacationQueries`, mindestens:

- `Ich suche ein Wellnesshotel mit Sauna in Tirol.`
- `Mich interessiert ob es Hotels gibt, bei denen man mit den Schi bis zum Hoteleingang fahren kann` (die gemeldete Formulierung)
- `Gibt es Hotels mit Pistenanschluss?`
- `Welche Hotels liegen direkt an der Skipiste?`
- `Are there ski-in ski-out hotels?`
- `Kann man in euren Hotels wandern gehen?`
- `Gibt es Reitangebote im Hotel?`
- `Y a-t-il des hôtels avec ski aux pieds?` (`hôtel` nach Fold)
- `Sag mir mehr über Gmachl`
- `Was bietet der Krallerhof?`
- `Adults Only Hotel mit Pool`
- `Hotels in Tirol`

**Service reject** — `IntentClassifierService_RejectsOffTopic`:

- `Was ist die Hauptstadt von Frankreich?`
- `Wie wird das Wetter morgen in Berlin?`
- `Schreib eine Python-Funktion für Sortieren.`
- `Erkläre Photosynthese einfach.`
- `Who won Wimbledon last year?`
- `Bitcoin price today?`

**Cues isoliert** — `HotelDomainCues_MatchCompoundsAndDialectNotExactQuestion`:

- true: `Hoteleingang an der Piste`, `Schifahren im Winter`, `ski-in ski-out`, `hôtels au pied des pistes`
- false: Wetter-Berlin, Photosynthese, `Geschichte der Fotografie` (`geschichte` darf **nicht** über Substring `schi` matchen)

**TSV-Struktur:**

- `IntentTraining_EachLanguageHasAtLeast64InAnd64Out` — ≥30 Blöcke, je ≥64 In und ≥64 Out.
- `IntentTraining_NoAmenityLocationTemplateSwaps` — LCP-Regel II.5.5 auf jedem In-Block.

Parser: `LoadLanguageBlocks` in derselben Testdatei (Flush wenn `in_scope` auf `out_of_scope` folgt).

---

## II.8 Diagnose, wenn es wieder passiert

1. Log: `[Intent] out_of_scope` vs. `[Ethical] REJECT`. Falsches Gate → Teil I vs. Teil II.
2. `HotelDomainCues.Matches(exakter User-String)` in einem Test. False trotz `hotel`/`schi` → Fold/Tokenisierung (Kompositum, Bindestrich, Diakritik).
3. Läuft ein alter Prozess? TSV/DLL nicht im Output? Locator-Kandidaten in `ClassifierDataLocator` prüfen.
4. TSV: wieder 12 Orte × gleiche Präfixe? Test `NoAmenityLocationTemplateSwaps` muss rot werden — Test nicht aufweichen.
5. Neue Hausnamen: Cue-Liste erweitern (distinktiv) **und** eine der 64 Sprechweisen (19–21, 45–47, 61–63) analog übersetzen, nicht 12 Ortsvarianten.

---

## II.9 Kurzregeln

| Irrtum | Korrekt |
|--------|---------|
| Ski-Zugang ist Off-Topic, weil Sport in `out_of_scope` steht | Nein. Ski/Piste/Wandern/Reiten sind Hotelurlaub. Sport**resultate** (Tour, WM) sind out. |
| `Hotels` im Satz reicht dem Bayes immer | Nein. Lange unbekannte Kontexte ertränken das Signal. Deshalb Domain-Cues. |
| Mehr Template-Zeilen machen den Bayes robuster | Nein. Homogene Masse + Unseen-Penalty. |
| 6 Beispiele/Sprache genügen, wenn Cues existieren | Nicht hier: Vorgabe ≥64 je Klasse, und 6 Amenity-Klone sind trotzdem Schablone. |
| Ethical und Intent sind austauschbar | Nein. Toxizität vs. Thema. |

---

# Teil III — Gesprächskontext: Verlauf muss in Retrieval und Katalog einfließen

**Anlass (User-Dialog, Sim-UI):**

1. `Welche Hotels kennst du in Tirol?` → Katalog listete **alle** Hotels (auch Kärnten/Salzburg), nicht nur Tirol.
2. `Hochschober` → Freitext-Antwort zum Hotel (OK inhaltlich).
3. `Wieviele Zimmer gibt es dort?` → Bot blieb bei Hochschober (History am Answer-LLM wirkte), fand aber keine Zimmerzahl.
4. `Zu welchen Hotels hast du Informationen ueber die Zimmeranzahl?` → Quellen/Vorschläge u. a. Engel, Schwarz, Gmachl **ohne** Bindung an Tirol / vorherigen Fokus.

**Sitzung Teil III (31.08.2026):** Befund + Code. System-Prompts in der DB wurden mitgeprüft und soft-upgegradet.

Verwandt: `documentation/language-detection.md` (`sessionId` muss über Turns mitgegeben werden — sonst gibt es keinen Verlauf).

---

## III.0 Auftrag an ein späteres Modell

Wenn Folgefragen den Regions- oder Hotel-Kontext „vergessen“, **nicht** nur den Answer-Prompt härter machen. Reihenfolge:

1. Kommt `sessionId` vom Client (MCP/Sim) in `/api/chat/recommend` an? Ohne Session gibt es keine History und keine Constraints.
2. Geht die History an den **Answer-LLM** (`BuildConversationHistory`), aber die **Vektor-Query** nur aus der aktuellen User-Zeile? Das war der Hauptbug.
3. Ist die Frage ein Broad-Catalog (`IsBroadCatalogQuery`) mit Region im Text, und der Katalog lädt trotzdem `GetAllAsync` ohne Filter? Zweites Symptom.
4. Steht in `pipeline.answer` nur „nur Hotels aus den aktuellen DB-Treffern“, ohne dass Retrieval die Region injiziert? Dann „repariert“ der Prompt den Drift nicht — er zementiert ihn.

Dann nach III.6 umsetzen. Tests in III.7 müssen grün sein. API neu starten (laufender Prozess sperrt DLLs).

**Nicht tun**

- Nicht annehmen „History ist kaputt“, nur weil die Antwort Hotels außerhalb der Region nennt. Oft ist History am LLM ok, Retrieval driftet.
- Nicht nur `pipeline.answer` ändern und Retrieval lassen. Prompt allein reicht nicht.
- Nicht alle Katalog-Hotel-IDs als `__recommended_hotels__` speichern (Follow-up sucht dann 3 zufällige Katalog-Hotels).
- Nicht bei Deixis („dort“) die Session-Region in die Vektor-Query hängen, wenn das Fokus-Hotel außerhalb der Region liegt (Hochschober = Kärnten nach Tirol-Frage).
- System-Prompts nicht mit `ON CONFLICT DO NOTHING` „aktualisieren“ und erwarten, dass die DB den neuen Text bekommt — Soft-Upgrade oder Admin nötig (III.5).

---

## III.1 Was schiefging (Befund)

### III.1.1 Architektur-Spalt (Kern)

In `ChatService.ProcessRecommendationAsync` (Stand vor Fix):

| Stufe | Verlauf genutzt? |
|-------|------------------|
| Session laden + `BuildConversationHistory` (letzte 10 User/Assistant) | ja → Answer-LLM |
| `__recommended_hotels__` → `SearchAsync` pro Hotel (Take 3) | teilweise, aber nach Katalog = Zufalls-Top-3 aller Hotels |
| Vektor-Query (`queryForSearch`) | **nein** — nur `request.Requirements` (bzw. Translate) |
| Katalog `ProcessCatalogListingAsync` | **nein** — alle aktiven Hotels, Region im Text ignoriert |
| `pipeline.answer` / `PipelineContextWrapper` | streng: nur Hotels aus **aktuellen** DB-Treffern nennen |

Folge: User fragt Tirol → Katalog zeigt alles → User sagt „Hochschober“ → Answer kennt Verlauf → User fragt „Zimmeranzahl bei welchen Hotels?“ → Suche ohne „Tirol“ → Treffer aus ganz AT → Prompt erlaubt nur diese Treffer → Antwort driftet.

Dass „dort“ noch Hochschober meinte, beweist: **History am LLM funktioniert**, wenn `sessionId` gesetzt ist. Der Bug ist **Retrieval-Grounding**, nicht „Session speichert nichts“.

### III.1.2 System-Prompts (DB)

Seed: `PostgreSQLSystemPromptRepository.GetDefaultPrompts()`, Key `pipeline.answer`.

- Insert: `ON CONFLICT (key) DO NOTHING` — bestehende Admin-/Seed-Zeilen werden **nicht** überschrieben.
- Prompt sagte (EN/DE): nur Hotels aus den Datenbankergebnissen nennen; Allgemeinwissen nur *über* diese Hotels.
- Kein Hinweis auf Regions-/Fokus-Constraints aus dem Chat-Verlauf.

`PipelineContextWrapper` (Hardcoded in `ChatService`, immer am Answer-Call) wiederholte RULE 1/2 ohne Conversation-Grounding.

Fazit für ein späteres Modell: Prompt-Anpassung ist **Ergänzung**, nicht Ersatz für Query-Enrichment + Regionsfilter. Soft-Upgrade beim Seed nötig (III.5), sonst bleibt die DB auf altem Text.

### III.1.3 Katalog speichert alle IDs

Vorher: `SaveInteractionAsync(..., shuffled.Select(h => h.HotelId).ToList())` nach Katalog.

`LoadRecommendedHotelIds` + `previousHotelIds.Take(3)` → Follow-up-Detail-Suche auf die **ersten drei** der gespeicherten Liste (Katalog-Shuffle), nicht auf das später genannte Hotel.

### III.1.4 Sim / MCP

`chatgpt/public/sim/index.html` hält `sessionId` aus dem Tool-Result und sendet ihn mit. Ohne das wäre jeder Turn eine neue Session — dann gäbe es weder History noch Constraints. Bei Diagnose zuerst Trace-Step `Session` / Response-Feld `sessionId` prüfen.

---

## III.2 Soll-Architektur

Zwei getrennte Kanäle:

1. **Answer-LLM:** unverändert History (`BuildConversationHistory`) + DB-Kontext der **bereits gefilterten/angereicherten** Suche.
2. **Retrieval:** explizite Session-Constraints (`Region`, `FocusHotelId`/`FocusHotelName`), die
   - aus User-Text extrahiert,
   - in der Session als System-Message persistiert,
   - in die Vektor-Query gemischt,
   - Katalog und Trefferliste filtern,
   - Follow-up-Hotel-IDs priorisieren.

```
User-Text + sessionId
  → Safety Gates (Language / Ethical / Intent)     [Teil I–II, language-detection.md]
  → Load/Merge ConversationConstraints
  → Broad-Catalog?
       ja  → FilterByRegion (+ optional Vector-Ergänzung) → Antwort → Constraints speichern
            (keine __recommended_hotels__ für den ganzen Katalog)
       nein → Translate (falls nicht de)
            → EnrichSearchQuery(constraints)
            → SearchAllHotels + FollowUpSearch(focus zuerst, dann last recommendations)
            → optional RegionFilter auf validResults
            → Relevance / Answer (History + PipelineContextWrapper inkl. RULE 3)
            → Focus aktualisieren wenn 1 Hotel / Fokus in Recommendations
```

---

## III.3 Dateien

| Rolle | Pfad |
|-------|------|
| Constraint-Modell + Helper | `src/HotelChatbot.Application/Services/ConversationConstraints.cs` (**neu**) |
| Pipeline-Orchestrierung | `src/HotelChatbot.Application/Services/ChatService.cs` |
| Katalog-Erkennung / Antworttext | `src/HotelChatbot.Application/Services/RecommendationPresentation.cs` — `IsBroadCatalogQuery`, `BuildCatalogAnswer(..., regionFilter)` |
| Answer-Prompt Seed + Soft-Upgrade | `src/HotelChatbot.Infrastructure/Repositories/PostgreSQLSystemPromptRepository.cs` — `pipeline.answer`, `EnsurePromptFragmentAsync` |
| Context-Wrapper / Fallback-Prompt | `ChatService` Konstanten `PipelineContextWrapper`, `FallbackAnswerPrompt` |
| History-Rollen → OpenAI | `src/HotelChatbot.Infrastructure/Services/OpenAIChatCompletionService.cs` — `role.ToLowerInvariant()` |
| Session-Persistenz | `ChatSession.Messages` JSONB; Prefixes siehe III.4.4 |
| Tests | `src/HotelChatbot.Tests/ConversationConstraintTests.cs` (**neu**); Katalog-Detection weiter in `RecommendationPresentationTests.cs` |
| Sim (sessionId) | `chatgpt/public/sim/index.html`, MCP `get_response` Args `sessionId` |

---

## III.4 `ConversationConstraints` — so nachbauen

Namespace: `HotelChatbot.Application.Services`.

### III.4.1 Modell

```csharp
public sealed class ConversationConstraints
{
    public string? Region { get; set; }
    public string? FocusHotelId { get; set; }
    public string? FocusHotelName { get; set; }
    public bool HasRegion => !string.IsNullOrWhiteSpace(Region);
    public bool HasFocus => !string.IsNullOrWhiteSpace(FocusHotelId);
}
```

### III.4.2 Regionen

Statische Liste `(Canonical, Aliases[])` mit Word-Boundary-Match (`\b…\b`, IgnoreCase). Mindestens:

| Canonical | Aliase (Beispiele) |
|-----------|-------------------|
| Tirol | tirol, tyrol, tyrolen |
| Salzburg | salzburg, salzburger land, salzburgerland |
| Kärnten | kärnten, karnten, carinthia, carinthien |
| Steiermark | steiermark, styria |
| Vorarlberg | vorarlberg |
| Oberösterreich / Niederösterreich | inkl. ASCII-Varianten |
| Wien | wien, vienna |
| Bayern | bayern, bavaria, bayerischen alpen |
| Südtirol | südtirol, sudtirol, south tyrol, alto adige |
| Trentino | trentino |

`ExtractRegion(text)` → erster Treffer als Canonical, sonst `null`.

`HotelMatchesRegion(hotel, region)`: Substring-Contains (IgnoreCase) in `Region`, `Location`, `Country`, `Domain`, `HotelId`. So matcht `hotel_engel-tirol_com` auch ohne gefülltes Region-Feld.

`FilterByRegion(hotels, region)`: aktive Hotels mit `HotelMatchesRegion`.

### III.4.3 Fokus-Hotel / Deixis / Multi-Hotel

- `MatchSingleHotelByName(text, hotels)`: Treffer über `HotelId`, voller `Name`, oder Kurzname ohne Präfix `Hotel ` (Word-Boundary, Kurzname ≥ 4 Zeichen). Bei mehreren Matches den **eindeutig längeren** Namen wählen, sonst `null`.
- `IsDeicticHotelReference`: Regex auf u. a. `dort`, `dazu`, `davon`, `darüber`/`darueber`, `dieses hotel`, `diesem hotel`, `dem hotel`, `that hotel`, `this hotel`, `there`, `the hotel`.
- `IsMultiHotelQuestion`: u. a. `zu welchen hotels`, `welche hotels`, `in welchen hotels`, `which hotels`, `what hotels`, `among … hotels`.

`Merge(existing, userMessage, hotels)`:

1. `Region` aus Message überschreibt, sonst Session behalten.
2. Namensmatch → Focus setzen.
3. Sonst wenn Multi-Hotel **und** nicht Deixis → Focus löschen (Region behalten). Beispiel: „Zu welchen Hotels hast du Infos zur Zimmeranzahl?“ nach Hochschober-Fokus.

### III.4.4 Query-Enrichment

`EnrichSearchQuery(query, constraints)`:

1. Bei Deixis + Focus: Fokus-Namen **vor** die Query setzen, falls noch nicht enthalten.
2. Region **nur anhängen**, wenn **nicht** (Deixis ∧ Focus). Sonst driftet „Zimmer dort?“ (Hochschober/Kärnten) mit angehängtem „Tirol“.
3. Region anhängen, wenn noch nicht in der zusammengesetzten Query (IgnoreCase).

### III.4.5 Wann Treffer nach Region filtern

`ShouldFilterResultsByRegion(constraints, userMessage)`:

- `false` ohne Region.
- `false` bei Focus + Deixis.
- `false` wenn Message den `FocusHotelName` enthält.
- sonst `true` (z. B. Multi-Hotel-Follow-up mit Session-Region Tirol).

In `ChatService` nach dem Filter: Fokus-Hotel **wieder einfügen**, falls es wegen Region rausgefallen wäre (Defensive).

### III.4.6 Session-Persistenz

Prefix: `__conversation_constraints__:` + JSON (`Serialize`/`Deserialize`).

- Rolle: `MessageRole.System`.
- Speichern: alte Prefix-Messages entfernen, eine neue schreiben, `UpdateAsync`.
- Laden: neueste Message mit Prefix.

Andere Prefixes (nicht vermischen):

| Prefix | Zweck |
|--------|--------|
| `__conversation_constraints__:` | Region + Focus |
| `__recommended_hotels__:` | JSON-Array Hotel-IDs der letzten **nicht-Katalog**-Empfehlung |
| `__pending_sources__:` | Weitere Quellen (eigenes Feature) |

---

## III.5 ChatService-Anbindung (konkret)

Nach Safety Gates, vor Catalog/Search:

1. `allActiveHotels = GetAllAsync` → aktiv.
2. `constraints = Merge(LoadConversationConstraints(session), requirements, allActiveHotels)`.
3. `SaveConversationConstraintsAsync`.
4. Trace-Steps: `Constraints`, bei Änderung `QueryEnrich`, `RegionFilter`.

**Katalog** (`ProcessCatalogListingAsync`):

1. Region = `constraints.Region` ?? `ExtractRegion(requirements)`.
2. `FilterByRegion`.
3. Wenn Region gesetzt und Metadaten lückenhaft: optional `SearchAllHotelsAsync(region, …)` und Hotels mit **leerem** Region+Location **oder** `HotelMatchesRegion` ergänzen — **nicht** Hotels mit widersprechender Region (Kärnten) nur wegen Vektor-Score in den Tirol-Katalog ziehen, außer Meta leer.
4. `BuildCatalogAnswer(shuffled, language, regionFilter)` — Texte „Hotels in {Region}“ bzw. leer-Hinweis.
5. `SaveInteractionAsync(..., recommendedHotelIds: null)` — **kein** Katalog-ID-Dump.

**Search:**

1. Roh-Query (DE original / sonst Translate).
2. `queryForSearch = EnrichSearchQuery(...)`.
3. `SearchAllHotelsAsync`.
4. `BuildFollowUpHotelIds`: zuerst `FocusHotelId`, dann `previousHotelIds`, max 3, dedupe → `SearchAsync` pro ID.
5. `validResults` + optional RegionFilter.
6. Nach erfolgreicher Answer: wenn genau 1 Recommendation → Focus auf dieses Hotel; Constraints speichern. `recommendedIds` wie bisher für Non-Catalog.

**Answer-Kontext:**

`PipelineContextWrapper` um RULE 3 erweitern (Conversation grounding: Region, Fokus, Deixis).  
`FallbackAnswerPrompt` analog `CONVERSATION GROUNDING`.

**DB-Prompt `pipeline.answer`:**

- Seed `Content` / `ContentDe` enthalten Abschnitte `CONVERSATION GROUNDING` bzw. `GESPRÄCHSKONTEXT` (siehe Repository).
- Nach dem normalen Seed: `EnsurePromptFragmentAsync` — wenn Marker in `content`/`content_de` fehlt, **Fragment anhängen** (nicht ganze Prompt ersetzen, Admin-Edits schonen). Marker: `CONVERSATION GROUNDING` / `GESPRÄCHSKONTEXT`.

**OpenAI History:** `role.ToLowerInvariant()` vor Switch `user`/`assistant`, damit falsch kapitalisierte Rollen nicht als System landen.

---

## III.6 Umsetzungsschritte (Klon / anderes Produkt)

1. `ConversationConstraints.cs` wie III.4 anlegen; Regionen/Aliase an Zielmarkt anpassen.
2. `RecommendationPresentation.BuildCatalogAnswer` um optionalen `regionFilter` und Leer-Fall erweitern. `IsBroadCatalogQuery` darf Region **weiterhin** als Katalog behandeln (Filter kommt danach) — „Kennst du Hotels in Salzburg?“ bleibt `true` für Broad-Catalog.
3. `ChatService.ProcessRecommendationAsync` Constraints-Pfad + Catalog/Search wie III.5.
4. `PipelineContextWrapper` + Fallback-Answer + Seed `pipeline.answer` + `EnsurePromptFragmentAsync`.
5. Tests III.7.
6. `dotnet test … --filter "FullyQualifiedName~ConversationConstraint|FullyQualifiedName~RecommendationPresentation"`.
7. Host neu starten (`./start`). Ohne Neustart bleibt alter Code im gesperrten API-Prozess.
8. Manuell in `/sim`: Dialog aus III.Anlass; Trace muss `Constraints`, ggf. `QueryEnrich` / `RegionFilter` zeigen; `sessionId` darf nicht pro Turn neu sein.

---

## III.7 Tests (müssen nach Constraint-Änderungen gelten)

Datei: `src/HotelChatbot.Tests/ConversationConstraintTests.cs`

Mindestens:

| Test | Erwartung |
|------|-----------|
| `ExtractRegion_FindsCanonical` | Tirol/Salzburg/Carinthia→Kärnten; ohne Ort → null |
| `FilterByRegion_KeepsMatchingHotels` | Sample mit Tirol/Kärnten/Salzburg → nur Tirol |
| `MatchSingleHotelByName_Hochschober` | `"Hochschober"` → `hotel_hochschober_com` |
| `EnrichSearchQuery_AddsFocusOnDeixis_SkipsRegion` | „Wieviele Zimmer gibt es dort?“ + Focus Hochschober + Region Tirol → enthält Hochschober, **nicht** Tirol |
| `EnrichSearchQuery_AddsRegionOnMultiHotelFollowUp` | Zimmeranzahl-Multi-Frage + Region Tirol → enthält Tirol |
| `Merge_MultiHotelQuestion_ClearsFocus_KeepsRegion` | Focus weg, Region Tirol |
| `ShouldFilterResultsByRegion_FalseForDeixis` | Deixis false; Multi-Hotel true |
| `IsDeicticHotelReference_Dort` | dort true; Katalog-Frage false |
| `BuildCatalogAnswer_WithRegion` | Text „in Tirol“, kein Hochschober (Kärnten-Sample) |

`RecommendationPresentationTests.BroadCatalogQuery_Detection`: `"Kennst du Hotels in Salzburg?"` bleibt **true** (Catalog-Pfad); Regionfilter ist Sache von Constraints, nicht von `HasStrongFilters`.

---

## III.8 Diagnose, wenn es wieder passiert

1. Trace/Log: fehlt `sessionId`? Dann Client/MCP, nicht Retrieval-Helper.
2. Trace `Constraints`: ist `region` / `focusHotelId` nach Turn 1 bzw. „Hochschober“ gesetzt?
3. Trace `vectorQuery` / `QueryEnrich`: hängt bei Multi-Hotel-Follow-up die Region? Bei „dort“ der Hotelname **ohne** falsche Region?
4. Trace `RegionFilter`: `before` → `after` — greift der Filter? Wenn `after == before` und Region gesetzt: Metadaten (`Hotel.Region`/`Location`) prüfen; Domain/HotelId-Contains reicht nicht für alle Häuser.
5. Katalog zeigt Hotels außerhalb der Region: `FilterByRegion` + Vector-Ergänzung zu permissiv (Meta leer + hoher Score)? Oder alter API-Prozess?
6. Answer nennt Hotels außerhalb trotz Filter: Prompt/LLM ignoriert Trefferliste — dann `validResults` vor Answer prüfen (kommen Fremd-Hotels noch rein?).
7. DB-Prompt ohne Grounding: `SELECT content FROM system_prompts WHERE key = 'pipeline.answer'` — fehlt Marker? API einmal starten (EnsurePromptFragment) oder Admin.

---

## III.9 Kurzregeln

| Irrtum | Korrekt |
|--------|---------|
| Verlauf fließt nicht ein, weil Session kaputt ist | Oft fließt er in den Answer-LLM; die **Suche** ignorierte ihn. |
| Strengerer Answer-Prompt allein fixen Regions-Drift | Nein. Retrieval muss Region/Fokus tragen; Prompt nur absichern. |
| Katalog mit „in Tirol“ = alle Hotels listen ist ok | Nein. Catalog-Pfad muss Region filtern (Metadaten ± Vector-Hilfe). |
| Alle Katalog-IDs als recommended speichern | Nein. Follow-up priorisiert Focus + letzte echte Empfehlungen. |
| Deixis-Query immer um Session-Region ergänzen | Nein. Fokus-Hotel kann außerhalb der früheren Region liegen. |
| Seed `ON CONFLICT DO NOTHING` aktualisiert Prompts | Nein. Fragment-Upgrade oder manueller Admin-Update. |

**Robustheit** = persistierte Constraints + Query-Enrichment + Katalog-/Trefferfilter + Prompt-Grounding + gültige `sessionId`-Kette — nicht nur längere History am LLM.

---

# Teil IV — Sprache nach jeder Eingabe: Klassifizierer, Nachfrage, Sticky, Fallback

**Anlass (User-Vorgabe, 31.08.2026):**

Da die Spracherkennung ein **lokaler Klassifizierer** ist (kein LLM), soll die Sprache **nach jeder User-Eingabe** geprüft werden, nicht nur nach der ersten.

Explizite Regeln:

1. Sätze wie `Hotels in Salzburg` sind in mehreren Sprachen gültig (Deutsch, Englisch, Flämisch/Niederländisch). Liegen beim **ersten Satz** zwei oder mehr Sprachen knapp beieinander → das System **fragt**, welche Sprache es sprechen soll — **jeweils in genau diesen Sprachen**.
2. War die Sprache beim ersten Satz **eindeutig** und ist ihre Wahrscheinlichkeit in einem Folgesatz **hoch, aber nicht die höchste** → trotzdem die Sprache des ersten Satzes verwenden.
3. Fallback ist immer **Deutsch**.
4. Verwendet der User eine Sprache, die nicht Deutsch ist und **nicht erkannt** wird (z. B. Japanisch) → das System antwortet auf **Englisch**.

**Sitzung Teil IV (31.08.2026):** Domain-DTO, Policy, Pipeline, Script-Filter, Tests, MCP-`sessionId`. Kurzfassung: `documentation/language-detection.md`.

---

## IV.0 Auftrag an ein späteres Modell

Wenn die Antwort-Sprache „am ersten Satz klebt“, obwohl der User gewechselt hat, **oder** bei `Hotels in Salzburg` stillschweigend Niederländisch/Englisch gewählt wird, **oder** Japanisch mit Deutsch beantwortet wird: nicht den LLM-Language-Agent reaktivieren. Reihenfolge:

1. Läuft `ILanguageDetector.Classify` **in jedem Turn** (nicht `Detect` + Client-Override)?
2. Wird `request.Language` / MCP-`language` noch als harter Skip genutzt? Das **muss weg**.
3. Wird Session-Zustand `__conversation_language__:` geladen/gespeichert? Ohne das gibt es kein Sticky und keine Pending-Query.
4. Kommt `sessionId` vom Client (MCP `get_response` / Sim) wieder mit? Sonst ist jeder Turn ein „erster Satz“.
5. Wird `Hotels in Salzburg` nur über Softmax entschieden? Softmax über ~45 Klassen **peakt** (gemessen: `nl:0,97` / `de:0,03`). Close-Margin auf Softmax reicht **nicht**. Es braucht `IsSharedVocabularyPhrase` (Polyglot-Wortliste) **zusätzlich**.

Dann nach IV.8 umsetzen. Tests in IV.9 müssen grün sein. Host neu starten (Language-Classifier ist Singleton).

**Nicht tun**

- Nicht `request.Language` als Override behalten „weil ChatGPT die Sprache schon kennt“. Das war der Grund, warum nur der erste Satz zählte.
- Nicht nur den Top-Code des Klassifizierers nehmen und Fallback `de` bei `IsUnknown`. Dann wird `Hotels in Salzburg` zu `nl` oder `de`, ohne Nachfrage.
- Nicht Softmax-Nähe allein als Mehrdeutigkeit verwenden. Der Klassifizierer ist auf Kurzphrasen mit Lehnwörtern (`Hotels`, `in`, Ortsname) überconfident.
- Nicht Japanisch/CJK als Fallback Deutsch behandeln (`Detect(..., fallback: "de")` gilt nur für **Crawler**).
- Nicht die Pending-Query verwerfen: nach „Welche Sprache?“ muss `Deutsch` die **ursprüngliche** Hotelanfrage suchen, nicht das Wort „Deutsch“.
- Nicht den historischen LLM-Prompt `pipeline.language_detect` wieder einschalten.
- `HardReject`/`Intent` nicht mit Sprache vermischen. Language sitzt **vor** Ethical/Intent.
- Ethical-/Intent-Reject-Texte bleiben de vs. en (bestehend); die Nachfrage selbst ist mehrsprachig.

---

## IV.1 Was schiefging / was gebaut werden musste

### IV.1.1 Vorher

`RunSafetyGatesAsync(text, languageOverride, …)`:

- Wenn `languageOverride` gesetzt → **kein** Klassifizierer.
- Sonst `_languageDetector.Detect(text)` → ein ISO-Code, bei Unsicherheit immer `de`.
- Kein Session-Lock, keine Nachfrage, kein Japanisch→EN.

LLM-Language-Detect war bereits durch `LanguageDetectionService` + `LanguageClassifier` ersetzt (billig). Die Pipeline nutzte das nicht voll: sie behandelte Sprache wie einen einmaligen LLM-Call.

### IV.1.2 Softmax-Falle (Pflichtwissen)

Repro mit trainiertem `LanguageClassifier`:

```
Classify("Hotels in Salzburg") → nl ≈ 0,97, de ≈ 0,03
```

Wort-Coverage-Sharing im Klassifizierer (N-Gramme in mehreren Klassen) hat das **nicht** gefixt: nur Niederländisch hatte hohe Wort-Treffer. Deshalb sitzt die Mehrdeutigkeit in der **Policy** (`IsSharedVocabularyPhrase`), nicht in einer Close-Margin auf Softmax.

Ein späteres Modell, das nur `top.Probability - second < 0.04` nachbaut, **verfehlt die User-Regel**.

### IV.1.3 Session

Sticky und Nachfrage brauchen denselben `sessionId`-Kanal wie Teil III. MCP `get_response` muss `sessionId` aus dem vorherigen Tool-Result wieder hereingeben. Sim (`chatgpt/public/sim/index.html`) tut das bereits.

---

## IV.2 Soll-Architektur

Zwei Schichten, nicht vermischen:

1. **Klassifizierer** (Infrastructure): Rangfolge, `IsAmbiguous` (Softmax-Margin), `IsUnrecognizedScript` (Schrift), `IsUnknown`. Crawler nutzen weiter `Detect()` → Fallback `de`.
2. **Gesprächsregeln** (Application): `ConversationLanguagePolicy.Resolve(detection, sessionState, userText)` → Sprache **dieser** Antwort, ggf. Nachfrage, neuer Session-Zustand, `TextToProcess` (Pending-Query).

```
User-Text + sessionId
  → Classify(text)                         # immer, kein Override
  → LoadConversationLanguage(session)
  → ConversationLanguagePolicy.Resolve
  → SaveConversationLanguage
  → NeedsClarification?
       ja  → ResponseType language_clarify, Success=true, STOP
             (Ethical/Intent nicht auf „Deutsch“ als Hotel-Query)
       nein → Ethical(queryText) → Intent(queryText)
            → Translate/Search/Answer in decision.Language
            → queryText = TextToProcess (Pending-Query nach Sprachwahl)
```

`Source`-Werte der Decision (Logging/Trace-Meta): `clarify`, `detected`, `sticky`, `switched`, `choice`, `fallback_de`, `unrecognized_en`.

---

## IV.3 Dateien

| Rolle | Pfad |
|-------|------|
| Scores-DTO | `src/HotelChatbot.Domain/Language/LanguageDetectionDetails.cs` — `LanguageScore`, `LanguageDetectionDetails` |
| Detector-API | `src/HotelChatbot.Domain/Interfaces/ILanguageDetector.cs` — `Detect` **und** `Classify` |
| Klassifizierer | `src/HotelChatbot.Infrastructure/Classifiers/Language/LanguageClassifier.cs` |
| Schriftfilter | `…/Language/ScriptFilter.cs` — `IsUnsupportedScript` |
| Service | `src/HotelChatbot.Infrastructure/Classifiers/LanguageDetectionService.cs` |
| Policy | `src/HotelChatbot.Application/Services/ConversationLanguagePolicy.cs` |
| Nachfrage-Texte / Aliase | `src/HotelChatbot.Application/Services/LanguageClarification.cs` |
| Pipeline | `ChatService.RunSafetyGatesAsync(text, session, …)` — **kein** `languageOverride` |
| Session-Prefix | `__conversation_language__:` (nicht mit Constraints-Prefix vermischen) |
| MCP | `chatgpt/index.js` — `get_response` Input `sessionId`, `executeTool` reicht `SessionId` durch |
| Tests | `src/HotelChatbot.Tests/ConversationLanguagePolicyTests.cs` |
| Kurz-Doku | `documentation/language-detection.md` |

`ILanguageDetector` bleibt in Domain. Application darf **nicht** auf `LanguageClassifier` zugreifen; nur auf `LanguageDetectionDetails`.

---

## IV.4 Klassifizierer und Schrift — so nachbauen

### IV.4.1 `ILanguageDetector`

```csharp
string Detect(string text, string fallback = "de");
LanguageDetectionDetails Classify(string text);
```

`Detect` (Crawler, `CrawlerTextUtils`): `Classify` → wenn `IsUnknown` **oder** `IsAmbiguous` **oder** `IsUnrecognizedScript` **oder** kein `TopCode` → `fallback` (Default `de`). Chat-Pipeline **nicht** über `Detect`.

`Classify` mappt `ClassificationResult` → `LanguageDetectionDetails` inkl. `Ranked` (Top bis 8 Einträge). Leerer Text → `LanguageDetectionDetails.Empty("Leerer Text.")`.

### IV.4.2 `ClassificationResult`

Zusätzliche Flags (Defaults false):

- `IsAmbiguous` — Softmax-Margin `top - second < MinMargin` (0,04), **nicht** Unknown.
- `IsUnrecognizedScript` — `ScriptFilter.IsUnsupportedScript`.

Bei Ambiguous: `Code` bleibt der Top-Kandidat, `IsUnknown = false`. Policy entscheidet Nachfrage, nicht der Klassifizierer allein.

### IV.4.3 `ScriptFilter.IsUnsupportedScript`

Buchstaben zählen, Unicode-Script:

- Latein, Kyrillisch, Griechisch, Georgisch, Armenisch = trainiert.
- Alles andere (`LetterScript.Other`: CJK, Hiragana/Katakana, Arabisch, Hebräisch, Thai, …).

True wenn: ≥ 4 Buchstaben **und** `Other ≥ 4` **und** `Other / letters ≥ 0,40`.

Früher `RestrictTo`: Other → `null` (alle Klassen). Japanisch wurde dann mit niedriger Coverage als Unknown → `Detect` → **Deutsch**. Das verletzt Regel 4.

### IV.4.4 Klassifizierer-Konstanten (nicht ohne Not ändern)

`MinLetters = 2`, `MinConfidence = 0,36`, `MinCoverage = 0,28`, `MinMargin = 0,04`, `MinWordCoverage = 0,12`.

Optional im Klassifizierer: Shared-Word-Coverage auf kurzen Texten (`MinSharedWordCoverage`, `MaxSharedLetters`) — **kein Ersatz** für Policy-Polyglot. Wenn nachgebaut: nur als Extra-Signal `IsAmbiguous`, Policy bleibt maßgeblich.

---

## IV.5 `ConversationLanguagePolicy` — so nachbauen

Namespace: `HotelChatbot.Application.Services`. Statische Klasse + State/Decision-Typen.

### IV.5.1 Konstanten

| Name | Wert | Bedeutung |
|------|------|-----------|
| `FallbackLanguage` | `de` | unsicherer lateinischer Text, zu kurz |
| `UnrecognizedLanguage` | `en` | nicht trainierte Schrift |
| `SessionPrefix` | `__conversation_language__:` | System-Message |
| `MinCandidateProbability` | 0,10 | Close-Set |
| `CloseAbsoluteMargin` | 0,15 | Abstand zum Führenden |
| `CloseRelativeRatio` | 0,50 | oder ≥ 50 % des Führenden |
| `StickyMinProbability` | 0,12 | Folgesatz: Lock noch „hoch“ |
| `MaxClarificationLanguages` | 4 | Nachfrage nicht überladen |
| `PolyglotCodes` | `de`, `en`, `nl` | Flämisch = `nl` |

### IV.5.2 State / Decision

```csharp
public sealed class ConversationLanguageState
{
    public string? LockedLanguage { get; set; }
    public bool FirstTurnUnambiguous { get; set; }
    public List<string>? PendingCodes { get; set; }
    public string? PendingQuery { get; set; }
}

public sealed class ConversationLanguageDecision
{
    public required string Language { get; init; }
    public bool NeedsClarification { get; init; }
    public string? ClarificationMessage { get; init; }
    public required ConversationLanguageState State { get; init; }
    public required string Source { get; init; }
    public required string TextToProcess { get; init; }
}
```

JSON wie Constraints: `Serialize` / `Deserialize`, bei Fehler `null`.

### IV.5.3 `Resolve` — Reihenfolge (nicht umstellen)

```
1. Clone(existing)
2. wenn PendingCodes.Count > 0     → ResolvePending
3. wenn IsUnrecognizedScript       → EN, Lock NICHT setzen (unrecognized_en)
4. wenn LockedLanguage gesetzt     → ResolveFollowUp
5. sonst                           → ResolveFirstTurn
```

Unerkannte Schrift **vor** Follow-up/First-Turn: sonst würde ein Lock Deutsch Japanisch sticky halten.

### IV.5.4 Erster Satz (`ResolveFirstTurn`)

1. `IsSharedVocabularyPhrase(text)` → `Ask` mit `PolyglotCandidates` (immer de/en/nl, Scores aus Ranked falls vorhanden).
2. Sonst `CloseLanguages(Ranked)` ≥ 2 → `Ask` mit dieser Close-Gruppe (andere Sprachen, z. B. fr/it, wenn Softmax wirklich nah ist).
3. Sonst eindeutiger Top (`!IsUnknown && !IsAmbiguous && TopCode`) → Lock, `FirstTurnUnambiguous = true`, Source `detected`.
4. Sonst Fallback `de`, **nicht** locken (`fallback_de`). Nächster Satz darf neu entscheiden.

`Ask`: `PendingCodes`, `PendingQuery = original text`, `LockedLanguage = null`, Message = `LanguageClarification.Build(candidates)` (eine Zeile pro Sprache). `NeedsClarification = true`. `TextToProcess` = Pending-Query.

### IV.5.5 Folgesatz mit Lock (`ResolveFollowUp`)

1. Unrecognized → EN **diese** Antwort, Lock **unverändert** (nächstes Deutsch bleibt möglich).
2. `IsSharedVocabularyPhrase` **oder** `IsStickyHigh` → Lock behalten, Source `sticky`.
3. Anderer Top klar (`!IsUnknown`, Top ≠ Lock) → Lock wechseln, `switched` (User hat wirklich die Sprache gewechselt).
4. Sonst Top verwenden oder Lock halten.

`IsStickyHigh`: Lock-Score in Ranked ≥ 0,12 **oder** Lock liegt in `CloseLanguages`.

### IV.5.6 Offene Nachfrage (`ResolvePending`)

1. Unrecognized → EN, Pending löschen.
2. `LanguageClarification.TryMatchChoice(text, PendingCodes)` (Deutsch, German, English, Nederlands, Flämisch, flemish, ISO-Code, …):
   - Wenn der Text **zusätzlich** wie Hotel-Query wirkt (`LooksLikeHotelQuery`: hotel/spa/wellness/… als Substring) → `TextToProcess = text`.
   - Sonst `TextToProcess = PendingQuery` (User sagte nur „Deutsch“).
   - Lock auf gewählten Code, Pending löschen, Source `choice`.
3. Sonst eindeutiger Top **in** PendingCodes → lock + aktuelle Message als Query.
4. Ein Close-Kandidat in Pending → ebenso.
5. Mehrere Close in Pending → erneut `Ask`.
6. Sonst erneut `Ask` mit Remaining aus Ranked ∩ Pending.

### IV.5.7 Geteilter Wortschatz (`IsSharedVocabularyPhrase`)

Pflicht für `Hotels in Salzburg`.

- Wörter = zusammenhängende Buchstaben, lowercased, 1–6 Wörter.
- Mindestens ein Wort beginnt mit `hotel` **oder** ist exakt `spa` / `wellness`.
- **Jedes** Wort steht in `InternationalWords` (IgnoreCase).

Mindest-Set:

```
hotel, hotels, hôtel, spa, wellness, pool, sauna,
salzburg, tirol, tyrol, kärnten, karnten, carinthia,
steiermark, vorarlberg, wien, vienna, bayern, bavaria,
in, mit, with, und, and, the, a, an, bei, near,
adults, only
```

Flämisch nicht als eigener ISO-Code: Katalog hat `nl`. Nachfrage-Satz auf Niederländisch.

Keine Ortsnamen als alleiniges Signal ohne hotel/spa/wellness (sonst „in Salzburg“ ohne Hotelbezug).

### IV.5.8 `LanguageClarification`

- `Build(candidates)`: Zeilen joinen mit `\n`.
- `PromptFor(code)`: native Frage „Möchten Sie auf Deutsch fortfahren?“ / „Would you like to continue in English?“ / „Wilt u in het Nederlands verdergaan?“ plus weitere Katalogsprachen; unbekannt → `Would you like to continue in {Name}?`.
- `TryMatchChoice(text, allowed)`: ganze Wörter, längere Aliase zuerst. `de` nur als alleinstehendes Wort, nicht als Teil von `deutsch` (Wortgrenzen). Aliase mindestens: deutsch/german, english/englisch, nederlands/dutch/niederländisch/holländisch/flemish/flämisch/vlaams.

---

## IV.6 ChatService-Anbindung (konkret)

Signatur:

```csharp
RunSafetyGatesAsync(string text, ChatSession session, bool logIntent, ct, trace)
```

**Kein** `languageOverride`.

Ablauf im Gate:

1. `LoadConversationLanguage` (neueste System-Message mit Prefix).
2. `_languageDetector.Classify(text)`.
3. `Resolve` → `SaveConversationLanguageAsync` (alte Prefix-Messages entfernen, eine schreiben — analog Constraints).
4. Trace-Agent `Language`, Meta: `language`, `source`, `ambiguous`, `unrecognized`, `ranked`.
5. Wenn `NeedsClarification` → `SafetyGateResult(lang, "language_clarify", message, TextToProcess)` **ohne** Ethical/Intent.
6. Sonst Ethical + Intent auf **`queryText = TextToProcess`**, nicht auf „Deutsch“.

`SafetyGateResult` hat vier Felder: `Language`, `RejectReason`, `RejectMessage`, `TextToProcess`.

**Recommend** nach Gates:

- `language_clarify` → `SaveInteractionAsync`, `BuildPipelineResponse(true, …, "language_clarify", …)` — **Success true**, damit MCP/ChatGPT die Frage zeigt (nicht `no_match`).
- `var userQuery = gates.TextToProcess` für Catalog-Detection, Constraint-Merge, Translate, Region-Filter, Relevance, Answer-LLM.
- `SaveInteractionAsync` speichert weiterhin `request.Requirements` (was der User getippt hat).

**Hotel-Details** (`ProcessHotelDetailsAsync`): nach erfolgreichem Hotel-Lookup `GetOrCreatePipelineSessionAsync`, dieselben Gates, `language_clarify` mit `Success = true`, `SessionId = session.SessionId`, Suche/Translate/Answer auf `userQuery`.

Reject-Texte Ethical/Intent: `language == "en"` → EN, sonst DE (bestehend).

---

## IV.7 MCP / Sim

- Tool `get_response`: optionales `sessionId` („from a previous get_response result“).
- `executeTool("get_response")`: `SessionId: args.sessionId` an `/api/chat/recommend`.
- Server-Instructions: SessionId aus dem letzten Tool-Result mitgeben.
- Legacy-Schema analog.
- Sim sendet `sessionId` bereits; `language: null` ist korrekt (Override existiert nicht mehr).

Ohne `sessionId`: jeder Turn = First-Turn (erneute Nachfrage bei Polyglot-Phrasen). Das ist dann kein Classifier-Bug.

---

## IV.8 Umsetzungsschritte (Klon / anderes Produkt)

1. Domain: `LanguageDetectionDetails` + `Classify` auf dem Detector.
2. `ScriptFilter.IsUnsupportedScript`; Klassifizierer: Ambiguous-Flag, Unrecognized früh returnen, Top-8.
3. `LanguageDetectionService.Classify` / `Detect` wie IV.4.1.
4. `LanguageClarification` + `ConversationLanguagePolicy` wie IV.5 (Polyglot-Liste **nicht** weglassen).
5. `ChatService` Gates umbauen (IV.6); Prefix speichern.
6. MCP `sessionId` auf `get_response`.
7. Tests IV.9.
8. `dotnet test src/HotelChatbot.Tests/HotelChatbot.Tests.csproj --filter ConversationLanguagePolicyTests`
9. Host neu starten. Manuell `/sim`: `Hotels in Salzburg` → dreisprachige Frage; `Deutsch` → Hotelsuche; klarer DE-Satz, dann `Hotels in Salzburg` → bleibt DE; japanischer Satz → EN.

---

## IV.9 Tests (müssen nach Language-Änderungen gelten)

Datei: `src/HotelChatbot.Tests/ConversationLanguagePolicyTests.cs`

| Test | Erwartung |
|------|-----------|
| `FirstTurn_CloseScores_AsksInThoseLanguages` | de/en/nl nah → Nachfrage, PendingQuery original, kein fr in der Message |
| `FirstTurn_ClearGerman_LocksGerman` | klare DE-Query → Lock `de`, Source `detected` |
| `FollowUp_LockedGermanStillHighButNotTop_KeepsGerman` | Lock de, Ranked nl>en>de mit de≥0,12 → `sticky` / `de` |
| `FollowUp_ClearLanguageSwitch_UsesNewLanguage` | Lock de, klar FR → `fr`, `switched` |
| `UnrecognizedScript_AnswersEnglish_DoesNotLock` | `IsUnrecognizedScript` → `en`, Lock null |
| `UnknownLatin_FallsBackToGerman` | Empty/unknown → `de`, nicht locken |
| `PendingClarification_Deutsch_UsesPendingQuery` | Pending + „Deutsch“ → `de`, TextToProcess = alte Query |
| `PendingClarification_EnglishAlias_LocksEnglish` | „English please“ → `en` |
| `JapaneseClassifier_MarksUnrecognizedScript` | `京都のホテルを探しています` → `IsUnrecognizedScript` |
| `SharedPhrase_PeakedDutchSoftmax_StillAsksDeEnNl` | Softmax nl 0,97 + Text `Hotels in Salzburg` → **trotzdem** Nachfrage de/en/nl |
| `SharedPhrase_HotelsInSalzburg_AsksClarification` | echter Klassifizierer + Policy |
| `SharedPhrase_AfterClearGerman_StaysGerman` | Lock nach klarem DE, Folgesatz `Hotels in Salzburg` → `de` / `sticky` |

Bestehende Smoke-Tests (`LanguageClassifier_DetectsGermanAndEnglish`) müssen weiter gelten.

Befehl: `dotnet test src/HotelChatbot.Tests/HotelChatbot.Tests.csproj --filter ConversationLanguagePolicyTests` — in dieser Sitzung: 76 Tests gesamt grün.

---

## IV.10 Diagnose, wenn es wieder passiert

1. Trace `Language` Meta `source` / `ranked`. `override` darf nicht mehr vorkommen.
2. `Hotels in Salzburg` erster Turn: fehlt `language_clarify`? Dann `IsSharedVocabularyPhrase` (Wortliste, Tokenisierung) oder Session schon gelockt von einem vorherigen Turn.
3. Antwort auf Niederländisch trotz DE-Lock: Sticky greift nicht — Phrase nicht in InternationalWords oder Lock nicht persistiert (`sessionId`).
4. Japanisch auf Deutsch: `IsUnsupportedScript` false (zu wenig Other-Buchstaben) oder Pipeline nutzt noch `Detect()`+Fallback de.
5. User sagt „Deutsch“, Bot sucht nichts / Out-of-Scope: Ethical/Intent liefen auf „Deutsch“ statt `PendingQuery`. `TextToProcess` prüfen.
6. Jeder Turn fragt erneut die Sprache: `sessionId` fehlt in MCP/Sim.
7. Alter API-Prozess ohne neue DLL.

---

## IV.11 Kurzregeln

| Irrtum | Korrekt |
|--------|---------|
| Sprache nur beim ersten Satz, weil LLM teuer war | Klassifizierer ist billig → **jeder** Turn. |
| Softmax-Gewinner = Gesprächssprache | Nein bei Kurzphrasen. Polyglot + Sticky. |
| Unsicher = immer Deutsch | Lateinisch unsicher = DE. **Andere Schrift** = EN. |
| Client-`language` ist ein gültiger Skip | Nein. Nur Session-Lock/Pending. |
| Nachfrage ohne Pending-Query | User müsste die Hotelsuche wiederholen — verboten. |
| Flämisch eigener Code | Nein. `nl` + niederländischer Nachfrage-Satz. |
| `Detect()` in der Chat-Pipeline | Nein. `Classify` + Policy. `Detect` nur Crawler. |

**Robustheit** = Classify-jeder-Turn + Policy (Close **und** Shared-Vocabulary) + Session-Lock/Pending + Unrecognized→EN + Fallback DE ohne Lock + `sessionId`-Kette.
