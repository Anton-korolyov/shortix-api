using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WatchController : ControllerBase
    {
        private readonly AppDbContext _db;

        public WatchController(AppDbContext db)
        {
            _db = db;
        }

        // ===========================
        // SAVE WATCH TIME
        // ===========================
        [Authorize]
        [HttpPost("watchtime")]
        public async Task<IActionResult> SaveWatchTime(
            Guid videoId,
            double seconds)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            _db.WatchTimes.Add(new WatchTime
            {
                UserId = userId,
                VideoId = videoId,
                Seconds = seconds
            });

            await _db.SaveChangesAsync();

            return Ok();
        }
    }
}
