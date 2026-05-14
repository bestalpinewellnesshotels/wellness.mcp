# Migration zu pgvector
$connectionString = "Host=dev-universe.net;Port=5432;Database=Bwchat;Username=bwchatuser;Password=0#PLsqi59Lsytj*j;SSL Mode=Prefer;Trust Server Certificate=true"

$dllPath = ".\src\HotelChatbot.Api\bin\Debug\net10.0\Npgsql.dll"
Add-Type -Path $dllPath

$conn = New-Object Npgsql.NpgsqlConnection($connectionString)
$conn.Open()

Write-Host "1. Backup erstellen..." -ForegroundColor Yellow
$cmd = $conn.CreateCommand()
$cmd.CommandText = "CREATE TABLE IF NOT EXISTS content_chunks_backup AS SELECT * FROM content_chunks;"
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "   OK Backup erstellt" -ForegroundColor Green

Write-Host "2. Vector-Spalte hinzufuegen..." -ForegroundColor Yellow
$cmd.CommandText = "ALTER TABLE content_chunks ADD COLUMN IF NOT EXISTS embedding_vector vector(1536);"
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "   OK Spalte hinzugefuegt" -ForegroundColor Green

Write-Host "3. Daten migrieren..." -ForegroundColor Yellow
$cmd.CommandText = "UPDATE content_chunks SET embedding_vector = embedding::text::vector WHERE embedding IS NOT NULL AND embedding_vector IS NULL;"
$rows = $cmd.ExecuteNonQuery()
Write-Host "   OK $rows Embeddings migriert" -ForegroundColor Green

Write-Host "4. Index erstellen..." -ForegroundColor Yellow
$cmd.CommandText = "CREATE INDEX IF NOT EXISTS content_chunks_embedding_hnsw_idx ON content_chunks USING hnsw (embedding_vector vector_cosine_ops);"
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "   OK HNSW-Index erstellt" -ForegroundColor Green

$conn.Close()
Write-Host "`nMigration abgeschlossen!" -ForegroundColor Green
