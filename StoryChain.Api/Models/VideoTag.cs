   namespace StoryChain.Api.Models
    {
        public class VideoTag
        {
            public Guid Id { get; set; }

            public Guid VideoId { get; set; }
            public Video Video { get; set; } = null!;

            public string Tag { get; set; } = null!;
        }
    }

