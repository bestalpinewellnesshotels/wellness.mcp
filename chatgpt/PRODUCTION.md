# Produktiv-URLs (Best Alpine)

| Dienst | URL |
|--------|-----|
| **MCP / ChatGPT Connector (SSE)** | `https://mcp.bestalpine2.ms.mynet.at/sse` |
| MCP Health | `https://mcp.bestalpine2.ms.mynet.at/health` |
| API (intern) | `http://127.0.0.1:8080` |
| Admin-Oberfläche | nach Deploy intern: `http://127.0.0.1:8080/admin` |

## ChatGPT Custom GPT / Connector

In OpenAI unter **Actions** oder **Apps / Connector**:

- **Server URL:** `https://mcp.bestalpine2.ms.mynet.at`
- **SSE Endpoint:** `/sse`

Test:

```bash
curl https://mcp.bestalpine2.ms.mynet.at/health
curl -N https://mcp.bestalpine2.ms.mynet.at/sse
```

## Testserver (alt)

| Dienst | URL |
|--------|-----|
| API | `https://bestwellness-api.dev-universe.net` |
| MCP | `https://bestwellness.dev-universe.net/sse` |
