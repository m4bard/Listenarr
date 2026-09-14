/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */


namespace Listenarr.Domain.Downloads
{
    public enum ImportSourceDisposition
    {
        Unknown,
        Unchanged,
        Retained,
        Retired
    }

    /// <summary>
    /// A stable, non-leaking classification of why an import failed. Populated at the
    /// point the failure is created (where the real exception or blocked-action detail is
    /// still available), so that anything built from an <see cref="ImportResult"/> for an
    /// external-facing surface (the History API) can show a fixed sentence instead of raw
    /// exception text or filesystem paths. See issue #975.
    /// </summary>
    public enum ImportFailureClass
    {
        /// <summary>Not a failure, or no classification recorded.</summary>
        None = 0,
        PermissionDenied,
        DestinationUnavailable,
        DestinationConflict,
        PathTooLong,
        FileInUse,
        /// <summary>An I/O error occurred that does not fall into a more specific class.</summary>
        IoError,
        /// <summary>Rejected by an internal policy or capability check rather than the filesystem (no exception involved).</summary>
        Blocked,
        /// <summary>A failure occurred but could not be classified against a known shape.</summary>
        Unknown
    }

    /// <summary>
    /// Fixed, non-leaking sentences for each <see cref="ImportFailureClass"/>. These are the
    /// only text that may reach an external-facing failure surface; the exception object or
    /// blocked-action detail itself belongs on the log line only.
    /// </summary>
    public static class ImportFailureClassSentences
    {
        public static string Describe(ImportFailureClass failureClass) => failureClass switch
        {
            ImportFailureClass.PermissionDenied => "Failed to import file, permission denied",
            ImportFailureClass.DestinationUnavailable => "Failed to import file, destination unavailable",
            ImportFailureClass.DestinationConflict => "Failed to import file, destination already exists",
            ImportFailureClass.PathTooLong => "Failed to import file, path too long",
            ImportFailureClass.FileInUse => "Failed to import file, file in use",
            ImportFailureClass.IoError => "Failed to import file, I/O error",
            ImportFailureClass.Blocked => "Failed to import file, blocked by policy",
            _ => "Failed to import file, see server log for detail"
        };
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public string? SourcePath { get; set; }
        public string? FinalPath { get; set; }
        public string? Message { get; set; }
        public FileAction Action { get; set; }
        public FileAction RequestedAction { get; set; }
        public FileAction EffectiveAction { get; set; }
        public ImportSourceDisposition SourceDisposition { get; set; }
        public string? WarningCode { get; set; }
        public bool WasRegisteredToAudiobook { get; set; }
        public DateTime? Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Non-leaking classification of this failure, set by the factory methods below.
        /// <see cref="ImportFailureClass.None"/> for a success, or for a failure built
        /// directly through the object initializer rather than a factory.
        /// </summary>
        public ImportFailureClass FailureClass { get; set; } = ImportFailureClass.None;

        public override string ToString()
        {
            return $"Success:{Success}, Action:{Action}, Message: {Message}, Destination:{FinalPath}";
        }

        public static ImportResult ImportSuccess(FileAction action, string sourcePath, string finalPath, bool wasRegisteredToAudiobook = false)
        {
            return new ImportResult
            {
                Success = true,
                Action = action,
                RequestedAction = action,
                EffectiveAction = action,
                SourceDisposition = action == FileAction.Move
                    ? ImportSourceDisposition.Retired
                    : ImportSourceDisposition.Unchanged,
                SourcePath = sourcePath,
                FinalPath = finalPath,
                WasRegisteredToAudiobook = wasRegisteredToAudiobook
            };
        }

        public static ImportResult ImportSuccess(
            FileAction requestedAction,
            FileAction effectiveAction,
            ImportSourceDisposition sourceDisposition,
            string sourcePath,
            string finalPath,
            bool wasRegisteredToAudiobook = false,
            string? warningCode = null,
            string? message = null)
        {
            return new ImportResult
            {
                Success = true,
                Action = effectiveAction,
                RequestedAction = requestedAction,
                EffectiveAction = effectiveAction,
                SourceDisposition = sourceDisposition,
                SourcePath = sourcePath,
                FinalPath = finalPath,
                WasRegisteredToAudiobook = wasRegisteredToAudiobook,
                WarningCode = warningCode,
                Message = message
            };
        }

        public static ImportResult ImportFailure(FileAction action, string sourcePath, string finalPath)
        {
            return new ImportResult
            {
                Success = false,
                Action = action,
                RequestedAction = action,
                EffectiveAction = action,
                SourcePath = sourcePath,
                FinalPath = finalPath,
                Message = $"Unable to perform {action} on {sourcePath} to {finalPath}",
                // These call sites are always a policy/capability rejection (blocked
                // destination, unavailable ownership, etc.), never a thrown exception.
                FailureClass = ImportFailureClass.Blocked
            };
        }

        public static ImportResult Exception(Exception exception, string sourcePath = "")
        {
            return new ImportResult
            {
                Success = false,
                Message = exception.Message,
                SourcePath = sourcePath,
                FailureClass = ClassifyException(exception)
            };
        }

        private static ImportFailureClass ClassifyException(Exception exception) => exception switch
        {
            UnauthorizedAccessException => ImportFailureClass.PermissionDenied,
            DirectoryNotFoundException => ImportFailureClass.DestinationUnavailable,
            FileNotFoundException => ImportFailureClass.DestinationUnavailable,
            PathTooLongException => ImportFailureClass.PathTooLong,
            IOException ioException when ioException.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) =>
                ImportFailureClass.DestinationConflict,
            IOException ioException when ioException.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase) =>
                ImportFailureClass.FileInUse,
            IOException => ImportFailureClass.IoError,
            _ => ImportFailureClass.Unknown
        };

        public static ImportResult Skipped(string message)
        {
            return new ImportResult
            {
                Success = true,
                Message = message
            };
        }
    }
}
