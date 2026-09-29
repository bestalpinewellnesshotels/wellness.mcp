using Npgsql;
using System;
using System.IO;

var connectionString = "Host=dev-universe.net;Port=5432;Database=Bwchat;Username=bwchatuser;Password=YOUR_PASSWORD";

Console.WriteLine("Starting pgvector migration...\n");

using var conn = new NpgsqlConnection(connectionString);
conn.Open();

// 1. Backup
Console.WriteLine("1. Creating backup...");
using (var cmd = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS content_chunks_backup AS SELECT * FROM content_chunks", conn))
{
    cmd.ExecuteNonQuery();
}
Console.WriteLine("   ✓ Backup created\n");

// 2. Add vector column
Console.WriteLine("2. Adding vector column...");
using (var cmd = new NpgsqlCommand("ALTER TABLE content_chunks ADD COLUMN IF NOT EXISTS embedding_vector vector(1536)", conn))
{
    cmd.ExecuteNonQuery();
}
Console.WriteLine("   ✓ Column added\n");

// 3. Migrate data
Console.WriteLine("3. Migrating embeddings (this may take a moment)...");
using (var cmd = new NpgsqlCommand("UPDATE content_chunks SET embedding_vector = embedding::text::vector WHERE embedding IS NOT NULL AND embedding_vector IS NULL", conn))
{
    var rows = cmd.ExecuteNonQuery();
    Console.WriteLine($"   ✓ {rows} embeddings migrated\n");
}

// 4. Create index
Console.WriteLine("4. Creating HNSW index (this may take 30-60 seconds)...");
using (var cmd = new NpgsqlCommand("CREATE INDEX IF NOT EXISTS content_chunks_embedding_vector_idx ON content_chunks USING hnsw (embedding_vector vector_cosine_ops)", conn))
{
    cmd.ExecuteNonQuery();
}
Console.WriteLine("   ✓ Index created\n");

// Statistics
Console.WriteLine("Statistics:");
using (var cmd = new NpgsqlCommand("SELECT COUNT(*) as total, COUNT(embedding_vector) as migrated FROM content_chunks", conn))
using (var reader = cmd.ExecuteReader())
{
    if (reader.Read())
    {
        Console.WriteLine($"   Total chunks: {reader.GetInt64(0)}");
        Console.WriteLine($"   Migrated: {reader.GetInt64(1)}");
    }
}

Console.WriteLine("\n✓ Migration completed successfully!");
