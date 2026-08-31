# Produktiv-URLs (Best Alpine)

| Dienst | URL |
|--------|-----|
| **MCP / OpenAI (Streamable HTTP)** | `https://mcp.bestalpine2.ms.mynet.at/mcp` |
| MCP Health | `https://mcp.bestalpine2.ms.mynet.at/health` |
| Domain-Challenge | `https://mcp.bestalpine2.ms.mynet.at/.well-known/openai-apps-challenge` |
| Legacy SSE (Übergang) | `https://mcp.bestalpine2.ms.mynet.at/sse` |
| API (intern) | `http://127.0.0.1:8080` |
| Admin-Oberfläche | nach Deploy intern: `http://127.0.0.1:8080/admin` |

## ChatGPT / OpenAI Plugin

- **MCP Server URL (Einreichung):** `https://mcp.bestalpine2.ms.mynet.at/mcp`
- Transport: **Streamable HTTP** (nicht mehr `/sse` als offizieller Endpunkt)

Test:

```bash
curl https://mcp.bestalpine2.ms.mynet.at/health
npx @modelcontextprotocol/inspector
# Inspector: Streamable HTTP → https://mcp.bestalpine2.ms.mynet.at/mcp
```

Deploy vom Windows-PC:

```powershell
.\DEPLOY-PRODUCTION.ps1
```

Details: `docs/BESTALPINE-DEPLOYMENT.md`

## Testserver (alt)

| Dienst | URL |
|--------|-----|
| API | `https://bestwellness-api.dev-universe.net` |
| MCP | `https://bestwellness.dev-universe.net/sse` |
