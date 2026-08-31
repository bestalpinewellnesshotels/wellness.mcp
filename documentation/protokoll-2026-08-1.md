# Protokoll – OpenAI-Plugin / MCP Best Alpine Wellness Hotels  
**Datum:** 9. August 2026  
**Von:** Gerhard  
**An:** Stephan  
**Betreff (Mail-Vorschlag):** Stand MCP / ChatGPT-Plugin – Technik weitgehend fertig, Einreichung wartet auf Firmen-Account

---

Hallo Stephan,

kurz der aktuelle Stand zur öffentlichen Einreichung des MCP-Servers bei OpenAI (ChatGPT Plugins).

## 1. Ausgangslage

OpenAI hatte den Server unter `https://mcp.bestalpine2.ms.mynet.at/sse` zwar erreichbar gefunden, aber für die öffentliche Einreichung noch nicht freigegeben. Gefordert waren unter anderem:

- stabiler Endpunkt `/mcp` mit aktuellem Streamable-HTTP-Transport  
- Tool-Sicherheitskennzeichnungen  
- strukturierte Suche inkl. Hotel-IDs  
- saubere Tool-Antworten ohne interne Debug-Felder  
- Health ohne interne URLs  
- Domain-Verifikation  
- 5 positive und 3 negative Testfälle  
- bei geschützter App: Demo-Zugang ohne MFA (bei uns: öffentlich ohne Login)

## 2. Was erledigt wurde

### Technik (produktiv deployed)

- Öffentlicher MCP-Endpunkt: **`https://mcp.bestalpine2.ms.mynet.at/mcp`** (Streamable HTTP)  
- Legacy-`/sse` bleibt übergangsweise bestehen, ist aber nicht mehr der Einreichungsendpunkt  
- `GET /health` liefert nur noch: `{ "status": "ok" }`  
- Pfad für Domain-Challenge vorbereitet: `/.well-known/openai-apps-challenge` (Token kommt erst aus dem OpenAI-Portal)  
- Rate Limiting und Timeouts aktiv  
- Tools mit Annotations: `readOnlyHint: true`, `openWorldHint: false`, `destructiveHint: false`  
- Suche liefert strukturierte Hotels inkl. stabiler **Hotel-ID** und Name  
- Detail-Tool funktioniert über Hotel-ID (`get_hotel_details`)  
- Quellen-Footer ohne Platzhalter „[to be done]“  
- Auth Version 1: **ohne Login** (nur Lesen, keine Buchungen)

### Inhalte / Review-Hotels

Für die OpenAI-Tests wurden die Hotels mit den meisten Datenbank-Einträgen gewählt und mit Stammdaten versehen:

1. Stock (`hotel_stock_at`)  
2. Nesslerhof (`hotel_nesslerhof_at`)  
3. Übergossene Alm (`hotel_uebergossenealm_at`)

### Listing-Material (bereits vorliegend)

- Website: https://www.wellnesshotel.com/  
- Datenschutz: https://www.wellnesshotel.com/de/datenschutz  
- AGB: https://www.wellnesshotel.com/de/agb  
- Support: https://www.wellnesshotel.com/de/ueber-uns/kontakt-service  
- Support-Mail: info@bestwellnesshotels.at  
- Logo: Website-SVG bzw. rundes BAWH-Logo  

### Tests

- Automatischer Smoke-Test gegen Produktiv-`/mcp`: bestanden  
- Verbindung in ChatGPT **Developer Mode** funktioniert (Plugin „Best Alpine Wellness“, URL mit `/mcp`)  
- Suchanfragen liefern Hotels aus der Best-Alpine-Datenbank  

### Einreichungsunterlagen (Entwurf)

Liegen im Projekt unter `documentation/openai-submission-pack.md` (Listing-Texte, Tool-Begründungen, 5+3 Testfälle, Release Notes).

## 3. Aktueller Stopper bei der öffentlichen Einreichung

Das OpenAI-Plugin-Portal unter https://platform.openai.com/plugins ist erreichbar.  
Für **Create plugin / Business-Verifizierung** verlangt OpenAI:

- verifizierte Organisation (Business empfohlen)  
- **hinterlegte Zahlungsmethode** als Default-Payment-Method  

Ohne Firmen-OpenAI-Konto bzw. ohne Zahlungsmittel des Kunden kann die Verifizierung und damit Submit for Review **nicht** abgeschlossen werden.

Hinweis: Der technische Server und der Developer-Mode-Test laufen davon **unabhängig** weiter.

## 4. To-dos seitens Kunde / Projektleitung (Best Alpine)

Bitte zeitnah klären und freigeben:

1. **OpenAI-Organisation:** Unter welchem Firmen-Account soll das Plugin eingereicht und später veröffentlicht werden? (nicht unter einem privaten Entwicklerkonto)  
2. **Zahlungsmethode** für diese Organisation hinterlegen und als Default setzen  
3. **Business-Verifizierung** im OpenAI Platform Dashboard abschließen (Firmendaten / Nachweise)  
4. **Demo-Recording** der Hauptflows (Suche + Hotel-Details) – OpenAI verlangt für die Directory-Einreichung eine Demo-URL  
5. Kurzes Gegenlesen der Listing-Texte und Testfälle (Marken-/Produktwahrheit)  
6. Bestätigung: Version 1 bleibt bewusst **nur Lesen** (keine Buchung, keine Angebotsanfrage, keine E-Mails)

Sobald Punkt 1–3 erledigt sind, können Domain-Verify, Scan Tools und Submit unmittelbar fortgesetzt werden.

## 5. To-dos, die Gerhard und die Entwicklung parallel erledigen

### Gerhard

- Testfälle aus dem Submission-Pack im Developer Mode noch einmal manuell durchspielen (positiv und negativ)  
- Mit PL Abstimmung zu Firmen-Account / Zahlung / Verifizierung vorantreiben  
- Logo-Datei fürs Portal bereitlegen (PNG, falls SVG im Portal Probleme macht)  
- Nach Freigabe der Verifizierung: Portal-Schritte Domain-Challenge → Token an Entwicklung → Verify → Scan Tools → Formular → Submit  

### Entwicklung (Agent / Umsetzung)

- Domain-Challenge-Token entgegennehmen und unter  
  `https://mcp.bestalpine2.ms.mynet.at/.well-known/openai-apps-challenge`  
  als reinen Text ausrollen  
- Beim weiteren Portal-Durchlauf Schritt-für-Schritt unterstützen (Scan Tools, Formularfelder, Justifications, Testfälle)  
- Optional später (nur nach ausdrücklicher Freigabe): Latenz der Suche weiter verbessern; Prompt-Verschärfung gegen erfundete Preise/Verfügbarkeiten  
- Optional: weitere Hotels im Admin mit Ort/Region/Land/URL/Freigabe-Status vervollständigen  

## 6. Nächster gemeinsamer Meilenstein

**Ziel:** Öffentliche Einreichung (Submit for review), sobald die Firmen-Organisation verifiziert ist und eine Zahlungsmethode hinterlegt wurde.

Danach: Review durch OpenAI → bei Freigabe Veröffentlichung durch euch im Portal.

---

Viele Grüße  
Gerhard

---

*Interne Referenz:* Arbeitsplan `documentation/todo-202608.md`, Materialien `documentation/openai-submission-pack.md` / `documentation/listing-materials.md`  
*MCP-URL (Einreichung):* `https://mcp.bestalpine2.ms.mynet.at/mcp`
