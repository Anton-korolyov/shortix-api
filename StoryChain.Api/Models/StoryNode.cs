namespace StoryChain.Api.Models
{
    public class StoryNode
    {
        public Guid Id { get; set; }

        public Guid StoryId { get; set; }

        public Guid VideoId { get; set; }

        public Guid? ParentNodeId { get; set; }
        public Video Video { get; set; }

        public int Depth { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
