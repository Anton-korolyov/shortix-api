namespace StoryChain.Api.Models
{
    public class VideoView
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid VideoId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
