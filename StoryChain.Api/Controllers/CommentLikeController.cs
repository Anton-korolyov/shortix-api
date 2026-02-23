using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Api;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommentLikeController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public CommentLikeController(
            AppDbContext db,
            IHubContext<NotificationHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // ============================
        // TOGGLE LIKE
        // ============================
        [Authorize]
        [HttpPost("{commentId}")]
        public async Task<IActionResult> Toggle(Guid commentId)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var me = await _db.Users.FindAsync(userId);

            var comment = await _db.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null)
                return NotFound();

            var existing = await _db.CommentLikes
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.CommentId == commentId);

            // ============================
            // UNLIKE
            // ============================
            if (existing != null)
            {
                _db.CommentLikes.Remove(existing);
                await _db.SaveChangesAsync();
                return Ok(new { liked = false });
            }

            // ============================
            // LIKE
            // ============================
            var like = new CommentLike
            {
                UserId = userId,
                CommentId = commentId
            };

            _db.CommentLikes.Add(like);

            // don't notify yourself
            if (comment.UserId != userId)
            {
                var notification = new Notification
                {
                    UserId = comment.UserId,
                    Type = "comment_like",
                    Message = $"{me!.Username} liked your comment"
                };

                _db.Notifications.Add(notification);

                await _hub.Clients
                    .Group(comment.UserId.ToString())
                    .SendAsync("ReceiveNotification", new
                    {
                        type = notification.Type,
                        message = notification.Message
                    });
            }

            await _db.SaveChangesAsync();

            return Ok(new { liked = true });
        }

        // ============================
        // COUNT
        // ============================
        [HttpGet("{commentId}/count")]
        public async Task<IActionResult> Count(Guid commentId)
        {
            var count = await _db.CommentLikes
                .CountAsync(x => x.CommentId == commentId);

            return Ok(count);
        }
    }
}
