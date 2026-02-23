namespace StoryChain.Api.DTO
{
    public class DeleteVideoResult
    {
        public string? RedirectTo { get; set; }   // "feed" | "video"
        public Guid? VideoId { get; set; }
    }
}
