using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public class ManualImportItemDto
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("fullPath")]
    [Required]
    public string? FullPath {
        get
        {
            if (field == null) return null;
            return Path.GetFullPath(field);
        }
        set;
    }

    [JsonPropertyName("matchedAudiobookId")]
    public int MatchedAudiobookId { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string? ReleaseGroup { get; set; }

    [JsonPropertyName("qualityProfileId")]
    public int? QualityProfileId { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonIgnore]
    public int? SequenceNumberHint { get; set; }

    [JsonIgnore]
    public int? DiskNumberHint { get; set; }

    [JsonIgnore]
    public int? ChapterNumberHint { get; set; }
}