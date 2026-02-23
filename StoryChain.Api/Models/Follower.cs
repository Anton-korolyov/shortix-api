namespace StoryChain.Api.Models
{
    public class Follower
    {
        public Guid Id { get; set; }

        public Guid FollowerUserId { get; set; }   // кто подписался
        public User FollowerUser { get; set; }

        public Guid FollowingUserId { get; set; }  // на кого подписались
        public User FollowingUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
