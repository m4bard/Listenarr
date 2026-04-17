using System.Text.Json.Serialization;
using static Listenarr.Api.Services.FileMover;

public class ManualImportRequestDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "interactive";

    [JsonPropertyName("action")]
    public FileAction Action { get; set; }

    [JsonPropertyName("includeCompanionFiles")]
    public bool IncludeCompanionFiles { get; set; }

    [JsonPropertyName("cleanupEmptySourceFolders")]
    public bool CleanupEmptySourceFolders { get; set; }

    [JsonPropertyName("items")]
    public List<ManualImportItemDto>? Items { get; set; }
}