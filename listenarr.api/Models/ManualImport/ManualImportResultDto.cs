public class ManualImportResultDto
{
    public bool Success { get; set; }
    public string? SourcePath { get; set; }
    public string? DestinationPath { get; set; }
    public Audiobook? Audiobook { get; set; }
    public string? Error { get; set; }

    public static ManualImportResultDto FailureResult(string error, string? sourcePath)
    {
        return new ManualImportResultDto
        {
            Success = false,
            Error = error,
            SourcePath = sourcePath
        };
    }
}