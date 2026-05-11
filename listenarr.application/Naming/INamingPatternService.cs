namespace Listenarr.Application.Naming
{
    public interface INamingPatternService
    {
        string ApplyNamingPattern(
            string pattern,
            Dictionary<string, object> variables,
            bool treatAsFilename = false);

        string SanitizePathComponent(string pathComponent);
    }
}
