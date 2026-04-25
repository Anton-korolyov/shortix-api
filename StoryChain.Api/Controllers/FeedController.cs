using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StoryChain.Api.Data;

namespace StoryChain.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDatabase _redis;

    public FeedController(AppDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis.GetDatabase();
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid? cursor = null,
        Guid? videoId = null,
        int pageSize = 10,
        Guid? categoryId = null,
        bool following = false)
    {
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 50) pageSize = 50;

        Guid? currentUserId = null;

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (idClaim != null && Guid.TryParse(idClaim.Value, out var parsed))
            currentUserId = parsed;

        var cacheKey = $"feed:{currentUserId}:{cursor}:{videoId}:{pageSize}:{categoryId}:{following}";
        var cached = await _redis.StringGetAsync(cacheKey);

        if (!cached.IsNullOrEmpty)
            return Content(cached!, "application/json");

        var now = DateTime.UtcNow;

        //-----------------------------------------
        // BOOSTED VIDEOS
        //-----------------------------------------

        var boostedVideoIds = (await _db.VideoBoosts
            .AsNoTracking()
            .Where(b => b.Active && b.EndDate >= now)
            .Select(b => b.VideoId)
            .ToListAsync())
            .ToHashSet();

        //-----------------------------------------
        // FOLLOWING IDS
        //-----------------------------------------

        var followingIds = currentUserId == null
            ? new HashSet<Guid>()
            : (await _db.Followers
                .AsNoTracking()
                .Where(f => f.FollowerUserId == currentUserId)
                .Select(f => f.FollowingUserId)
                .ToListAsync())
                .ToHashSet();

        //-----------------------------------------
        // BASE QUERY
        //-----------------------------------------

        var baseQuery = _db.StoryNodes
            .AsNoTracking()
            .Where(n =>
                n.ParentNodeId == null &&
                !n.Video.IsDeleted &&
                !n.Video.Processing);

        if (categoryId != null)
            baseQuery = baseQuery.Where(n => n.Video.VideoCategoryId == categoryId);

        if (following)
        {
            if (currentUserId == null)
            {
                return Ok(new
                {
                    items = new List<object>(),
                    nextCursor = (Guid?)null
                });
            }

            baseQuery = baseQuery.Where(n => followingIds.Contains(n.Video.UserId));
        }

        //-----------------------------------------
        // LOAD VIDEOS
        //-----------------------------------------
        //-----------------------------------------
        // LOAD VIDEOS
        //-----------------------------------------

        int startIndex1 = 0;

        if (videoId != null)
        {
            var target = await baseQuery
                .Where(n => n.VideoId == videoId)
                .Select(n => new { n.Video.CreatedAt })
                .FirstOrDefaultAsync();

            if (target != null)
            {
                startIndex1 = await baseQuery
                    .Where(n => n.Video.CreatedAt > target.CreatedAt)
                    .CountAsync();
            }
        }

        var pageStart1 = (startIndex1 / pageSize) * pageSize;

        var skip = Math.Max(0, pageStart1 - pageSize);

        var videos = await baseQuery
            .OrderByDescending(n => n.Video.CreatedAt)
            .Skip(skip)
            .Take(80)
            .Select(n => new
            {
                NodeId = n.Id,
                VideoId = n.VideoId,
                UserId = n.Video.UserId,
                Url = n.Video.Url,
                ThumbnailUrl = n.Video.ThumbnailUrl,
                CreatedAt = n.Video.CreatedAt,
                Username = n.Video.User.Username,
                AvatarUrl = n.Video.User.AvatarUrl,
                Bio = n.Video.User.Bio,
                CategoryId = n.Video.VideoCategoryId
            })
            .ToListAsync();

        var nodeIds = videos.Select(v => v.NodeId).ToList();
        var videoIds = videos.Select(v => v.VideoId).ToList();

        //-----------------------------------------
        // FLOW
        //-----------------------------------------

        var childrenNodes = await _db.StoryNodes
            .Where(x => x.ParentNodeId != null && nodeIds.Contains(x.ParentNodeId.Value))
            .Select(x => x.ParentNodeId!.Value)
            .Distinct()
            .ToListAsync();

        var childrenSet = childrenNodes.ToHashSet();

        //-----------------------------------------
        // LIKE COUNTS
        //-----------------------------------------

        var likes = await _db.Likes
            .AsNoTracking()
            .Join(_db.StoryNodes,
                l => l.StoryNodeId,
                n => n.Id,
                (l, n) => new { n.VideoId })
            .Where(x => videoIds.Contains(x.VideoId))
            .GroupBy(x => x.VideoId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        //-----------------------------------------
        // VIEW COUNTS
        //-----------------------------------------

        var views = await _db.VideoViews
            .AsNoTracking()
            .Where(v => videoIds.Contains(v.VideoId))
            .GroupBy(v => v.VideoId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        //-----------------------------------------
        // WATCH TIME
        //-----------------------------------------

        var watch = await _db.WatchTimes
            .AsNoTracking()
            .Where(w => videoIds.Contains(w.VideoId))
            .GroupBy(w => w.VideoId)
            .Select(g => new { g.Key, Seconds = g.Sum(x => x.Seconds) })
            .ToDictionaryAsync(x => x.Key, x => x.Seconds);

        //-----------------------------------------
        // BUILD CANDIDATES
        //-----------------------------------------

        var candidates = new List<FeedVideoCandidate>();

        foreach (var v in videos)
        {
            candidates.Add(new FeedVideoCandidate
            {
                NodeId = v.NodeId,
                VideoId = v.VideoId,
                UserId = v.UserId,
                Url = v.Url ?? "",
                ThumbnailUrl = v.ThumbnailUrl,
                CreatedAt = v.CreatedAt,
                Username = v.Username ?? "",
                AvatarUrl = v.AvatarUrl,
                Bio = v.Bio,
                VideoCategoryId = v.CategoryId,
                LikesCount = likes.GetValueOrDefault(v.VideoId),
                ViewsCount = views.GetValueOrDefault(v.VideoId),
                WatchSeconds = watch.GetValueOrDefault(v.VideoId),
                IsBoosted = boostedVideoIds.Contains(v.VideoId),
                IsFollowingAuthor = followingIds.Contains(v.UserId),
                HasChildren = childrenSet.Contains(v.NodeId)
            });
        }

        //-----------------------------------------
        // SCORING
        //-----------------------------------------

        foreach (var item in candidates)
        {
            var ageHours = Math.Max(1, (now - item.CreatedAt).TotalHours);

            double freshnessScore =
                ageHours <= 6 ? 40 :
                ageHours <= 24 ? 25 :
                ageHours <= 72 ? 12 :
                4;

            item.Score =
                freshnessScore +
                item.LikesCount * 3.5 +
                item.ViewsCount * 0.25 +
                item.WatchSeconds * 0.02 +
                (item.IsBoosted ? 35 : 0) +
                (item.IsFollowingAuthor ? 20 : 0);
        }

        //-----------------------------------------
        // SORT
        //-----------------------------------------

        var ordered = candidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.NodeId)
            .ToList();

        if (videoId != null)
        {
            var target = ordered.FirstOrDefault(v => v.VideoId == videoId.Value);

            if (target != null)
            {
                ordered.Remove(target);
                ordered.Insert(0, target);
            }
        }


        //-----------------------------------------
        // CURSOR PAGINATION
        //-----------------------------------------

        if (cursor != null)
        {
            var cursorIndex = ordered.FindIndex(x => x.NodeId == cursor.Value);
            if (cursorIndex >= 0)
                ordered = ordered.Skip(cursorIndex + 1).ToList();
        }

        //-----------------------------------------
        // FIND INDEX
        //-----------------------------------------

        int index = 0;

        if (videoId != null)
        {
            var pos = ordered.FindIndex(v => v.VideoId == videoId.Value);

            if (pos >= 0)
                index = pos;
        }
        //-----------------------------------------
        // PAGE
        //-----------------------------------------
        var pageVideos = ordered
      .Take(pageSize)
           .Select(video => new
         {
             type = "video",
             id = video.NodeId,
             videoId = video.VideoId,
             url = video.Url,
             thumbnailUrl = video.ThumbnailUrl,
             username = video.Username,
             avatarUrl = video.AvatarUrl,
             bio = video.Bio,
             hasChildren = video.HasChildren
         })
         .ToList<object>();

        var nextCursor = pageVideos.Count > 0
            ? ((dynamic)pageVideos.Last()).id
            : (Guid?)null;

        var result = new
        {
            items = pageVideos,
            nextCursor,
            index = index
        };

        await _redis.StringSetAsync(
            cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(30));

        return Ok(result);
    }

    private class FeedVideoCandidate
    {
        public Guid NodeId { get; set; }
        public Guid VideoId { get; set; }
        public Guid UserId { get; set; }

        public string Url { get; set; } = "";
        public string? ThumbnailUrl { get; set; }

        public DateTime CreatedAt { get; set; }

        public string Username { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public string? Bio { get; set; }

        public Guid? VideoCategoryId { get; set; }

        public int LikesCount { get; set; }
        public int ViewsCount { get; set; }

        public double WatchSeconds { get; set; }

        public bool IsBoosted { get; set; }
        public bool IsFollowingAuthor { get; set; }

        public bool HasChildren { get; set; }

        public double Score { get; set; }
    }
}