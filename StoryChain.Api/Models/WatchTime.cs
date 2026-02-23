namespace StoryChain.Api.Models
{
    public class WatchTime
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid VideoId { get; set; }

        public double Seconds { get; set; }
    }
}
