namespace StoryChain.Api.DTO
{
    public class AddCommentDto
    {
        public Guid StoryNodeId { get; set; }
        public string Text { get; set; } = "";
        public Guid? ParentCommentId { get; set; }
    }
}
