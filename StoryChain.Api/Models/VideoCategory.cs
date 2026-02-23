namespace StoryChain.Api.Models
{
    public class VideoCategory
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;

        public ICollection<Video> Videos { get; set; } = new List<Video>();
    }
}
