namespace StoryChain.Api.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public string Username { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public string? Bio { get; set; }
        // Refresh token
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiry { get; set; }

   
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
