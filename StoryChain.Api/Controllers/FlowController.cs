using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/flow")]
    public class FlowController : ControllerBase
    {
        private readonly AppDbContext _db;

        public FlowController(AppDbContext db)
        {
            _db = db;
        }

        // ===========================
        // GET FLOW
        // ===========================
        [HttpGet("{nodeId}")]
        public async Task<IActionResult> GetFlow(Guid nodeId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

            Guid? userId =
                Guid.TryParse(userIdStr, out var u)
                    ? u
                    : null;

            // ⚡ ОДИН SQL запрос
            var children = await _db.StoryNodes
                .AsNoTracking()
                .Where(n =>
                    n.ParentNodeId == nodeId &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .Select(n => new
                {
                    id = n.Id,

                    url = n.Video.Url,
                    thumbnail = n.Video.ThumbnailUrl,

                    username = n.Video.User.Username,
                    avatarUrl = n.Video.User.AvatarUrl,
                    bio = n.Video.User.Bio,

                    likes = _db.Likes.Count(l => l.StoryNodeId == n.Id),

                    comments = _db.Comments.Count(c => c.StoryNodeId == n.Id),

                    isLiked = userId != null &&
                        _db.Likes.Any(l =>
                            l.StoryNodeId == n.Id &&
                            l.UserId == userId),

                    hasChildren =
                        _db.StoryNodes.Any(c =>
                            c.ParentNodeId == n.Id),

                    created = n.Video.CreatedAt
                })
                .Take(5)
                .ToListAsync();

            if (children.Count == 0)
            {
                return Ok(new
                {
                    defaultVideo = (object?)null,
                    alternatives = Array.Empty<object>()
                });
            }

            // 🔥 выбираем default
            var chosen = children
                .OrderByDescending(v => v.likes + v.comments)
                .ThenBy(v => v.created)
                .First();

            return Ok(new
            {
                defaultVideo = chosen,
                alternatives = children
            });
        }

        // ===========================
        // FAST PRELOAD NEXT VIDEO
        // ===========================
        [HttpGet("next/{nodeId}")]
        public async Task<IActionResult> GetNext(Guid nodeId)
        {
            var next = await _db.StoryNodes
                .AsNoTracking()
                .Where(n =>
                    n.ParentNodeId == nodeId &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .Select(n => new
                {
                    id = n.Id,
                    url = n.Video.Url,
                    thumbnail = n.Video.ThumbnailUrl,

                    hasChildren =
                        _db.StoryNodes.Any(c =>
                            c.ParentNodeId == n.Id)
                })
                .FirstOrDefaultAsync();

            return Ok(next);
        }
    }
}
