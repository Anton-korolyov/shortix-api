
using System.Buffers;

namespace StoryChain.Api.Models
{
    public class Video
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        public string Url { get; set; } = null!;
        public string? ThumbnailUrl { get; set; }

        public int DurationSec { get; set; }

        public bool IsDeleted { get; set; }
        public bool Processing { get; set; }

        public ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Pending;

        public double? NsfwScore { get; set; }
        public double? BloodScore { get; set; }
        public double? DisgustScore { get; set; }
        public double? WeaponScore { get; set; }

        public DateTime? ModeratedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? VideoCategoryId { get; set; }
        public VideoCategory? VideoCategory { get; set; }

        public ICollection<VideoTag> Tags { get; set; } = new List<VideoTag>();
    }
}
