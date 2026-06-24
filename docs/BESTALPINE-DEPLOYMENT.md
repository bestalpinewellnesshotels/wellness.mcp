# Best Alpine / Mynet – Produktiv-Deployment

**Ein Befehl genügt:**

```powershell
.\DEPLOY-PRODUCTION.ps1
```

Beim ersten Start fragt das Script fehlende Zugangsdaten ab und speichert sie in `deploy/production.settings.json` (lokal, nicht im Git). Beim nächsten Mal reicht meist Enter.

---

| Komponente | Plesk (Test) | Best Alpine Server |
|------------|--------------|-------------------|
| .NET API | `bestwellness-api.dev-universe.net` | intern `http://127.0.0.1:8080` |
| MCP (ChatGPT SSE) | `bestwellness.dev-universe.net` | `https://mcp.bestalpine2.ms.mynet.at` (via Apache) |
| PostgreSQL + pgvector | extern | `localhost` / `bestalpinedb1` |

## Warum PowerShell statt GitHub Actions?

Für den **ersten Produktiv-Start** ist ein lokales PowerShell-Skript am einfachsten:

- Sie arbeiten ohnehin auf Windows und haben bereits `DEPLOY.ps1`.
- Zugangsdaten (DB, OpenAI, SSH) bleiben lokal – nicht in GitHub Secrets.
- systemd, Node.js und DB-Migration lassen sich beim ersten Mal leichter manuell prüfen.

GitHub Actions lohnt sich **später**, wenn SSH-Key + Secrets eingerichtet sind und jeder Push auf `main` automatisch deployen soll.

---

## Server-Struktur

```
/data/web/mcp/home/
├── app/                          # .NET API (self-contained)
│   ├── HotelChatbot.Api
│   ├── appsettings.Production.json
│   └── wwwroot/                  # Admin + Demo
├── mcp/                          # Node.js MCP Server
│   ├── index.js
│   ├── package.json
│   └── node_modules/
└── .config/systemd/user/
    ├── hotelchatbot-api.service
    └── mcp.service
```

Apache `DocumentRoot` (`/data/web/mcp/home/site/public`) wird für statische Dateien genutzt; der MCP-Dienst läuft als eigener Prozess auf Port **3001** und wird per Reverse Proxy nach außen geleitet.

---

## Schritt 1 – Einmalig: SSH-Key (empfohlen)

Ohne Key fragt `scp`/`ssh` bei jedem Schritt nach dem Passwort.

```powershell
ssh-keygen -t ed25519 -f $env:USERPROFILE\.ssh\id_ed25519_bestalpine -N '""'
type $env:USERPROFILE\.ssh\id_ed25519_bestalpine.pub | ssh mcp@mcp.bestalpine2.ms.mynet.at "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

Optional in `~/.ssh/config`:

```
Host bestalpine-mcp
    HostName mcp.bestalpine2.ms.mynet.at
    User mcp
    IdentityFile ~/.ssh/id_ed25519_bestalpine
```

---

## Schritt 2 – Lokale Konfiguration

Entfällt – das Script fragt Sie interaktiv ab. Gespeichert wird in `deploy/production.settings.json`.

Optional manuell bearbeiten oder neu abfragen:

```powershell
.\DEPLOY-PRODUCTION.ps1 -Reconfigure
```

---

## Schritt 3 – Einmalig auf dem Server

Per SSH einloggen:

```powershell
ssh mcp@mcp.bestalpine2.ms.mynet.at
```

Dann:

```bash
bash -s < deploy/server-setup.sh
# oder manuell die Befehle aus deploy/server-setup.sh
```

**Node.js 18+** muss installiert sein (`node --version`). Falls nicht: Mynet anfragen.

**User-Linger** (Services laufen nach Logout):

```bash
loginctl enable-linger mcp
```

---

## Schritt 4 – Deploy vom Windows-PC

```powershell
.\DEPLOY-PRODUCTION.ps1
```

Nur bauen:

```powershell
.\DEPLOY-PRODUCTION.ps1 -BuildOnly
```

---

## Schritt 5 – Datenbank

Die leere Produktiv-DB muss Schema + Hoteldaten erhalten. Optionen:

1. **Dump vom Testserver** (empfohlen beim Go-Live):

```bash
# Auf Testserver / lokal mit Zugang zu dev-universe.net:
pg_dump -h dev-universe.net -U bwchatuser -d Bwchat -Fc -f bwchat.dump

# Auf Best-Alpine-Server (als User mit DB-Zugang):
pg_restore -h localhost -U bestalpine -d bestalpinedb1 --no-owner --no-acl bwchat.dump
```

2. **Frisch starten**: Hotels über `/admin` neu crawlen (dauert länger).

pgvector-Extension sollte auf dem Server bereits aktiv sein.

---

## Schritt 6 – Reverse Proxy (an Mynet / Kathi)

**Bitte an Mynet weiterleiten:**

| Einstellung | Wert |
|-------------|------|
| Öffentliche URL | `https://mcp.bestalpine2.ms.mynet.at` |
| Backend | `http://127.0.0.1:3001` |
| Pfade | `/`, `/sse`, `/health` |
| SSL | vom Server / Let’s Encrypt |
| WebSocket/SSE | Proxy darf Streaming nicht puffern (`ProxyPass` + ggf. `flushpackets=on`) |

Beispiel Apache (Referenz für Mynet):

```apache
ProxyPreserveHost On
ProxyPass        / http://127.0.0.1:3001/
ProxyPassReverse / http://127.0.0.1:3001/
```

Die .NET-API bleibt **nur intern** auf Port 8080. Optional kann später eine zweite Subdomain (z. B. `api.wellnesshotels.com`) für Admin/Crawling eingerichtet werden.

**ChatGPT Connector URL:** `https://mcp.bestalpine2.ms.mynet.at/sse`

---

## Schritt 7 – Test

Auf dem Server:

```bash
curl http://127.0.0.1:8080/health
curl http://127.0.0.1:3001/health
```

Von außen (nach Proxy):

```bash
curl https://mcp.bestalpine2.ms.mynet.at/health
```

---

## Logs & Neustart

```bash
systemctl --user status hotelchatbot-api.service mcp.service
journalctl --user -u hotelchatbot-api.service -f
journalctl --user -u mcp.service -f
systemctl --user restart hotelchatbot-api.service mcp.service
```

---

## Mail an Kathi (Vorlage)

> Hallo Kathi,
>
> der MCP-Dienst läuft intern auf Port **3001**, die API dahinter auf Port **8080** (nur localhost).
> Bitte lasst von Mynet einen Apache Reverse Proxy einrichten:
> **https://mcp.bestalpine2.ms.mynet.at** → **http://127.0.0.1:3001**
> (Pfade `/sse` und `/health` müssen erreichbar sein.)
>
> Für ChatGPT/Claude ist die Connector-URL: **https://mcp.bestalpine2.ms.mynet.at/sse**
>
> Optional später: eigene Subdomain für die Admin-Oberfläche.
>
> LG Gerhard

---

## Troubleshooting

| Problem | Lösung |
|---------|--------|
| `npm fehlt` | Mynet: Node.js 20 installieren |
| MCP `apiBaseUrl` zeigt falsche URL | Service neu starten nach Deploy |
| API antwortet nicht | `journalctl --user -u hotelchatbot-api.service` – oft DB-Verbindung oder OpenAI-Key |
| 502 von Apache | MCP-Service prüfen: `curl http://127.0.0.1:3001/health` |
| SSH Passwort-Loop | SSH-Key einrichten (Schritt 1) |
