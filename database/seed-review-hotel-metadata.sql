-- Redaktionelle Stammdaten für OpenAI-Review-Hotels (Top-3 nach Chunk-Anzahl)
-- Ausführen auf Produktiv-DB bestalpinedb1

ALTER TABLE hotels ADD COLUMN IF NOT EXISTS location TEXT;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS region TEXT;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS country TEXT;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS official_url TEXT;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS source_url TEXT;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS editorial_review_status TEXT;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS editorial_reviewed_at TIMESTAMP;
ALTER TABLE hotels ADD COLUMN IF NOT EXISTS categories JSONB NOT NULL DEFAULT '[]'::jsonb;

UPDATE hotels SET
  location = 'Finkenberg',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.stock.at/',
  source_url = 'https://www.stock.at/',
  editorial_review_status = 'approved',
  editorial_reviewed_at = NOW(),
  categories = '["Wellness","Ski","Alpine"]'::jsonb,
  updated_at = NOW()
WHERE hotel_id = 'hotel_stock_at';

UPDATE hotels SET
  location = 'Großarl',
  region = 'Salzburger Land',
  country = 'Austria',
  official_url = 'https://www.nesslerhof.at/',
  source_url = 'https://www.nesslerhof.at/',
  editorial_review_status = 'approved',
  editorial_reviewed_at = NOW(),
  categories = '["Wellness","Family","Alpine"]'::jsonb,
  updated_at = NOW()
WHERE hotel_id = 'hotel_nesslerhof_at';

UPDATE hotels SET
  location = 'Dienten am Hochkönig',
  region = 'Salzburger Land',
  country = 'Austria',
  official_url = 'https://uebergossenealm.at/',
  source_url = 'https://uebergossenealm.at/',
  editorial_review_status = 'approved',
  editorial_reviewed_at = NOW(),
  categories = '["Wellness","Alpine","Spa"]'::jsonb,
  updated_at = NOW()
WHERE hotel_id = 'hotel_uebergossenealm_at';
