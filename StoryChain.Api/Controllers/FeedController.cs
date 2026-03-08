using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;

namespace StoryChain.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedController : ControllerBase
{
    private readonly AppDbContext _db;

    public FeedController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        int page = 1,
        int pageSize = 10,
        Guid? categoryId = null,
        bool following = false
    )
    {
        if (page < 1) page = 1;
        if (pageSize > 50) pageSize = 50;

        Guid? userId = null;

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (idClaim != null)
            userId = Guid.Parse(idClaim.Value);

        // BASE QUERY

        var query = _db.StoryNodes
            .Where(n =>
                n.ParentNodeId == null &&
                !n.Video.IsDeleted &&
                !n.Video.Processing
            )
            .Select(n => new
            {
                nodeId = n.Id,
                videoId = n.VideoId,
                url = n.Video.Url,
                thumbnailUrl = n.Video.ThumbnailUrl,
                createdAt = n.Video.CreatedAt,
                userId = n.Video.UserId,
                username = n.Video.User.Username,
                avatarUrl = n.Video.User.AvatarUrl,
                bio = n.Video.User.Bio,
                category = n.Video.VideoCategory != null
                    ? n.Video.VideoCategory.Name
                    : null,
                tags = n.Video.Tags.Select(t => t.Tag).ToList()
            });

        // FOLLOWING FILTER

        if (following && userId != null)
        {
            query = query.Where(n =>
                _db.Followers.Any(f =>
                    f.FollowerUserId == userId &&
                    f.FollowingUserId == n.userId
                )
                && n.userId != userId
            );
        }

        // CATEGORY FILTER

        if (categoryId != null)
        {
            query = query.Where(n =>
                _db.Videos.Any(v =>
                    v.Id == n.videoId &&
                    v.VideoCategoryId == categoryId
                )
            );
        }

        // ORDER

        query = query.OrderByDescending(v => v.createdAt);

        var total = await query.CountAsync();

        // PAGE

        var videos = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var nodeIds = videos.Select(v => v.nodeId).ToList();

        // LIKES COUNT

        var likes = await _db.Likes
            .Where(l => nodeIds.Contains(l.StoryNodeId))
            .GroupBy(l => l.StoryNodeId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // COMMENTS COUNT

        var comments = await _db.Comments
            .Where(c => nodeIds.Contains(c.StoryNodeId))
            .GroupBy(c => c.StoryNodeId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // CHILDREN CHECK

        var children = await _db.StoryNodes
            .Where(c => c.ParentNodeId != null && nodeIds.Contains(c.ParentNodeId.Value))
            .GroupBy(c => c.ParentNodeId)
            .Select(g => g.Key)
            .ToListAsync();

        // LIKED BY USER

        HashSet<Guid> liked = new();

        if (userId != null)
        {
            liked = _db.Likes
                .Where(l =>
                    nodeIds.Contains(l.StoryNodeId) &&
                    l.UserId == userId
                )
                .Select(l => l.StoryNodeId)
                .ToHashSet();
        }

        var items = videos.Select(v => new
        {
            type = "video",
            id = v.nodeId,
            videoId = v.videoId,
            url = v.url,
            thumbnailUrl = v.thumbnailUrl,
            category = v.category,
            tags = v.tags,
            createdAt = v.createdAt,

            likes = likes.ContainsKey(v.nodeId) ? likes[v.nodeId] : 0,
            comments = comments.ContainsKey(v.nodeId) ? comments[v.nodeId] : 0,
            hasChildren = children.Contains(v.nodeId),
            isLiked = liked.Contains(v.nodeId),

            username = v.username,
            avatarUrl = v.avatarUrl,
            bio = v.bio
        }).ToList();

        return Ok(new
        {
            page,
            pageSize,
            total,
            hasMore = page * pageSize < total,
            items
        });
    }
}