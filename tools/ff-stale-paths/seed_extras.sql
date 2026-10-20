-- Loopback stub indexer and download client, so any automatic search or grab in a
-- throwaway run shows up in the stub request log instead of leaving the machine.
INSERT INTO Indexers (Name, Type, Implementation, Url, ApiKey, Categories, Tags, EnableRss, EnableAutomaticSearch,
  EnableInteractiveSearch, EnableAnimeStandardSearch, IsEnabled, Priority, MinimumAge, Retention, MaximumSize,
  AdditionalSettings, CreatedAt, UpdatedAt)
VALUES ('stub-indexer', 'Torrent', 'Torznab', 'http://127.0.0.1:9696', 'stubkey', '3030', '', 1, 1, 1, 0, 1, 25, 0, 0, 0,
  '{}', '2026-08-01 12:00:00', '2026-08-01 12:00:00');
INSERT INTO DownloadClientConfigurations (Id, Name, Type, Host, Port, Username, Password, DownloadPath, UseSSL, IsEnabled,
  SettingsJson, CreatedAt, RemoveCompletedDownloads, Priority)
VALUES ('stub-qbit', 'stub-qbit', 'qbittorrent', '127.0.0.1', 8081, 'admin', 'adminadmin', '/dev/null/never', 0, 1,
  '{"category":"audiobooks"}', '2026-08-01 12:00:00', '', 1);
SELECT 'indexers=' || (SELECT count(*) FROM Indexers) || ' clients=' || (SELECT count(*) FROM DownloadClientConfigurations);
-- The base fixture's profiles carry NULL list columns, which the .620 build cannot read
-- (GET /library 500s in QualityProfileRepository.GetAllAsync). Give them production-shaped
-- values: profile 1 (default, used by every seeded book) allows M4B/MP3 with cutoff M4B.
UPDATE QualityProfiles SET
  Qualities = '[{"Quality":"M4B","Allowed":true,"Priority":0,"Codec":"AAC","Bitrate":null},{"Quality":"MP3 320kbps","Allowed":true,"Priority":1,"Codec":"MP3","Bitrate":320}]',
  PreferredFormats = '["m4b","mp3"]', PreferredWords = '[]', MustNotContain = '[]', MustContain = '[]',
  PreferredLanguages = '["English"]', Description = COALESCE(Description, ''),
  CutoffQuality = CASE WHEN Id = 1 THEN 'M4B' ELSE CutoffQuality END,
  UpgradeAllowed = CASE WHEN Id = 1 THEN 1 ELSE UpgradeAllowed END;
