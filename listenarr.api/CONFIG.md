# Listenarr API Configuration

Listenarr stores runtime configuration and user data beneath the active content root.

- Local development: launch profiles set `LISTENARR_CONTENT_ROOT=../.env/development`, so runtime files live under `.env/development/config/`.
- Docker: the content root is `/app`, so runtime files live under `/app/config/`. Mount `/app/config` as a persistent volume.
- Tests and specialized hosts can override the content root or SQLite path to avoid shared state.

## Config Folder Structure

```text
config/
|-- appsettings/
|   `-- appsettings.json
|-- backups/
|   |-- .staging/
|   |-- manual/
|   `-- migration/
|-- cache/
|   `-- images/
|       |-- authors/
|       |-- library/
|       |-- series/
|       `-- temp/
|-- database/
|   `-- listenarr.db
|-- dataprotection-keys/
|-- ffmpeg/
|-- logs/
|   `-- listenarr-YYYYMMDD.log
`-- temp/
    `-- downloads/
```

## Directory Purposes

### appsettings/

Contains external application configuration:

- `appsettings.json` - Runtime overrides such as logging levels.

On first startup, the API creates `config/appsettings/appsettings.json` under the active content root if it does not exist. In local development that file is `.env/development/config/appsettings/appsettings.json`.

### backups/

Contains zip archives of the database and `config.json`, one subdirectory per reason:

- `migration/` - Taken automatically at startup when a release has schema changes to apply, and
  only then. Removed by the retention sweep once they pass the configured age.
- `manual/` - Taken on request from the System screen. Never removed automatically.

Each archive holds `listenarr.db`, `config.json` if one exists, and an `INFO` file naming the
version and the moment it was taken. The database copy is made with SQLite's online backup API, so
nothing has to stop while it runs.

`.staging/` is where an archive is assembled. It is emptied as each backup finishes, and an archive
only appears under `manual/` or `migration/` once it is complete, so a backup interrupted halfway
leaves nothing that looks like a usable one.

Archives carry the API key, indexer keys, download client credentials and the admin password hash.
Nothing serves them over HTTP: the API lists their names, sizes and ages and no more. Treat the
files themselves the way you would treat the database.

Retention is set on the General settings screen and defaults to 28 days, matching the rest of the
*arr family. Zero keeps everything.

### cache/images/

Stores cached book cover, author, and series images:

- `temp/` - Temporary cache for newly downloaded images.
- `library/` - Permanent book cover cache.
- `authors/` - Permanent author image cache.
- `series/` - Permanent series image cache.

### database/

Contains SQLite database files:

- `listenarr.db` - Main application database.

### dataprotection-keys/

Contains ASP.NET Core data protection keys used for authentication cookies and related protected payloads.

### ffmpeg/

Contains downloaded FFmpeg/FFprobe binaries and associated license notices.

### logs/

Contains application log files:

- Daily log files named `listenarr-YYYYMMDD.log`.
- Automatically cleaned up by the application according to retention settings.

### temp/downloads/

Temporary storage for DDL (Direct Download Link) files:

- Files download here first, then get processed and moved to final locations.
- Automatically cleaned up after successful processing or after retention expires.

## Important Notes

- The `config/` folder contains user-specific data and should be backed up.
- Before applying schema changes, Listenarr backs the database up into `config/backups/migration/`,
  and refuses to start if that backup cannot be written, since the schema change cannot be undone.
  Set `LISTENARR_BACKUP_BEFORE_MIGRATIONS=false` (or `Listenarr:BackupBeforeMigrations` in
  configuration) to skip it. It is an environment variable rather than a setting because an
  instance that will not start cannot show you a settings screen.
- Database files are SQLite-based and contain application data.
- Temp and cache directories are automatically managed by background services.
- Log files can contain sensitive diagnostic data; restrict access accordingly.

## ExternalRequests Configuration

The application supports an `ExternalRequests` configuration section that controls retry behavior for external scrapes (Amazon/Audible) and a named `us` HttpClient used for US-domain retries when necessary.

Example `config/appsettings/appsettings.json`:

```json
{
  "ExternalRequests": {
    "PreferUsDomain": true
  }
}
```

- `PreferUsDomain`: when true, services attempt a retry using a `.com` (US) domain if a localized, redirected, or noisy page is detected.

## Logging Configuration

Set `LISTENARR_LOG_LEVEL` for runtime overrides, or edit `config/appsettings/appsettings.json` under the active content root and set `Serilog:MinimumLevel:Default` or `Logging:LogLevel:Default`.
