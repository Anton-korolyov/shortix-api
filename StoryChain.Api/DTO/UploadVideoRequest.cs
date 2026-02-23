namespace StoryChain.Api.DTO
{
    public class UploadVideoRequest
    {
        public IFormFile File { get; set; } = null!;

        public Guid? ParentNodeId { get; set; }

        public Guid VideoCategoryId { get; set; }

        public List<string> Tags { get; set; } = new();
    }
}
