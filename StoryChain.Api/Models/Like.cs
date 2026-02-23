namespace StoryChain.Api.Models
{
    public class Like
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid StoryNodeId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
