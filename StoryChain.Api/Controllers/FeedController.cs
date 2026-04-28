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

    // Сколько NodeId держим в Redis на пользователя
    private const int MaxFeedCache = 500;
    private const int PageSize = 10;

    public FeedController(AppDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis.GetDatabase();
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid? cursor = null,
        Guid? videoId = null,
        int pageSize = PageSize,
        Guid? categoryId = null,
        bool following = false)
    {
        if (pageSize < 1) pageSize = PageSize;
        if (pageSize > 50) pageSize = 50;

        Guid? currentUserId = null;
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (idClaim != null && Guid.TryParse(idClaim.Value, out var parsed))
            currentUserId = parsed;

        // ключ кэша порядка ленты для этого пользователя
        var orderKey = $"feed:order:{currentUserId ?? Guid.Empty}:{following}:{categoryId}";

        // ==============================================
        // ВОЗВРАТ ИЗ FLOW — ищем позицию в кэше
        // ==============================================
        if (videoId != null)
        {
            var cachedRaw = await _redis.StringGetAsync(orderKey);

            if (!cachedRaw.IsNullOrEmpty)
            {
                var allIds = JsonSerializer.Deserialize<List<Guid>>(cachedRaw!)!;
                var pos = allIds.IndexOf(videoId.Value);

                if (pos >= 0)
                {
                    // берём окно ±(pageSize/2) вокруг нужного видео
                    int start = Math.Max(0, pos - pageSize / 2);
                    var windowIds = allIds.Skip(start).Take(pageSize).ToList();

                    var dbVideos = await LoadVideosByIds(windowIds);

                    // сортируем в том же порядке что в кэше
                    var sortedPage = windowIds
                        .Select(id => dbVideos.FirstOrDefault(v => v.NodeId == id))
                        .Where(v => v != null)
                        .ToList();

                    int indexInPage = pos - start;

                    // nextCursor — последний элемент окна
                    Guid? nextCursor = windowIds.Count > 0 ? windowIds.Last() : (Guid?)null;

                    return Ok(new
                    {
                        items = sortedPage.Select(MapToDto),
                        nextCursor,
                        index = indexInPage
                    });
                }
            }

            // кэш протух — перестраиваем ленту с нуля (см. ниже)
        }

        // ==============================================
        // ОБЫЧНЫЙ FEED — строим ленту, кэшируем порядок
        // ==============================================

        var now = DateTime.UtcNow;

        var boostedIds = (await _db.VideoBoosts
            .AsNoTracking()
            .Where(b => b.Active && b.EndDate >= now)
            .Select(b => b.VideoId)
            .ToListAsync()).ToHashSet();

        var followingIds = currentUserId == null
            ? new HashSet<Guid>()
            : (await _db.Followers
                .AsNoTracking()
                .Where(f => f.FollowerUserId == currentUserId)
                .Select(f => f.FollowingUserId)
                .ToListAsync()).ToHashSet();

        var baseQuery = _db.StoryNodes
            .AsNoTracking()
            .Where(n =>
                n.ParentNodeId == null &&
                !n.Video.IsDeleted &&
                !n.Video.Processing);

        if (categoryId != null)
            baseQuery = baseQuery.Where(n => n.Video.VideoCategoryId == categoryId);

        if (following)
            baseQuery = baseQuery.Where(n => followingIds.Contains(n.Video.UserId));

        // при возврате из Flow и протухшем кэше — грузим 200 вокруг даты видео
        int dbSkip = 0;
        int dbTake = MaxFeedCache;

        if (videoId != null)
        {
            var targetDate = await baseQuery
                .Where(n => n.VideoId == videoId)
                .Select(n => n.Video.CreatedAt)
                .FirstOrDefaultAsync();

            if (targetDate != default)
            {
                // грузим 100 до и 100 после по дате — потом scoring расставит правильно
                var beforeCount = await baseQuery
                    .Where(n => n.Video.CreatedAt > targetDate)
                    .CountAsync();

                dbSkip = Math.Max(0, beforeCount - 100);
                dbTake = 200;
            }
        }
        else if (cursor != null)
        {
            // курсорная пагинация — найти позицию курсора в кэше
            var cachedRaw = await _redis.StringGetAsync(orderKey);
            if (!cachedRaw.IsNullOrEmpty)
            {
                var allIds = JsonSerializer.Deserialize<List<Guid>>(cachedRaw!)!;
                var cursorPos = allIds.IndexOf(cursor.Value);

                if (cursorPos >= 0)
                {
                    var nextIds = allIds.Skip(cursorPos + 1).Take(pageSize).ToList();
                    if (nextIds.Count > 0)
                    {
                        var dbPage = await LoadVideosByIds(nextIds);
                        var sorted = nextIds
                            .Select(id => dbPage.FirstOrDefault(v => v.NodeId == id))
                            .Where(v => v != null)
                            .ToList();

                        return Ok(new
                        {
                            items = sorted.Select(MapToDto),
                            nextCursor = nextIds.Last(),
                            index = (object?)null
                        });
                    }
                }
            }
        }

        // грузим кандидатов из БД
        var rawVideos = await baseQuery
            .OrderByDescending(n => n.Video.CreatedAt)
            .Skip(dbSkip)
            .Take(dbTake)
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

        var nodeIds = rawVideos.Select(v => v.NodeId).ToList();
        var videoIds = rawVideos.Select(v => v.VideoId).ToList();

        var childrenSet = (await _db.StoryNodes
            .Where(x => x.ParentNodeId != null && nodeIds.Contains(x.ParentNodeId.Value))
            .Select(x => x.ParentNodeId!.Value)
            .Distinct()
            .ToListAsync()).ToHashSet();

        var likes = await _db.Likes
            .AsNoTracking()
            .Join(_db.StoryNodes, l => l.StoryNodeId, n => n.Id, (l, n) => new { n.VideoId })
            .Where(x => videoIds.Contains(x.VideoId))
            .GroupBy(x => x.VideoId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var views = await _db.VideoViews
            .AsNoTracking()
            .Where(v => videoIds.Contains(v.VideoId))
            .GroupBy(v => v.VideoId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var watch = await _db.WatchTimes
            .AsNoTracking()
            .Where(w => videoIds.Contains(w.VideoId))
            .GroupBy(w => w.VideoId)
            .Select(g => new { g.Key, Seconds = g.Sum(x => x.Seconds) })
            .ToDictionaryAsync(x => x.Key, x => x.Seconds);

        // скоринг
        var candidates = rawVideos.Select(v =>
        {
            var ageHours = Math.Max(1, (now - v.CreatedAt).TotalHours);
            double freshness = ageHours <= 6 ? 40 : ageHours <= 24 ? 25 : ageHours <= 72 ? 12 : 4;

            return new FeedVideoCandidate
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
                IsBoosted = boostedIds.Contains(v.VideoId),
                IsFollowingAuthor = followingIds.Contains(v.UserId),
                HasChildren = childrenSet.Contains(v.NodeId),
                Score = freshness
                    + likes.GetValueOrDefault(v.VideoId) * 3.5
                    + views.GetValueOrDefault(v.VideoId) * 0.25
                    + watch.GetValueOrDefault(v.VideoId) * 0.02
                    + (boostedIds.Contains(v.VideoId) ? 35 : 0)
                    + (followingIds.Contains(v.UserId) ? 20 : 0)
            };
        })
        .OrderByDescending(x => x.Score)
        .ThenByDescending(x => x.CreatedAt)
        .ThenByDescending(x => x.NodeId)
        .ToList();

        // кэшируем порядок NodeId в Redis на 10 минут
        var orderedIds = candidates.Select(x => x.NodeId).ToList();
        await _redis.StringSetAsync(
            orderKey,
            JsonSerializer.Serialize(orderedIds),
            TimeSpan.FromMinutes(10));

        // находим позицию videoId если возврат из Flow
        int returnIndex = 0;
        int pageStart = 0;

        if (videoId != null)
        {
            var pos = candidates.FindIndex(v => v.VideoId == videoId.Value);
            if (pos >= 0)
            {
                pageStart = Math.Max(0, pos - pageSize / 2);
                returnIndex = pos - pageStart;
            }
        }

        var page = candidates
            .Skip(pageStart)
            .Take(pageSize)
            .ToList();

        var nextCursorId = page.Count > 0 ? page.Last().NodeId : (Guid?)null;

        return Ok(new
        {
            items = page.Select(MapToDto),
            nextCursor = nextCursorId,
            index = videoId != null ? returnIndex : (int?)null
        });
    }

    // ==============================================
    // HELPERS
    // ==============================================

    private async Task<List<FeedVideoCandidate>> LoadVideosByIds(List<Guid> nodeIds)
    {
        var now = DateTime.UtcNow;

        var videos = await _db.StoryNodes
            .AsNoTracking()
            .Where(n => nodeIds.Contains(n.Id))
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

        var videoIds = videos.Select(v => v.VideoId).ToList();

        var childrenSet = (await _db.StoryNodes
            .Where(x => x.ParentNodeId != null && nodeIds.Contains(x.ParentNodeId.Value))
            .Select(x => x.ParentNodeId!.Value)
            .Distinct()
            .ToListAsync()).ToHashSet();

        var likes = await _db.Likes
            .AsNoTracking()
            .Join(_db.StoryNodes, l => l.StoryNodeId, n => n.Id, (l, n) => new { n.VideoId })
            .Where(x => videoIds.Contains(x.VideoId))
            .GroupBy(x => x.VideoId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return videos.Select(v => new FeedVideoCandidate
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
            HasChildren = childrenSet.Contains(v.NodeId)
        }).ToList();
    }

    private static object MapToDto(FeedVideoCandidate? v) => new
    {
        type = "video",
        id = v!.NodeId,
        videoId = v.VideoId,
        url = v.Url,
        thumbnailUrl = v.ThumbnailUrl,
        username = v.Username,
        avatarUrl = v.AvatarUrl,
        bio = v.Bio,
        hasChildren = v.HasChildren
    };

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