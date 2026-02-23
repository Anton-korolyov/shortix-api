using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly AppDbContext _db;

        public NotificationController(AppDbContext db)
        {
            _db = db;
        }

        // GET api/notifications/unread-count
        [Authorize]
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var count = await _db.Notifications
                .CountAsync(x =>
                    x.UserId == userId &&
                    !x.IsRead
                );

            return Ok(new { count });
        }

        // GET api/notifications
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var list = await _db.Notifications
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Type,
                    x.Message,
                    x.IsRead,
                    x.CreatedAt
                })
                .ToListAsync();

            return Ok(list);
        }
        // POST api/notifications/mark-read
        [Authorize]
        [HttpPost("mark-read")]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var unread = await _db.Notifications
                .Where(x => x.UserId == userId && !x.IsRead)
                .ToListAsync();

            foreach (var n in unread)
                n.IsRead = true;

            await _db.SaveChangesAsync();

            return Ok();
        }

    }
}
