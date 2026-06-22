using System;

namespace TunnelSecurity.Data.Auth.Models
{
    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool Revoked { get; set; }
        public Guid? ReplacedByToken { get; set; }

        // Navigation
        public User? User { get; set; }
    }
}   