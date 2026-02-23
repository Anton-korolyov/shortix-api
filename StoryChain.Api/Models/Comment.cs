namespace StoryChain.Api.Models
{
    public class Comment
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public User User { get; set; }
        public Guid StoryNodeId { get; set; }

        public Guid? ParentCommentId { get; set; }

        public string Text { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
