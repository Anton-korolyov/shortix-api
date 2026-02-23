namespace StoryChain.Api.Models
{
    public class Report
    {
        public Guid Id { get; set; }

        public Guid ReporterUserId { get; set; }

        public Guid? VideoId { get; set; }

        public Guid? CommentId { get; set; }

        public string Reason { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsResolved { get; set; }
    }
}
