using System.Text.Json.Serialization;

namespace Listenarr.Domain.Audiobooks.Enumerations
{
    /// <summary>
    /// How a quality profile treats a bundle or omnibus release relative to a release of a
    /// single book. This is a preference and not a filter: a release on the wrong side of it
    /// is scored down, never rejected, so a monitored book whose only available release is a
    /// bundle still gets filled.
    /// This acts as a DTO too for the API layer
    /// </summary>
    public enum ReleaseShapePreference
    {
        [JsonStringEnumMemberName("none")]
        NoPreference = 0,
        [JsonStringEnumMemberName("individual")]
        PreferIndividual = 1,
        [JsonStringEnumMemberName("bundle")]
        PreferBundle = 2
    }
}
