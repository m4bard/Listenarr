using static Listenarr.Api.Services.FileMover;

namespace Listenarr.Api.Services
{
    public interface IFileMover
    {
        Task<bool> MoveFileAsync(string sourceFile, string destFile);
        Task<bool> CopyFileAsync(string sourceFile, string destFile);
        Task<bool> HardlinkFileAsync(string sourceFile, string destFile);
        Task<bool> MoveDirectoryAsync(string sourceDir, string destDir);
        Task<bool> CopyDirectoryAsync(string sourceDir, string destDir);

        /// <summary>
        /// Perform the given action on the given file
        /// </summary>
        /// <param name="action">What we want to do with the file</param>
        /// <param name="source">File</param>
        /// <param name="destination">Optional destination of the action</param>
        /// <param name="usedDestinations">To remove probably</param>
        Task PerformActionOn(FileAction action, string source, string? destination = null, HashSet<string>? usedDestinations = null);
    }
}
