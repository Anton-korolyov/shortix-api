namespace StoryChain.Api.Models
{
    public class Ad
    {
        public Guid Id { get; set; }
        public string VideoUrl { get; set; } = null!;
        public string TargetUrl { get; set; } = null!;
        public bool IsActive { get; set; }
    }
}
