    -- Migration Script: JSONB Embeddings → pgvector
    -- Führt die Migration der content_chunks Tabelle auf pgvector durch

    \echo '=== Migration zu pgvector ==='
    \echo ''

    -- 1. Backup der aktuellen Daten (optional, aber empfohlen)
    \echo '1. Erstelle Backup-Tabelle...'
    CREATE TABLE IF NOT EXISTS content_chunks_backup AS 
    SELECT * FROM content_chunks;

    \echo '   ✓ Backup erstellt: content_chunks_backup'
    \echo ''

    -- 2. Neue Spalte für pgvector hinzufügen
    \echo '2. Füge vector-Spalte hinzu...'
    ALTER TABLE content_chunks 
    ADD COLUMN IF NOT EXISTS embedding_vector vector(1536);

    \echo '   ✓ Spalte embedding_vector (vector(1536)) hinzugefügt'
    \echo ''

    -- 3. Daten migrieren: JSONB → vector
    \echo '3. Migriere Embeddings von JSONB zu vector...'
    \echo '   (Das kann einige Sekunden dauern für 301 Einträge)'
    UPDATE content_chunks
    SET embedding_vector = embedding::text::vector
    WHERE embedding IS NOT NULL 
    AND embedding_vector IS NULL;

    \echo '   ✓ Embeddings migriert'
    \echo ''

    -- 4. Index erstellen für schnelle Vektor-Suche
    \echo '4. Erstelle IVFFlat Index für Cosine Similarity...'
    \echo '   (Das kann 10-30 Sekunden dauern)'

    -- Zuerst prüfen ob genug Daten vorhanden sind (IVFFlat braucht >100 rows)
    DO $$
    DECLARE
        row_count INTEGER;
    BEGIN
        SELECT COUNT(*) INTO row_count FROM content_chunks WHERE embedding_vector IS NOT NULL;
        
        IF row_count < 100 THEN
            RAISE NOTICE '   ⚠ Nur % Zeilen vorhanden. IVFFlat-Index braucht mindestens 100 Zeilen.', row_count;
            RAISE NOTICE '   → Verwende HNSW-Index stattdessen...';
            
            CREATE INDEX IF NOT EXISTS content_chunks_embedding_hnsw_idx 
            ON content_chunks 
            USING hnsw (embedding_vector vector_cosine_ops);
        ELSE
            RAISE NOTICE '   ✓ % Zeilen vorhanden. Erstelle IVFFlat-Index...', row_count;
            
            -- IVFFlat mit lists = sqrt(rows)
            CREATE INDEX IF NOT EXISTS content_chunks_embedding_ivfflat_idx 
            ON content_chunks 
            USING ivfflat (embedding_vector vector_cosine_ops)
            WITH (lists = 10);
        END IF;
    END $$;

    \echo '   ✓ Vector-Index erstellt'
    \echo ''

    -- 5. Alte JSONB-Spalte behalten (für Kompatibilität während Migration)
    \echo '5. Behalte alte embedding-Spalte (JSONB) als Backup'
    \echo '   Nach erfolgreicher Migration kann sie gelöscht werden mit:'
    \echo '   ALTER TABLE content_chunks DROP COLUMN embedding;'
    \echo ''

    -- 6. Statistiken
    \echo '6. Statistiken:'
    SELECT 
        COUNT(*) as total_chunks,
        COUNT(embedding_vector) as with_vector,
        COUNT(*) - COUNT(embedding_vector) as without_vector
    FROM content_chunks;

    \echo ''
    \echo '=== Migration abgeschlossen ==='
    \echo ''
    \echo 'Nächste Schritte:'
    \echo '1. C#-Code neu starten'
    \echo '2. Test-Anfrage senden'
    \echo '3. Bei Erfolg: ALTER TABLE content_chunks DROP COLUMN embedding;'
