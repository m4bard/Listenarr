using System.ComponentModel.DataAnnotations.Schema;
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Configuration
{
    public class ApplicationSettings
    {
        public int Version { get; set; }
        public int Id { get; set; } = 1; // Singleton pattern - only one settings record
        public string OutputPath { get; set; } = string.Empty;

        // Folder naming pattern (base directory structure)
        // Available variables:
        // {Author} - Audiobook author
        // {Narrator} - Narrator name(s)
        // {Series} - Series name (if applicable)
        // {SeriesNumber} - Position in series (e.g., "1", "2")
        // {Title} - Book/audiobook title
        // {Subtitle} - Book subtitle
        // {Edition} - User-defined edition label
        // {Publisher} - Publisher name
        // {Language} - Metadata language
        // {Asin} - Audible ASIN
        // {Year} - Publication year
        public string FolderNamingPattern { get; set; } = "{Author}/{Series}/{Title}";

        // File naming pattern for SINGLE-FILE imports (one audio file per audiobook)
        // Available variables:
        // {Author} - Audiobook author
        // {Narrator} - Narrator name(s)
        // {Series} - Series name (if applicable)
        // {SeriesNumber} - Position in series (e.g., "1", "2")
        // {Title} - Book/audiobook title
        // {Subtitle} - Book subtitle
        // {Edition} - User-defined edition label
        // {Publisher} - Publisher name
        // {Language} - Metadata language
        // {Asin} - Audible ASIN
        // {Year} - Publication year
        // {Quality} - Audio quality (e.g., "64kbps mp3")
        public string FileNamingPattern { get; set; } = "{Title}";

        // File naming pattern for MULTI-FILE imports (multiple audio files per audiobook)
        // Use {DiskNumber} or {DiskNumber:00}, {ChapterNumber} or {ChapterNumber:00} to differentiate files
        // Available variables:
        // {Author} - Audiobook author
        // {Narrator} - Narrator name(s)
        // {Series} - Series name (if applicable)
        // {SeriesNumber} - Position in series (e.g., "1", "2")
        // {Title} - Book/audiobook title
        // {Subtitle} - Book subtitle
        // {Edition} - User-defined edition label
        // {Publisher} - Publisher name
        // {Language} - Metadata language
        // {Asin} - Audible ASIN
        // {DiskNumber} or {DiskNumber:00} - Disk/part number (00 = zero-padded)
        // {ChapterNumber} or {ChapterNumber:00} - Chapter number (00 = zero-padded)
        // {Year} - Publication year
        // {Quality} - Audio quality (e.g., "64kbps mp3")
        public string MultiFileNamingPattern { get; set; } = "{Title}-{DiskNumber:00}-{ChapterNumber:00}";

        public bool EnableMetadataProcessing { get; set; } = true;
        /// <summary>
        /// Dead flag, kept only so this change stays a single additive migration.
        ///
        /// Persisted since the Settings page was added and never read by anything. Its
        /// stored value is true on every existing instance because that was the property
        /// initialiser, not because an operator chose it, which is why the embedding
        /// behaviour below is governed by a new column instead of this one. Dropping it is
        /// a separate mechanical change.
        /// </summary>
        public bool EnableCoverArtDownload { get; set; } = true;

        /// <summary>
        /// Embed cover artwork into audio files as they are imported.
        ///
        /// This replaces EnableCoverArtDownload, which was persisted from the day the
        /// Settings page was added and never read by anything. Reusing that column would
        /// have inherited a stored true on every existing instance, since the value came
        /// from a property initialiser rather than from anyone choosing it, and embedding
        /// rewrites the audio file. A new column starts false for everyone, so the
        /// behaviour is opt-in on upgrade rather than something an operator discovers.
        /// </summary>
        public bool EmbedCoverArtInAudioFiles { get; set; } = false;
        public string AudnexusApiUrl { get; set; } = "https://api.audnex.us";
        public int MaxConcurrentDownloads { get; set; } = 3;
        public int PollingIntervalSeconds { get; set; } = 30;
        public bool EnableNotifications { get; set; } = false;

        // Audio file extensions FileUtils.IsAudioFile treats as recognized. Defaults to the same
        // set FileUtils.AudioExtensions has always used, so an untouched setting reproduces
        // today's hardcoded behavior exactly.
        public List<string> AllowedFileExtensions
        {
            get
            {
                return [.. FileUtils.NormalizeExtensions(field)];
            }
            set;
        } = [.. FileUtils.AudioExtensions];

        // Number of seconds a download must be observed in the client as "complete" before
        // the system will finalize it (stability window). Keeping a short default (10s)
        // avoids accidental long delays while still allowing this to be tuned by admins.
        public int DownloadCompletionStabilitySeconds { get; set; } = 10;

        // Retry/backoff settings for when a finalized download has no discoverable source file
        // at the time of finalization. These control how the monitor schedules retries when
        // files are still being extracted/moved by the client.
        public int MissingSourceRetryInitialDelaySeconds { get; set; } = 30;
        public int MissingSourceMaxRetries { get; set; } = 3;

        // Action to take when a download completes
        public FileAction CompletedFileAction { get; set; } = FileAction.Copy;

        // Whether to extract archive files (zip/rar/7z) when discovered in a completed download
        public bool ExtractArchives { get; set; } = true;

        // Maximum number of concurrent ffprobe processes during an unmatched scan.
        // Lower values reduce NAS/disk I/O pressure; higher values speed up large libraries.
        public int UnmatchedScanConcurrency { get; set; } = 2;

        // Whether to show completed downloads from external clients in the Activity view
        public bool ShowCompletedExternalDownloads { get; set; } = false;

        // Number of days to retain action history. Zero keeps history indefinitely.
        public int HistoryRetentionDays { get; set; } = 0;

        /// <summary>
        /// Number of days the daily housekeeping sweep keeps a terminal row in the append-only
        /// journal and cache tables. Zero disables the sweep and keeps every row indefinitely.
        /// </summary>
        /// <remarks>
        /// Thirty, and zero to disable, is Prowlarr's HistoryCleanupDays exactly
        /// (src/NzbDrone.Core/Configuration/ConfigService.cs:80), which is the only
        /// operator-configurable window over a database table anywhere in the family. Zero
        /// already means unlimited in this codebase as well, so the two agree.
        /// </remarks>
        public int HousekeepingRetentionDays { get; set; } = 30;

        /// <summary>
        /// When true the housekeeping sweep evaluates every predicate and logs how many rows it
        /// would remove, and removes none. Shipped on, so an upgraded install lands in a state
        /// that writes nothing until an operator has read a cycle's counts.
        /// </summary>
        public bool HousekeepingDryRun { get; set; } = true;

        // Failed download handling settings
        public bool FailedDownloadHandlingEnabled { get; set; } = true;
        public bool FailedDownloadAutoSearch { get; set; } = false;
        public List<string> ImportBlacklistExtensions
        {
            get
            {
                return [.. FileUtils.NormalizeExtensions(field)];
            }
            set;
        } = [];

        /// <summary>
        /// Webhook URL for sending notifications (legacy single webhook).
        /// </summary>
        public string WebhookUrl { get; set; } = string.Empty;

        /// <summary>
        /// List of enabled notification triggers (legacy).
        /// </summary>
        public List<string> EnabledNotificationTriggers { get; set; } = new() { "book-added", "book-downloading", "book-available", "book-completed" };

        /// <summary>
        /// Multiple webhooks configuration (new format).
        /// </summary>
        public List<WebhookConfiguration>? Webhooks { get; set; }

        /// <summary>
        /// Configured custom scripts. Each entry is one executable run on the channels it is
        /// enabled for.
        /// </summary>
        public List<CustomScriptConfiguration>? CustomScripts { get; set; }

        // Optional admin credentials submitted from the UI when saving settings.
        // These are NOT mapped to the ApplicationSettings table; they are used to create/update
        // a User record in the Users table via the ConfigurationService.
        /// <summary>
        /// Admin username submitted from the UI (not persisted to the settings table).
        /// </summary>
        [NotMapped]
        public string? AdminUsername { get; set; }

        [NotMapped]
        public string? AdminPassword { get; set; }

        // Discord bot integration settings (used by external Discord bot or interactions)
        /// <summary>
        /// Enable (persisted) Discord bot integration settings. The bot process may read these settings to
        /// automatically login / register commands.
        /// </summary>
        public bool DiscordBotEnabled { get; set; } = false;

        /// <summary>
        /// Discord Application (Client) ID for registering application commands.
        /// </summary>
        public string? DiscordApplicationId { get; set; }

        /// <summary>
        /// Optional Guild ID to register commands in a single guild for faster deployment during testing.
        /// </summary>
        public string? DiscordGuildId { get; set; }

        /// <summary>
        /// Optional Channel ID to restrict bot interactions to a single channel. If set, the bot
        /// will ignore interactions from other channels unless the bot configuration allows it.
        /// </summary>
        public string? DiscordChannelId { get; set; }

        /// <summary>
        /// Bot token used by an external bot process to authenticate to Discord.
        /// NOTE: Storing tokens in the database has security implications. Consider using a secrets manager
        /// for production deployments.
        /// </summary>
        public string? DiscordBotToken { get; set; }

        /// <summary>
        /// Saved Prowlarr host/URL used by the indexer import flow.
        /// </summary>
        public string? ProwlarrUrl { get; set; }

        /// <summary>
        /// Optional saved Prowlarr port used by the indexer import flow.
        /// </summary>
        public int? ProwlarrPort { get; set; }

        /// <summary>
        /// Encrypted Prowlarr API key used by the indexer import flow.
        /// </summary>
        public string? ProwlarrApiKeyEncrypted { get; set; }

        /// <summary>
        /// Optional Prowlarr tag filter used by the indexer import flow.
        /// When set, only indexers with this tag are imported and the audiobook category filter is bypassed.
        /// </summary>
        public string? ProwlarrTagFilter { get; set; }

        /// <summary>
        /// Primary command group name (e.g. "request"). We'll create a slash command with this group and
        /// a subcommand for specific request types (e.g. "audiobook").
        /// </summary>
        public string? DiscordCommandGroupName { get; set; } = "request";

        /// <summary>
        /// Subcommand name for audiobooks (e.g. "audiobook"). Combined with the group this yields "/request audiobook".
        /// </summary>
        public string? DiscordCommandSubcommandName { get; set; } = "audiobook";

        /// <summary>
        /// Optional custom username for the Discord bot. If set, the bot will attempt to change its username.
        /// </summary>
        public string? DiscordBotUsername { get; set; }

        /// <summary>
        /// Optional avatar URL for the Discord bot. If set, the bot will attempt to change its avatar.
        /// </summary>
        public string? DiscordBotAvatar { get; set; }

        // Search settings
        /// <summary>
        /// Enable searching Amazon as part of intelligent searches.
        /// </summary>
        public bool EnableAmazonSearch { get; set; } = true;

        /// <summary>
        /// Enable searching Audible as part of intelligent searches.
        /// </summary>
        public bool EnableAudibleSearch { get; set; } = true;

        /// <summary>
        /// Enable using OpenLibrary augmentation during intelligent searches.
        /// </summary>
        public bool EnableOpenLibrarySearch { get; set; } = true;

        /// <summary>
        /// Preferred default Audible/Audible market region for Add New searches.
        /// </summary>
        public string DefaultSearchRegion { get; set; } = "us";

        /// <summary>
        /// How many indexers one search may query at the same time. 4 is the ceiling that was
        /// hardcoded before this became a setting, so an upgraded install searches exactly as it
        /// did. Lower it when a local Jackett or Prowlarr proxy, or an indexer behind it, wants
        /// gentler treatment.
        /// </summary>
        public int MaxConcurrentIndexerSearches { get; set; } = 4;

        /// <summary>
        /// Preferred default language filter for Add New searches.
        /// </summary>
        public string DefaultSearchLanguage { get; set; } = "english";

        // Scheduled provider-metadata refresh. The interval says how often the walk wakes up;
        // the staleness age is what actually governs how often a given book is touched.
        //
        // On, which is only safe because an upgraded row does not arrive due. Startup gives
        // every row that has no refresh timestamp the time of that backfill, so an upgraded
        // library gets a full staleness window before any of it is due, and then ages into the
        // queue oldest first the way books added after the upgrade do.
        public bool MetadataRefreshEnabled { get; set; } = true;
        public int MetadataRefreshIntervalHours { get; set; } = 24;
        public int MetadataRefreshStaleAfterDays { get; set; } = 30;

        // Deliberately timid. The provider publishes no rate limit, so the shipped budget stays
        // well under any plausible ceiling; pushback narrows it further at runtime.
        public int MetadataRefreshRequestsPerHour { get; set; } = 60;
        public int MetadataRefreshMinimumSpacingMs { get; set; } = 1000;

        // The author identity repair pass. Three states out of two switches, and the order they
        // are reached in is the point.
        //
        // Off, shipped, so an upgrade costs nothing and nothing is rewritten by surprise. Turned
        // on, it previews: DryRun stays true, so the pass resolves every name it examines and
        // says what it would change without changing anything, and it can be read twice and give
        // the same answer. Only when the operator turns DryRun off does it write.
        //
        // A pass that rewrites who an author is has to be seen before it runs, which is why the
        // preview is the state you land in rather than a flag you have to find. The family's
        // housekeepers do not need this: every Readarr housekeeper is a local recomputation, and
        // none of them asks a provider who somebody is.
        public bool AuthorIdentityRepairEnabled { get; set; }
        public bool AuthorIdentityRepairDryRun { get; set; } = true;

        // Daily, which is HousekeepingCommand's interval in Readarr's TaskManager and the same
        // one the metadata walk uses here.
        public int AuthorIdentityRepairIntervalHours { get; set; } = 24;

        // Deliberately small. Every row examined costs at least one provider request out of the
        // same hourly budget the metadata walk spends, so an unbounded pass would starve the
        // ordinary refresh for as long as it ran. Rows it does not reach stay at the head of the
        // queue for the next run.
        public int AuthorIdentityRepairMaxRowsPerRun { get; set; } = 25;
    }
}
