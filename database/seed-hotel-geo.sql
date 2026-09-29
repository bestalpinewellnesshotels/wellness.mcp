-- Stammdaten + WGS84 für den Katalog (hotel-urls.json).
-- Zuerst migrate-hotel-geo.sql ausführen.
-- UPDATE trifft nur vorhandene hotel_id; fehlende Häuser bleiben unberührt.

UPDATE hotels SET
  name = 'STOCK resort',
  location = 'Finkenberg',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.stock.at/',
  latitude = 47.154477,
  longitude = 11.823156,
  updated_at = NOW()
WHERE hotel_id = 'hotel_stock_at';

UPDATE hotels SET
  name = 'Hotel Post Lermoos',
  location = 'Lermoos',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.post-lermoos.at/',
  latitude = 47.4014,
  longitude = 10.8796,
  updated_at = NOW()
WHERE hotel_id = 'hotel_post-lermoos_at';

UPDATE hotels SET
  name = 'Hotel Nesslerhof',
  location = 'Großarl',
  region = 'Salzburg',
  country = 'Austria',
  official_url = 'https://www.nesslerhof.at/',
  latitude = 47.2374,
  longitude = 13.2009,
  updated_at = NOW()
WHERE hotel_id = 'hotel_nesslerhof_at';

UPDATE hotels SET
  name = 'Der Alpbacherhof',
  location = 'Alpbach',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.alpbacherhof.at/',
  latitude = 47.3986,
  longitude = 11.9428,
  updated_at = NOW()
WHERE hotel_id = 'hotel_alpbacherhof_at';

UPDATE hotels SET
  name = 'Naturhotel Waldklause',
  location = 'Längenfeld',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.waldklause.at/',
  latitude = 47.0703,
  longitude = 10.9618,
  updated_at = NOW()
WHERE hotel_id = 'hotel_waldklause_at';

UPDATE hotels SET
  name = 'Der Engel',
  location = 'Grän',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.engel-tirol.com/',
  latitude = 47.5006,
  longitude = 10.5563,
  updated_at = NOW()
WHERE hotel_id = 'hotel_engel-tirol_com';

UPDATE hotels SET
  name = 'Hotel Hochschober',
  location = 'Turracher Höhe',
  region = 'Kärnten',
  country = 'Austria',
  official_url = 'https://www.hochschober.com/',
  latitude = 46.9140,
  longitude = 13.8758,
  updated_at = NOW()
WHERE hotel_id = 'hotel_hochschober_com';

UPDATE hotels SET
  name = 'Übergossene Alm Resort',
  location = 'Dienten am Hochkönig',
  region = 'Salzburg',
  country = 'Austria',
  official_url = 'https://www.uebergossenealm.at/',
  latitude = 47.3853,
  longitude = 13.0028,
  updated_at = NOW()
WHERE hotel_id = 'hotel_uebergossenealm_at';

UPDATE hotels SET
  name = 'Wellnessresidenz Alpenrose',
  location = 'Maurach am Achensee',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.alpenrose.at/',
  latitude = 47.4250,
  longitude = 11.7548,
  updated_at = NOW()
WHERE hotel_id = 'hotel_alpenrose_at';

UPDATE hotels SET
  name = 'Wellnesshotel Warther Hof',
  location = 'Warth am Arlberg',
  region = 'Vorarlberg',
  country = 'Austria',
  official_url = 'https://www.wartherhof.at/',
  latitude = 47.2583,
  longitude = 10.1836,
  updated_at = NOW()
WHERE hotel_id = 'hotel_wartherhof_at';

UPDATE hotels SET
  name = 'Hotel Krallerhof',
  location = 'Leogang',
  region = 'Salzburg',
  country = 'Austria',
  official_url = 'https://www.krallerhof.at/',
  latitude = 47.4394,
  longitude = 12.7610,
  updated_at = NOW()
WHERE hotel_id = 'hotel_krallerhof_at';

UPDATE hotels SET
  name = 'Hotel Theresa',
  location = 'Zell am Ziller',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.theresa.at/',
  latitude = 47.2328,
  longitude = 11.8775,
  updated_at = NOW()
WHERE hotel_id = 'hotel_theresa_at';

UPDATE hotels SET
  name = 'Genussdorf Gmachl',
  location = 'Bergheim',
  region = 'Salzburg',
  country = 'Austria',
  official_url = 'https://www.gmachl.at/',
  latitude = 47.839749,
  longitude = 13.022782,
  updated_at = NOW()
WHERE hotel_id = 'hotel_gmachl_at';

UPDATE hotels SET
  name = 'Alpenresort Schwarz',
  location = 'Mieming',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://www.alpenresort-schwarz.at/',
  latitude = 47.3008,
  longitude = 10.9842,
  updated_at = NOW()
WHERE hotel_id = 'hotel_alpenresort-schwarz_at';

UPDATE hotels SET
  name = 'Alpin Resort Sacher Seefeld',
  location = 'Seefeld',
  region = 'Tirol',
  country = 'Austria',
  official_url = 'https://seefeld.sacher.com/',
  latitude = 47.3315,
  longitude = 11.1850,
  updated_at = NOW()
WHERE hotel_id = 'hotel_seefeld_sacher_com';
