namespace StoryChain.Api.Models
{
    public class VideoBoost
    {
        public Guid Id { get; set; }

        public Guid VideoId { get; set; }

        public Guid UserId { get; set; }

        public int Budget { get; set; }

        public int Days { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public bool Active { get; set; }
    }
}
