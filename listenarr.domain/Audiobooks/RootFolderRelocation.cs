using System.ComponentModel.DataAnnotations;
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Audiobooks;

public enum RootFolderRelocationMode
{
    Relocate,
    MetadataOnly
}

public enum RootFolderRelocationStatus
{
    Pending,
    Running,
    NeedsAttention,
    Completed,
    Failed
}

public sealed class RootFolderRelocation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RootFolderId { get; set; }
    public int? ActiveRootFolderId { get; set; }
    public RootFolder RootFolder { get; set; } = null!;
    [Required, MaxLength(1000)]
    public string SourcePath { get; set; } = string.Empty;
    [Required, MaxLength(1000)]
    public string TargetPath { get; set; } = string.Empty;
    public RootFolderRelocationMode Mode { get; set; } = RootFolderRelocationMode.Relocate;
    public RootFolderRelocationStatus Status { get; set; } = RootFolderRelocationStatus.Pending;
    public bool DeleteEmptySource { get; set; } = true;
    [Required, MaxLength(200)]
    public string DesiredName { get; set; } = string.Empty;
    public bool DesiredIsDefault { get; set; }
    public FileSystemCaseSensitivityMode TargetCaseSensitivityMode { get; set; } = FileSystemCaseSensitivityMode.Auto;
    public int TotalJobs { get; set; }
    public int CompletedJobs { get; set; }
    [MaxLength(4000)]
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<MoveJob> MoveJobs { get; set; } = new List<MoveJob>();
}
