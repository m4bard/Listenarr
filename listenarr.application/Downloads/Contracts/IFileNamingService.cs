
namespace Listenarr.Application.Downloads.Contracts
{
    /// <summary>
    /// Generates file paths using configured naming patterns
    /// </summary>
    public interface IFileNamingService
    {
        /// <summary>
        /// Apply the configured file naming pattern to generate the final file path
        /// </summary>
        /// <param name="metadata">Audiobook metadata</param>
        /// <param name="originalExtension">File extension (e.g., ".m4b", ".mp3")</param>
        /// <returns>Full file path using the naming pattern</returns>
        Task<string> GenerateFilePathAsync(AudioMetadata metadata, string originalExtension = ".m4b");

        /// <summary>
        /// Apply the configured file naming pattern to generate the final file path with a specific output path
        /// </summary>
        /// <param name="metadata">Audiobook metadata</param>
        /// <param name="outputPath">Specific output path to use</param>
        /// <param name="originalExtension">File extension (e.g., ".m4b", ".mp3")</param>
        /// <returns>Full file path using the naming pattern</returns>
        Task<string> GenerateFilePathAsync(AudioMetadata metadata, string outputPath, string originalExtension = ".m4b");

        /// <summary>
        /// Parse a naming pattern and replace variables with actual values
        /// </summary>
        /// <param name="pattern">The naming pattern template</param>
        /// <param name="variables">Dictionary of variable values</param>
        /// <param name="treatAsFilename">Whether to treat as filename (sanitize invalid chars)</param>
        /// <returns>Final path with variables replaced</returns>
        string ApplyNamingPattern(string pattern, Dictionary<string, object> variables, bool treatAsFilename = false); // FIXME: Should be private
        string ApplyNamingPattern(string pattern, AudioMetadata metadata, bool treatAsFilename = false);
        string ApplyNamingPattern(string pattern, AudibleBookMetadata metadata, bool treatAsFilename = false);

        /// <summary>
        /// Render the folder, single-file and multi-file naming patterns against a fixed
        /// synthetic sample through the same code path as <see cref="GenerateFilePathAsync(AudioMetadata, string)"/>,
        /// so the settings screen shows what import and rename will actually write instead of
        /// an independent approximation of it.
        /// </summary>
        /// <param name="folderPattern">In-progress folder naming pattern, not necessarily saved.</param>
        /// <param name="filePattern">In-progress single-file naming pattern, not necessarily saved.</param>
        /// <param name="multiFilePattern">In-progress multi-file naming pattern, not necessarily saved.</param>
        NamingPatternPreview PreviewNamingPatterns(string? folderPattern, string? filePattern, string? multiFilePattern);
    }
}
