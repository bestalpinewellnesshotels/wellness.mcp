-- Geo-Spalten für Distanz-Ranking (Nähe-Fragen).
-- Danach: seed-hotel-geo.sql ausführen.
-- Idempotent. Die API ergänzt dieselben Spalten beim Start (IF NOT EXISTS).

ALTER TABLE hotels ADD COLUMN IF NOT EXISTS latitude DOUBLE PRECISION;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS longitude DOUBLE PRECISION;
