using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Api;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LikeController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public LikeController(
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
        [HttpPost("{nodeId}")]
        public async Task<IActionResult> ToggleLike(Guid nodeId)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var me = await _db.Users.FindAsync(userId);

            var node = await _db.StoryNodes.FindAsync(nodeId);
            if (node == null)
                return NotFound();

            var video = await _db.Videos.FindAsync(node.VideoId);
            if (video == null)
                return NotFound("Video not found");

            // ❌ нельзя лайкать своё видео
            if (video.UserId == userId)
            {
                var count = await _db.Likes
                    .CountAsync(x => x.StoryNodeId == nodeId);

                return Ok(new { liked = false, count });
            }

            var existing = await _db.Likes
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.StoryNodeId == nodeId);

            // ============================
            // UNLIKE
            // ============================
            if (existing != null)
            {
                _db.Likes.Remove(existing);
                await _db.SaveChangesAsync();

                var count = await _db.Likes
                    .CountAsync(x => x.StoryNodeId == nodeId);
                  await _hub.Clients.All.SendAsync(
                      "VideoLiked",
                     new
      {
        nodeId = nodeId,
        liked = false,
        count = count
    }
);

                return Ok(new { liked = false, count });
            }

            // ============================
            // LIKE
            // ============================
            var like = new Like
            {
                UserId = userId,
                StoryNodeId = nodeId
            };

            _db.Likes.Add(like);

            // 🔔 SAVE NOTIFICATION
            var notification = new Notification
            {
                UserId = video.UserId,
                Type = "like",
                Message = $"{me!.Username} liked your video"
            };

            _db.Notifications.Add(notification);

            await _db.SaveChangesAsync();

            // ⚡ SEND REALTIME
            await _hub.Clients
                .Group(video.UserId.ToString())
                .SendAsync("ReceiveNotification", new
                {
                    type = notification.Type,
                    message = notification.Message
                });

            var newCount = await _db.Likes
                .CountAsync(x => x.StoryNodeId == nodeId);


            await _hub.Clients.All.SendAsync(
    "VideoLiked",
    new
    {
        nodeId = nodeId,
        liked = true,
        count = newCount
    }
);



            return Ok(new { liked = true, count = newCount });
        }

        // ============================
        // COUNT
        // ============================
        [HttpGet("{nodeId}/count")]
        public async Task<IActionResult> GetCount(Guid nodeId)
        {
            var count = await _db.Likes
                .CountAsync(x => x.StoryNodeId == nodeId);

            return Ok(count);
        }
    }
}
