# GitHub Actions – Production Deploy

Workflow: `.github/workflows/deploy-production.yml`  
Startet bei Push auf `main` oder manuell unter Actions.

## Secrets in GitHub hinterlegen

Repository → Settings → Secrets and variables → Actions → New repository secret

| Secret | Beispielwert |
|--------|--------------|
| `DEPLOY_SSH_PRIVATE_KEY` | Inhalt von `~/.ssh/id_ed25519_bestalpine` |
| `DEPLOY_SSH_HOST` | `mcp.bestalpine2.ms.mynet.at` |
| `DEPLOY_SSH_USER` | `mcp` |
| `DEPLOY_PUBLIC_URL` | `https://mcp.bestalpine2.ms.mynet.at` |
| `POSTGRES_DATABASE` | `bestalpinedb1` |
| `POSTGRES_USER` | `bestalpine` |
| `POSTGRES_PASSWORD` | *(Produktiv-Passwort)* |
| `OPENAI_ENDPOINT` | Azure/OpenAI Endpoint |
| `OPENAI_API_KEY` | API Key |
| `OPENAI_DEPLOYMENT` | `gpt-4.1-mini` |
| `OPENAI_EMBEDDING_DEPLOYMENT` | `text-embedding-3-small` |
| `OPENAI_EMBEDDING_DIMENSIONS` | `1536` |
| `SPEECH_KEY` | optional |
| `SPEECH_REGION` | `germanywestcentral` |
| `ELEVENLABS_KEY` | optional |
| `ELEVENLABS_VOICE` | optional |
| `ADMIN_PASSWORD` | Admin-Passwort |
| `HOTEL_PASSWORD` | Hotel-Passwort |
| `TOKEN_SECRET` | langer Zufallsstring |

Private Key exportieren (lokal):

```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519_bestalpine -Raw
```

Komplett kopieren inkl. `-----BEGIN/END OPENSSH PRIVATE KEY-----`.
