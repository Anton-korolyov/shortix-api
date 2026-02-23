namespace StoryChain.Api.DTO
{
    public class CommentTreeDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = null!;
        public DateTime CreatedAt { get; set; }

        public List<CommentTreeDto> Replies { get; set; } = new();
    }
}
