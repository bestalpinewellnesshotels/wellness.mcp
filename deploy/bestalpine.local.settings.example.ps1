# Kopiere diese Datei nach deploy/bestalpine.local.settings.ps1 und trage die Werte ein.
# deploy/bestalpine.local.settings.ps1 wird NICHT ins Git committed.

$DeployConfig = @{
    # SSH / SFTP (vom Kunden)
    SshHost     = "mcp.bestalpine2.ms.mynet.at"
    SshUser     = "mcp"
    # Optional: nur noetig ohne SSH-Key. Besser: einmalig SSH-Key einrichten (siehe docs/BESTALPINE-DEPLOYMENT.md)
    SshPassword = ""

    # Pfade auf dem Server (Home des mcp-Users)
    RemoteAppDir = "/data/web/mcp/home/app"
    RemoteMcpDir = "/data/web/mcp/home/mcp"

    # PostgreSQL (vom Kunden, localhost auf dem Server)
    PostgresHost     = "localhost"
    PostgresPort     = 5432
    PostgresDatabase = "bestalpinedb1"
    PostgresUser     = "bestalpine"
    PostgresPassword = "CHANGE_ME"

    # OpenAI / Azure OpenAI – Werte vom Kunden eintragen
    OpenAiEndpoint              = "https://YOUR-RESOURCE.openai.azure.com/"
    OpenAiApiKey                = "CHANGE_ME"
    OpenAiDeploymentName        = "gpt-4.1-mini"
    OpenAiEmbeddingDeployment   = "text-embedding-3-small"
    OpenAiEmbeddingDimensions   = 1536

    # Optional: Azure Speech-to-Text (leer lassen wenn nicht genutzt)
    SpeechSubscriptionKey = ""
    SpeechServiceRegion   = "germanywestcentral"

    # Admin-Oberflaeche (/admin) – in Produktion starke Passwoerter setzen
    AdminPassword = "CHANGE_ME"
    HotelPassword = "CHANGE_ME"
    TokenSecret   = "CHANGE_ME-long-random-string"

    # Interne Ports (Apache proxyt nur MCP nach aussen)
    ApiPort = 8080
    McpPort = 3001
}

# Alias fuer aeltere Skript-Versionen
$cfg = $DeployConfig
