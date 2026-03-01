using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Models
{
    public class UserSession
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(256)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string TokenHash { get; set; } = string.Empty;

        public bool IsAdmin { get; set; }

        public bool RememberMe { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow;

        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    }
}
