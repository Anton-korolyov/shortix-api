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

        var cacheKey = $"feed:{currentUserId}:{cursor}:{pageSize}:{categoryId}:{following}";
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
        // FOLLOWING
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
        // USER INTERESTED CATEGORIES
        //-----------------------------------------

        var interestedCategories = currentUserId == null
            ? new HashSet<Guid>()
            : (await _db.Likes
                .AsNoTracking()
                .Where(l => l.UserId == currentUserId)

                .Join(_db.StoryNodes,
                    l => l.StoryNodeId,
                    n => n.Id,
                    (l, n) => n.VideoId)

                .Join(_db.Videos,
                    videoId => videoId,
                    v => v.Id,
                    (videoId, v) => v.VideoCategoryId)

                .Where(c => c != null)
                .Select(c => c!.Value)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

        //-----------------------------------------
        // AGGREGATIONS
        //-----------------------------------------

        var likeCountsQuery = _db.Likes
            .AsNoTracking()
            .Join(_db.StoryNodes,
                l => l.StoryNodeId,
                n => n.Id,
                (l, n) => new { n.VideoId })
            .GroupBy(x => x.VideoId)
            .Select(g => new
            {
                VideoId = g.Key,
                Count = g.Count()
            });

        var viewCountsQuery = _db.VideoViews
            .AsNoTracking()
            .GroupBy(x => x.VideoId)
            .Select(g => new
            {
                VideoId = g.Key,
                Count = g.Count()
            });

        var watchTimesQuery = _db.WatchTimes
            .AsNoTracking()
            .GroupBy(x => x.VideoId)
            .Select(g => new
            {
                VideoId = g.Key,
                Seconds = g.Sum(x => x.Seconds)
            });

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

        //-----------------------------------------
        // CANDIDATES
        //-----------------------------------------

        var candidates = await (
            from n in baseQuery

            join lc in likeCountsQuery
                on n.VideoId equals lc.VideoId into likesJoin
            from lc in likesJoin.DefaultIfEmpty()

            join vc in viewCountsQuery
                on n.VideoId equals vc.VideoId into viewsJoin
            from vc in viewsJoin.DefaultIfEmpty()

            join wc in watchTimesQuery
                on n.VideoId equals wc.VideoId into watchJoin
            from wc in watchJoin.DefaultIfEmpty()

            select new FeedVideoCandidate
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

                VideoCategoryId = n.Video.VideoCategoryId,

                LikesCount = lc != null ? lc.Count : 0,
                ViewsCount = vc != null ? vc.Count : 0,
                WatchSeconds = wc != null ? wc.Seconds : 0
            })
            .OrderByDescending(x => x.CreatedAt)
            .Take(300)
            .ToListAsync();

        //-----------------------------------------
        // SCORING
        //-----------------------------------------

        foreach (var item in candidates)
        {
            item.IsBoosted = boostedVideoIds.Contains(item.VideoId);
            item.IsFollowingAuthor = followingIds.Contains(item.UserId);

            item.CategoryMatch =
                item.VideoCategoryId.HasValue &&
                interestedCategories.Contains(item.VideoCategoryId.Value);

            var ageHours = Math.Max(1, (now - item.CreatedAt).TotalHours);

            double freshnessScore =
                ageHours <= 6 ? 40 :
                ageHours <= 24 ? 25 :
                ageHours <= 72 ? 12 :
                4;

            var likesScore = item.LikesCount * 3.5;
            var viewsScore = item.ViewsCount * 0.25;
            var watchScore = item.WatchSeconds * 0.02;

            var boostScore = item.IsBoosted ? 35 : 0;
            var followingScore = item.IsFollowingAuthor ? 20 : 0;
            var categoryScore = item.CategoryMatch ? 18 : 0;

            item.Score =
                freshnessScore +
                likesScore +
                viewsScore +
                watchScore +
                boostScore +
                followingScore +
                categoryScore;
        }

        //-----------------------------------------
        // SORT
        //-----------------------------------------

        var ordered = candidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreatedAt)
            .ToList();

        //-----------------------------------------
        // CURSOR PAGINATION
        //-----------------------------------------

        if (cursor != null)
        {
            ordered = ordered
                .Where(x => x.NodeId.CompareTo(cursor.Value) < 0)
                .ToList();
        }

        var pageVideos = ordered
            .Take(pageSize)
            .ToList();

        var nextCursor = pageVideos.LastOrDefault()?.NodeId;

        //-----------------------------------------
        // ADS
        //-----------------------------------------

        var ads = await _db.Ads
            .Where(a =>
                a.Active &&
                a.StartDate <= now &&
                a.EndDate >= now &&
                a.Views < a.Budget)
            .OrderBy(a => a.Views)
            .Take(10)
            .ToListAsync();

        //-----------------------------------------
        // MERGE ADS
        //-----------------------------------------

        List<object> feed = new();
        int adIndex = 0;
        int adEvery = 7;

        foreach (var video in pageVideos)
        {
            feed.Add(new
            {
                type = "video",
                id = video.NodeId,
                videoId = video.VideoId,
                url = video.Url,
                thumbnailUrl = video.ThumbnailUrl,
                username = video.Username,
                avatarUrl = video.AvatarUrl,
                bio = video.Bio,
                score = video.Score
            });

            if (feed.Count % adEvery == 0 && ads.Count > 0)
            {
                var ad = ads[adIndex % ads.Count];

                feed.Add(new
                {
                    type = "ad",
                    id = ad.Id,
                    mediaUrl = ad.MediaUrl,
                    link = ad.Link
                });

                ad.Views++;
                adIndex++;
            }
        }

        await _db.SaveChangesAsync();

        //-----------------------------------------
        // RESULT
        //-----------------------------------------

        var result = new
        {
            items = feed,
            nextCursor = nextCursor
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
        public bool CategoryMatch { get; set; }

        public double Score { get; set; }
    }
}