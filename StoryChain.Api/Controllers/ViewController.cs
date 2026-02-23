using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ViewController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ViewController(AppDbContext db)
        {
            _db = db;
        }

        [Authorize]
        [HttpPost("{videoId}")]
        public async Task<IActionResult> AddView(Guid videoId)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!
            );

            var exists = await _db.VideoViews
                .AnyAsync(v => v.VideoId == videoId && v.UserId == userId);

            if (exists)
                return Ok(); // один просмотр с пользователя

            _db.VideoViews.Add(new VideoView
            {
                UserId = userId,
                VideoId = videoId
            });

            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}
