using System.Globalization;
using System.Text;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Api.Services
{
    public class AuthorMonitoringService : IAuthorMonitoringService
    {
        private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
        {
            "all",
            "english",
            "spanish",
            "german",
            "hungarian",
            "french",
            "polish",
            "italian",
            "russian"
        };

        private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["all"] = "all",
            ["any"] = "all",
            ["english"] = "english",
            ["en"] = "english",
            ["en-us"] = "english",
            ["en-uk"] = "english",
            ["en-gb"] = "english",
            ["en-ca"] = "english",
            ["en-au"] = "english",
            ["en-in"] = "english",
            ["spanish"] = "spanish",
            ["es"] = "spanish",
            ["spa"] = "spanish",
            ["es-es"] = "spanish",
            ["german"] = "german",
            ["de"] = "german",
            ["deu"] = "german",
            ["ger"] = "german",
            ["de-de"] = "german",
            ["deutsch"] = "german",
            ["hungarian"] = "hungarian",
            ["hu"] = "hungarian",
            ["hun"] = "hungarian",
            ["magyar"] = "hungarian",
            ["french"] = "french",
            ["fr"] = "french",
            ["fra"] = "french",
            ["fre"] = "french",
            ["fr-fr"] = "french",
            ["polish"] = "polish",
            ["pl"] = "polish",
            ["pol"] = "polish",
            ["pl-pl"] = "polish",
            ["italian"] = "italian",
            ["it"] = "italian",
            ["ita"] = "italian",
            ["it-it"] = "italian",
            ["russian"] = "russian",
            ["ru"] = "russian",
            ["rus"] = "russian",
            ["ru-ru"] = "russian"
        };

        private readonly ListenArrDbContext _dbContext;
        private readonly IAuthorCatalogService _authorCatalogService;
        private readonly ILibraryAddService _libraryAddService;
        private readonly ILogger<AuthorMonitoringService> _logger;

        public AuthorMonitoringService(
            ListenArrDbContext dbContext,
            IAuthorCatalogService authorCatalogService,
            ILibraryAddService libraryAddService,
            ILogger<AuthorMonitoringService> logger)
        {
            _dbContext = dbContext;
            _authorCatalogService = authorCatalogService;
            _libraryAddService = libraryAddService;
            _logger = logger;
        }

        public async Task<MonitoredAuthor?> GetMonitoredAuthorAsync(
            string name,
            string region,
            string language,
            CancellationToken cancellationToken = default)
        {
            var normalizedName = NormalizeAuthorName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            var normalizedRegion = NormalizeRegion(region);
            var normalizedLanguage = NormalizeLanguage(language, fallbackToEnglish: true);

            return await _dbContext.MonitoredAuthors
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    author => author.AuthorNameNormalized == normalizedName &&
                              author.Region == normalizedRegion &&
                              author.Language == normalizedLanguage,
                    cancellationToken);
        }

        public async Task<MonitorAuthorOperationResult> MonitorAuthorAsync(
            MonitorAuthorRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var normalizedName = NormalizeAuthorName(request.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException("Author name is required.", nameof(request));
            }

            var normalizedRegion = NormalizeRegion(request.Region);
            var normalizedLanguage = NormalizeLanguage(request.Language, fallbackToEnglish: true);
            var displayName = request.Name.Trim();

            var monitoredAuthor = await _dbContext.MonitoredAuthors.FirstOrDefaultAsync(
                author => author.AuthorNameNormalized == normalizedName &&
                          author.Region == normalizedRegion &&
                          author.Language == normalizedLanguage,
                cancellationToken);

            if (monitoredAuthor == null)
            {
                monitoredAuthor = new MonitoredAuthor
                {
                    AuthorName = displayName,
                    AuthorNameNormalized = normalizedName,
                    AuthorAsin = NormalizeOptionalIdentifier(request.Asin),
                    Region = normalizedRegion,
                    Language = normalizedLanguage,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.MonitoredAuthors.Add(monitoredAuthor);
            }
            else
            {
                monitoredAuthor.AuthorName = displayName;
                monitoredAuthor.AuthorAsin = NormalizeOptionalIdentifier(request.Asin) ?? monitoredAuthor.AuthorAsin;
                monitoredAuthor.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var syncResult = await SyncAuthorInternalAsync(monitoredAuthor, cancellationToken);
            return new MonitorAuthorOperationResult
            {
                MonitoredAuthor = monitoredAuthor,
                SyncResult = syncResult
            };
        }

        public async Task<bool> UnmonitorAuthorAsync(int id, CancellationToken cancellationToken = default)
        {
            var monitoredAuthor = await _dbContext.MonitoredAuthors.FirstOrDefaultAsync(
                author => author.Id == id,
                cancellationToken);

            if (monitoredAuthor == null)
            {
                return false;
            }

            _dbContext.MonitoredAuthors.Remove(monitoredAuthor);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<MonitorAuthorSyncResult> SyncAuthorAsync(int id, CancellationToken cancellationToken = default)
        {
            var monitoredAuthor = await _dbContext.MonitoredAuthors.FirstOrDefaultAsync(
                author => author.Id == id,
                cancellationToken);

            if (monitoredAuthor == null)
            {
                return new MonitorAuthorSyncResult
                {
                    ErrorMessage = "Monitored author not found.",
                    FailedCount = 1,
                    Succeeded = false
                };
            }

            return await SyncAuthorInternalAsync(monitoredAuthor, cancellationToken);
        }

        public async Task<int> SyncDueAuthorsAsync(CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow.AddDays(-1);
            var dueAuthors = await _dbContext.MonitoredAuthors
                .Where(author => author.LastCheckedAt == null || author.LastCheckedAt < cutoff)
                .OrderBy(author => author.LastCheckedAt ?? DateTime.MinValue)
                .ToListAsync(cancellationToken);

            var syncedCount = 0;
            foreach (var author in dueAuthors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await SyncAuthorInternalAsync(author, cancellationToken);
                if (result.Succeeded)
                {
                    syncedCount++;
                }
            }

            return syncedCount;
        }

        private async Task<MonitorAuthorSyncResult> SyncAuthorInternalAsync(
            MonitoredAuthor monitoredAuthor,
            CancellationToken cancellationToken)
        {
            var result = new MonitorAuthorSyncResult();

            try
            {
                var catalog = await _authorCatalogService.GetCatalogAsync(
                    monitoredAuthor.AuthorName,
                    monitoredAuthor.Region,
                    limit: 500,
                    language: null,
                    cancellationToken: cancellationToken);

                if (catalog == null)
                {
                    result.Succeeded = false;
                    result.ErrorMessage = "Author catalog could not be loaded.";
                    monitoredAuthor.LastError = TruncateError(result.ErrorMessage);
                    monitoredAuthor.LastCheckedAt = DateTime.UtcNow;
                    monitoredAuthor.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return result;
                }

                monitoredAuthor.AuthorAsin = NormalizeOptionalIdentifier(catalog.Author.Asin) ?? monitoredAuthor.AuthorAsin;

                var existingLibrary = await _dbContext.Audiobooks
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                foreach (var book in catalog.Books)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!ShouldIncludeBookForLanguage(book, monitoredAuthor.Language))
                    {
                        continue;
                    }

                    if (FindExistingLibraryMatch(book, existingLibrary) != null)
                    {
                        result.ExistingCount++;
                        continue;
                    }

                    var addResult = await _libraryAddService.AddToLibraryAsync(
                        new LibraryAddOperationRequest
                        {
                            Metadata = MapToMetadata(book),
                            Monitored = true,
                            AutoSearch = false,
                            HistorySource = "AuthorMonitoring",
                            HistoryMessage =
                                $"Audiobook '{book.Title}' added automatically from monitored author '{monitoredAuthor.AuthorName}'"
                        },
                        cancellationToken);

                    if (addResult.Added && addResult.Audiobook != null)
                    {
                        result.AddedCount++;
                        existingLibrary.Add(addResult.Audiobook);
                        continue;
                    }

                    if (addResult.AlreadyExists)
                    {
                        result.ExistingCount++;
                        if (addResult.Audiobook != null)
                        {
                            existingLibrary.Add(addResult.Audiobook);
                        }
                        continue;
                    }

                    result.FailedCount++;
                }

                monitoredAuthor.LastCheckedAt = DateTime.UtcNow;
                monitoredAuthor.LastSuccessfulSyncAt = monitoredAuthor.LastCheckedAt;
                monitoredAuthor.LastError = null;
                monitoredAuthor.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                result.Succeeded = true;
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(
                    ex,
                    "Failed to sync monitored author '{Author}' ({Region}/{Language})",
                    monitoredAuthor.AuthorName,
                    monitoredAuthor.Region,
                    monitoredAuthor.Language);

                result.Succeeded = false;
                result.ErrorMessage = ex.Message;
                result.FailedCount++;
                monitoredAuthor.LastCheckedAt = DateTime.UtcNow;
                monitoredAuthor.LastError = TruncateError(ex.Message);
                monitoredAuthor.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return result;
            }
        }

        private static AudibleBookMetadata MapToMetadata(AudimetaSearchResult book)
        {
            var primarySeries = book.Series?.FirstOrDefault();
            var runtime = book.LengthMinutes ?? book.RuntimeLengthMin ?? book.RuntimeMinutes;

            return new AudibleBookMetadata
            {
                Asin = book.Asin,
                Title = book.Title,
                Subtitle = book.Subtitle,
                Authors = (book.Authors ?? new List<AudimetaAuthor>())
                    .Select(author => author.Name)
                    .Where(author => !string.IsNullOrWhiteSpace(author))
                    .Cast<string>()
                    .ToList(),
                ImageUrl = book.ImageUrl,
                Runtime = runtime,
                Language = book.Language,
                Publisher = book.Publisher,
                Narrators = (book.Narrators ?? new List<AudimetaNarrator>())
                    .Select(narrator => narrator.Name)
                    .Where(narrator => !string.IsNullOrWhiteSpace(narrator))
                    .Cast<string>()
                    .ToList(),
                Genres = (book.Genres ?? new List<AudimetaGenre>())
                    .Select(genre => genre.Name)
                    .Where(genre => !string.IsNullOrWhiteSpace(genre))
                    .Cast<string>()
                    .ToList(),
                Series = primarySeries?.Name,
                SeriesNumber = primarySeries?.Position,
                PublishedDate = book.ReleaseDate,
                PublishYear = TryExtractPublishYear(book.ReleaseDate),
                Isbn = string.IsNullOrWhiteSpace(book.Isbn) ? new List<string>() : new List<string> { book.Isbn },
                Source = "Audimeta"
            };
        }

        private static Audiobook? FindExistingLibraryMatch(
            AudimetaSearchResult book,
            IEnumerable<Audiobook> libraryBooks)
        {
            var asin = NormalizeIdentifier(book.Asin);
            if (!string.IsNullOrWhiteSpace(asin))
            {
                var asinMatch = libraryBooks.FirstOrDefault(candidate =>
                    NormalizeIdentifier(candidate.Asin) == asin);
                if (asinMatch != null)
                {
                    return asinMatch;
                }
            }

            var isbn = NormalizeIdentifier(book.Isbn);
            if (!string.IsNullOrWhiteSpace(isbn))
            {
                var isbnMatch = libraryBooks.FirstOrDefault(candidate =>
                    (candidate.Isbn ?? new List<string>()).Any(value => NormalizeIdentifier(value) == isbn));
                if (isbnMatch != null)
                {
                    return isbnMatch;
                }
            }

            var titleAuthorKey = BuildTitleAuthorKey(
                book.Title,
                (book.Authors ?? new List<AudimetaAuthor>())
                    .Select(author => author.Name)
                    .Where(author => !string.IsNullOrWhiteSpace(author))
                    .Cast<string>()
                    .ToList());

            if (string.IsNullOrWhiteSpace(titleAuthorKey))
            {
                return null;
            }

            return libraryBooks.FirstOrDefault(candidate =>
                BuildTitleAuthorKey(candidate.Title, candidate.Authors) == titleAuthorKey);
        }

        private static bool ShouldIncludeBookForLanguage(AudimetaSearchResult book, string preferredLanguage)
        {
            if (string.Equals(preferredLanguage, "all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedBookLanguage = NormalizeLanguage(book.Language, fallbackToEnglish: false);
            return string.Equals(normalizedBookLanguage, preferredLanguage, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAuthorName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else if (char.IsWhiteSpace(character))
                {
                    builder.Append(' ');
                }
            }

            return string.Join(
                ' ',
                builder.ToString()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizeRegion(string? region)
        {
            return AudiobookIdentifierNormalizer.NormalizeRegion(region) ?? "us";
        }

        private static string NormalizeLanguage(string? language, bool fallbackToEnglish)
        {
            var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallbackToEnglish ? "english" : string.Empty;
            }

            if (LanguageAliases.TryGetValue(normalized, out var alias))
            {
                return alias;
            }

            if (SupportedLanguages.Contains(normalized))
            {
                return normalized;
            }

            return fallbackToEnglish ? "english" : string.Empty;
        }

        private static string? NormalizeOptionalIdentifier(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : new string(value.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string NormalizeIdentifier(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string BuildTitleAuthorKey(string? title, IEnumerable<string>? authors)
        {
            var normalizedTitle = NormalizeAuthorName(title);
            var normalizedAuthors = string.Join(
                "|",
                (authors ?? Enumerable.Empty<string>())
                    .Select(NormalizeAuthorName)
                    .Where(author => !string.IsNullOrWhiteSpace(author))
                    .OrderBy(author => author));

            return string.IsNullOrWhiteSpace(normalizedTitle) && string.IsNullOrWhiteSpace(normalizedAuthors)
                ? string.Empty
                : $"{normalizedTitle}::{normalizedAuthors}";
        }

        private static string? TryExtractPublishYear(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = System.Text.RegularExpressions.Regex.Match(value, "\\d{4}");
            return match.Success ? match.Value : null;
        }

        private static string? TruncateError(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.Length <= 2048 ? value : value[..2048];
        }
    }
}
