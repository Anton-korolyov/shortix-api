
namespace StoryChain.Api.Models
{
    public class Video
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public User User { get; set; } = null!;
        public string Url { get; set; } = null!;
        public string? ThumbnailUrl { get; set; } = null!;

        public int DurationSec { get; set; }

        public bool IsDeleted { get; set; }   // 👈 НОВОЕ
        public bool Processing { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? VideoCategoryId { get; set; }
        public VideoCategory? VideoCategory { get; set; }
        public ICollection<VideoTag> Tags { get; set; } = new List<VideoTag>();

    }
}
