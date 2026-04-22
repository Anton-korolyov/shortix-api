using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BoostController : ControllerBase
    {
        private readonly AppDbContext _db;

        public BoostController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            Guid videoId,
            int budget,
            int days)
        {
            var userId = Guid.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value
            );

            var boost = new VideoBoost
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                UserId = userId,
                Budget = budget,
                Days = days,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(days),
                Active = true
            };

            _db.VideoBoosts.Add(boost);

            await _db.SaveChangesAsync();

            return Ok(boost);
        }
    }
}
