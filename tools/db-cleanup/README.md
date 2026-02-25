Listenarr DB cleanup tool

Usage:

dotnet run --project tools/db-cleanup -- [path/to/listenarr.db]

The tool will create a timestamped backup and normalize common JSON-backed columns
(`Authors`, `Genres`, `Tags`, `Narrators`, `AuthorAsins`, `Isbn`) by wrapping primitive
JSON values into arrays when safe.
