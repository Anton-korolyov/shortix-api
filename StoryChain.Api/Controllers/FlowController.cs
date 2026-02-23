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
        // GET FLOW (DEFAULT + VARIANTS)
        // ===========================
        [HttpGet("{nodeId}")]
        public async Task<IActionResult> GetFlow(Guid nodeId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? userId =
                Guid.TryParse(userIdStr, out var u)
                    ? u
                    : null;

            var children = await _db.StoryNodes
                .Include(n => n.Video)
                .ThenInclude(v => v.User)
                .Where(n =>
                    n.ParentNodeId == nodeId &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .OrderBy(n => n.Video.CreatedAt)
                .ToListAsync();

            if (!children.Any())
            {
                return Ok(new
                {
                    defaultVideo = (object?)null,
                    alternatives = Array.Empty<object>()
                });
            }

            var chosen = await PickDefaultChild(nodeId);

            object Map(StoryNode n) => new
            {
                id = n.Id,
                url = n.Video.Url,

                username = n.Video.User.Username,
                avatarUrl = n.Video.User.AvatarUrl,
                bio = n.Video.User.Bio,

                likes = _db.Likes.Count(l => l.StoryNodeId == n.Id),
                comments = _db.Comments.Count(c => c.StoryNodeId == n.Id),

                isLiked = userId != null &&
                    _db.Likes.Any(l =>
                        l.StoryNodeId == n.Id &&
                        l.UserId == userId),

                hasChildren = _db.StoryNodes
                    .Any(c => c.ParentNodeId == n.Id)
            };

            return Ok(new
            {
                defaultVideo = chosen != null
                    ? Map(chosen)
                    : null,

                alternatives = children.Select(Map)
            });
        }

        // ===========================
        // PICK DEFAULT CHILD
        // ===========================
        private async Task<StoryNode?> PickDefaultChild(Guid parentNodeId)
        {
            // 🔥 Most popular
            var popular = await _db.StoryNodes
                .Include(n => n.Video)
                .ThenInclude(v => v.User)
                .Where(n =>
                    n.ParentNodeId == parentNodeId &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .OrderByDescending(n =>
                    _db.Likes.Count(l => l.StoryNodeId == n.Id) +
                    _db.Comments.Count(c => c.StoryNodeId == n.Id)
                )
                .FirstOrDefaultAsync();

            if (popular != null)
                return popular;

            // 🎲 Random fallback
            return await _db.StoryNodes
                .Include(n => n.Video)
                .ThenInclude(v => v.User)
                .Where(n =>
                    n.ParentNodeId == parentNodeId &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .OrderBy(x => Guid.NewGuid())
                .FirstOrDefaultAsync();
        }
    }
}
