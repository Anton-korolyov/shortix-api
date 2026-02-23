using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Models;


namespace StoryChain.Api.Data
{

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Video> Videos => Set<Video>();
        public DbSet<Story> Stories => Set<Story>();
        public DbSet<StoryNode> StoryNodes => Set<StoryNode>();
        public DbSet<Like> Likes => Set<Like>();
        public DbSet<Comment> Comments => Set<Comment>();
        public DbSet<CommentLike> CommentLikes => Set<CommentLike>();
        public DbSet<Report> Reports => Set<Report>();
        public DbSet<VideoView> VideoViews => Set<VideoView>();
        public DbSet<WatchTime> WatchTimes => Set<WatchTime>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Follower> Followers => Set<Follower>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<VideoCategory> VideoCategories => Set<VideoCategory>();
        public DbSet<VideoTag> VideoTags => Set<VideoTag>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // USERS
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // FOLLOWERS
            modelBuilder.Entity<Follower>()
                .HasOne(x => x.FollowerUser)
                .WithMany()
                .HasForeignKey(x => x.FollowerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Follower>()
                .HasOne(x => x.FollowingUser)
                .WithMany()
                .HasForeignKey(x => x.FollowingUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Follower>()
                .HasIndex(x => new { x.FollowerUserId, x.FollowingUserId })
                .IsUnique();

            // 🔥 VIDEO CATEGORIES
            modelBuilder.Entity<VideoCategory>()
                .HasIndex(c => c.Name);

            // 🔥 VIDEO TAGS
            modelBuilder.Entity<VideoTag>()
                .HasIndex(t => t.Tag);
        }
    }
}
