namespace StoryChain.Api.Models
{
    public class Notification
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }     // кому

        public string? Type { get; set; }     // follow / like / comment
        public string? Message { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
