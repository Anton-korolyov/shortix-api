namespace StoryChain.Api.Models
{
    public class Story
    {
        public Guid Id { get; set; }

        public Guid RootVideoId { get; set; }

        public Guid CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
