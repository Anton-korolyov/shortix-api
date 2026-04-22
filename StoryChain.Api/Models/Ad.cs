namespace StoryChain.Api.Models
{
    public class Ad
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public string Type { get; set; }
        public string MediaUrl { get; set; }
        public string Link { get; set; }

        public int Budget { get; set; }
        public int Days { get; set; }

        public int Views { get; set; }
        public int Clicks { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool Active { get; set; }
    }
}
