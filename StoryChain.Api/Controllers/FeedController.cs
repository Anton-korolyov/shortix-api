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

        var now = DateTime.UtcNow;

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
        // FIND VIDEO POSITION
        //-----------------------------------------

        int startIndex = 0;

        if (videoId != null)
        {
            var target = await baseQuery
                .Where(n => n.VideoId == videoId)
                .Select(n => new { n.Video.CreatedAt })
                .FirstOrDefaultAsync();

            if (target != null)
            {
                startIndex = await baseQuery
                    .Where(n => n.Video.CreatedAt > target.CreatedAt)
                    .CountAsync();
            }
        }

        //-----------------------------------------
        // PAGE START
        //-----------------------------------------

        int pageStart = (startIndex / pageSize) * pageSize;

        //-----------------------------------------
        // LOAD VIDEOS
        //-----------------------------------------

        var videos = await baseQuery
            .OrderByDescending(n => n.Video.CreatedAt)
            .Skip(cursor != null ? 0 : pageStart)
            .Take(pageSize * 5)
            .Select(n => new
            {
                NodeId = n.Id,
                VideoId = n.VideoId,
                Url = n.Video.Url,
                ThumbnailUrl = n.Video.ThumbnailUrl,
                CreatedAt = n.Video.CreatedAt,
                Username = n.Video.User.Username,
                AvatarUrl = n.Video.User.AvatarUrl,
                Bio = n.Video.User.Bio
            })
            .ToListAsync();

        //-----------------------------------------
        // FLOW DETECTION
        //-----------------------------------------

        var nodeIds = videos.Select(v => v.NodeId).ToList();

        var childrenNodes = await _db.StoryNodes
            .Where(x => x.ParentNodeId != null && nodeIds.Contains(x.ParentNodeId.Value))
            .Select(x => x.ParentNodeId!.Value)
            .Distinct()
            .ToListAsync();

        var childrenSet = childrenNodes.ToHashSet();

        //-----------------------------------------
        // SORT
        //-----------------------------------------

        var ordered = videos
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.NodeId)
            .ToList();

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
        // INDEX
        //-----------------------------------------

        int index = startIndex - pageStart;

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
                hasChildren = childrenSet.Contains(video.NodeId)
            })
            .ToList<object>();

        //-----------------------------------------
        // NEXT CURSOR
        //-----------------------------------------

        var nextCursor = pageVideos.Count > 0
            ? ((dynamic)pageVideos.Last()).id
            : (Guid?)null;

        //-----------------------------------------
        // RESULT
        //-----------------------------------------

        return Ok(new
        {
            items = pageVideos,
            nextCursor,
            index
        });
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