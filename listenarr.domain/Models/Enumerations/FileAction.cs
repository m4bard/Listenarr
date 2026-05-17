using System.Text.Json.Serialization;

namespace Listenarr.Domain.Models.Enumerations
{
    /// <summary>
    /// Represents action that can be taken on files
    /// This acts as a DTO too for the API layer
    /// </summary>
    public enum FileAction
    {
        [JsonStringEnumMemberName("none")]
        None,
        [JsonStringEnumMemberName("move")]
        Move,
        [JsonStringEnumMemberName("copy")]
        Copy,
        [JsonStringEnumMemberName("hardlink/copy")]
        HardlinkCopy
    }
}
