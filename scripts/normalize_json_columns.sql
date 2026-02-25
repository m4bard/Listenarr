hy PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- Show table info
PRAGMA table_info(Audiobooks);

-- Normalize JSON primitives (valid JSON but not array/object) into arrays
-- Authors
SELECT 'Authors_rows_to_fix' AS label, COUNT(*) AS cnt FROM Audiobooks WHERE Authors IS NOT NULL AND json_valid(Authors)=1 AND json_type(Authors) NOT IN ('array','object');
UPDATE Audiobooks SET Authors = '["' || json_extract(Authors, '$') || '"]' WHERE Authors IS NOT NULL AND json_valid(Authors)=1 AND json_type(Authors) NOT IN ('array','object');
SELECT 'Authors_changes', changes();

-- Genres
SELECT 'Genres_rows_to_fix' AS label, COUNT(*) AS cnt FROM Audiobooks WHERE Genres IS NOT NULL AND json_valid(Genres)=1 AND json_type(Genres) NOT IN ('array','object');
UPDATE Audiobooks SET Genres = '["' || json_extract(Genres, '$') || '"]' WHERE Genres IS NOT NULL AND json_valid(Genres)=1 AND json_type(Genres) NOT IN ('array','object');
SELECT 'Genres_changes', changes();

-- Tags
SELECT 'Tags_rows_to_fix' AS label, COUNT(*) AS cnt FROM Audiobooks WHERE Tags IS NOT NULL AND json_valid(Tags)=1 AND json_type(Tags) NOT IN ('array','object');
UPDATE Audiobooks SET Tags = '["' || json_extract(Tags, '$') || '"]' WHERE Tags IS NOT NULL AND json_valid(Tags)=1 AND json_type(Tags) NOT IN ('array','object');
SELECT 'Tags_changes', changes();

-- Narrators
SELECT 'Narrators_rows_to_fix' AS label, COUNT(*) AS cnt FROM Audiobooks WHERE Narrators IS NOT NULL AND json_valid(Narrators)=1 AND json_type(Narrators) NOT IN ('array','object');
UPDATE Audiobooks SET Narrators = '["' || json_extract(Narrators, '$') || '"]' WHERE Narrators IS NOT NULL AND json_valid(Narrators)=1 AND json_type(Narrators) NOT IN ('array','object');
SELECT 'Narrators_changes', changes();

-- AuthorAsins
SELECT 'AuthorAsins_rows_to_fix' AS label, COUNT(*) AS cnt FROM Audiobooks WHERE AuthorAsins IS NOT NULL AND json_valid(AuthorAsins)=1 AND json_type(AuthorAsins) NOT IN ('array','object');
UPDATE Audiobooks SET AuthorAsins = '["' || json_extract(AuthorAsins, '$') || '"]' WHERE AuthorAsins IS NOT NULL AND json_valid(AuthorAsins)=1 AND json_type(AuthorAsins) NOT IN ('array','object');
SELECT 'AuthorAsins_changes', changes();

-- Isbn
SELECT 'Isbn_rows_to_fix' AS label, COUNT(*) AS cnt FROM Audiobooks WHERE Isbn IS NOT NULL AND json_valid(Isbn)=1 AND json_type(Isbn) NOT IN ('array','object');
UPDATE Audiobooks SET Isbn = '["' || json_extract(Isbn, '$') || '"]' WHERE Isbn IS NOT NULL AND json_valid(Isbn)=1 AND json_type(Isbn) NOT IN ('array','object');
SELECT 'Isbn_changes', changes();

COMMIT;

-- Report any remaining valid-but-non-array rows (should be zero)
SELECT Id, 'Authors', json_type(Authors), Authors FROM Audiobooks WHERE Authors IS NOT NULL AND json_valid(Authors)=1 AND json_type(Authors) NOT IN ('array','object');
SELECT Id, 'Genres', json_type(Genres), Genres FROM Audiobooks WHERE Genres IS NOT NULL AND json_valid(Genres)=1 AND json_type(Genres) NOT IN ('array','object');
SELECT Id, 'Tags', json_type(Tags), Tags FROM Audiobooks WHERE Tags IS NOT NULL AND json_valid(Tags)=1 AND json_type(Tags) NOT IN ('array','object');
SELECT Id, 'Narrators', json_type(Narrators), Narrators FROM Audiobooks WHERE Narrators IS NOT NULL AND json_valid(Narrators)=1 AND json_type(Narrators) NOT IN ('array','object');
SELECT Id, 'AuthorAsins', json_type(AuthorAsins), AuthorAsins FROM Audiobooks WHERE AuthorAsins IS NOT NULL AND json_valid(AuthorAsins)=1 AND json_type(AuthorAsins) NOT IN ('array','object');
SELECT Id, 'Isbn', json_type(Isbn), Isbn FROM Audiobooks WHERE Isbn IS NOT NULL AND json_valid(Isbn)=1 AND json_type(Isbn) NOT IN ('array','object');

-- List some invalid-json rows for manual review
SELECT Id, 'Authors_invalid', json_valid(Authors), Authors FROM Audiobooks WHERE Authors IS NOT NULL AND json_valid(Authors)=0 LIMIT 50;
SELECT Id, 'Genres_invalid', json_valid(Genres), Genres FROM Audiobooks WHERE Genres IS NOT NULL AND json_valid(Genres)=0 LIMIT 50;
SELECT Id, 'Tags_invalid', json_valid(Tags), Tags FROM Audiobooks WHERE Tags IS NOT NULL AND json_valid(Tags)=0 LIMIT 50;
SELECT Id, 'Narrators_invalid', json_valid(Narrators), Narrators FROM Audiobooks WHERE Narrators IS NOT NULL AND json_valid(Narrators)=0 LIMIT 50;
SELECT Id, 'AuthorAsins_invalid', json_valid(AuthorAsins), AuthorAsins FROM Audiobooks WHERE AuthorAsins IS NOT NULL AND json_valid(AuthorAsins)=0 LIMIT 50;
SELECT Id, 'Isbn_invalid', json_valid(Isbn), Isbn FROM Audiobooks WHERE Isbn IS NOT NULL AND json_valid(Isbn)=0 LIMIT 50;
