using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Api;
using StoryChain.Api.Data;
using StoryChain.Api.DTO;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommentController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public CommentController(
            AppDbContext db,
            IHubContext<NotificationHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // ============================
        // CREATE COMMENT
        // ============================
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] AddCommentDto dto)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var me = await _db.Users.FindAsync(userId);

            var comment = new Comment
            {
                UserId = userId,
                StoryNodeId = dto.StoryNodeId,
                ParentCommentId = dto.ParentCommentId,
                Text = dto.Text
            };

            _db.Comments.Add(comment);
            await _db.SaveChangesAsync();
            // ⚡ realtime count
            var count = await _db.Comments
                .CountAsync(c => c.StoryNodeId == dto.StoryNodeId);

            await _hub.Clients.All.SendAsync(
                "VideoCommented",
                new
                {
                    nodeId = dto.StoryNodeId,
                    count = count
                }
            );
            // ============================
            // FIND VIDEO OWNER
            // ============================
            var videoOwnerId = await _db.StoryNodes
                .Where(n => n.Id == dto.StoryNodeId)
                .Select(n => n.Video.UserId)
                .FirstAsync();

            // don't notify yourself
            if (videoOwnerId != userId)
            {
                var notification = new Notification
                {
                    UserId = videoOwnerId,
                    Type = "comment",
                    Message = $"{me!.Username} commented on your video"
                };

                _db.Notifications.Add(notification);
                await _db.SaveChangesAsync();

                // realtime
                await _hub.Clients
                    .Group(videoOwnerId.ToString())
                    .SendAsync("ReceiveNotification", new
                    {
                        type = notification.Type,
                        message = notification.Message
                    });
            }

            return Ok(comment);
        }

        // ============================
        // GET COMMENTS
        // ============================
        [HttpGet("{nodeId}")]
        public async Task<IActionResult> GetByNode(Guid nodeId)
        {
            var comments = await _db.Comments
                .Include(c => c.User)
                .Where(c => c.StoryNodeId == nodeId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    id = c.Id,
                    text = c.Text,
                    userName = c.User.Username
                })
                .ToListAsync();

            return Ok(comments);
        }

        // ============================
        // TREE
        // ============================
        [HttpGet("{nodeId}/tree")]
        public async Task<IActionResult> GetTree(Guid nodeId)
        {
            var comments = await _db.Comments
                .Where(x => x.StoryNodeId == nodeId)
                .ToListAsync();

            var map = comments.ToDictionary(
                c => c.Id,
                c => new CommentTreeDto
                {
                    Id = c.Id,
                    Text = c.Text,
                    CreatedAt = c.CreatedAt
                }
            );

            var roots = new List<CommentTreeDto>();

            foreach (var c in comments)
            {
                if (c.ParentCommentId == null)
                    roots.Add(map[c.Id]);
                else
                    map[c.ParentCommentId.Value]
                        .Replies.Add(map[c.Id]);
            }

            return Ok(roots);
        }
    }
}
