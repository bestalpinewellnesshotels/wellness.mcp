-- 1. Backup erstellen
CREATE TABLE IF NOT EXISTS content_chunks_backup AS 
SELECT * FROM content_chunks;

-- 2. Vector-Spalte hinzufügen
ALTER TABLE content_chunks 
ADD COLUMN IF NOT EXISTS embedding_vector vector(1536);

-- 3. Daten migrieren (JSONB array -> vector)
UPDATE content_chunks 
SET embedding_vector = embedding::text::vector
WHERE embedding IS NOT NULL AND embedding_vector IS NULL;

-- 4. Index erstellen
CREATE INDEX IF NOT EXISTS content_chunks_embedding_vector_idx 
ON content_chunks USING hnsw (embedding_vector vector_cosine_ops);

-- Statistik
SELECT 
    COUNT(*) as total_chunks,
    COUNT(embedding_vector) as migrated_chunks
FROM content_chunks;
