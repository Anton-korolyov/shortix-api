using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminController(AppDbContext db)
        {
            _db = db;
        }
        [HttpGet("storynodes")]
        public async Task<IActionResult> GetStoryNodes()
        {
            var nodes = await _db.StoryNodes
                .OrderBy(n => n.CreatedAt)
                .Select(n => new
                {
                    id = n.Id,
                    parentNodeId = n.ParentNodeId,
                    storyId = n.StoryId,
                    videoId = n.VideoId
                })
                .ToListAsync();

            return Ok(nodes);
        }
        // ⚠️ ВРЕМЕННО! Очистка всей базы
        [HttpPost("clear")]
        public async Task<IActionResult> ClearAll()
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""Likes"";
                DELETE FROM ""CommentLikes"";
                DELETE FROM ""Comments"";
                DELETE FROM ""StoryNodes"";
                DELETE FROM ""VideoViews"";
                DELETE FROM ""WatchTimes"";
                DELETE FROM ""Videos"";
                DELETE FROM ""Stories"";
                DELETE FROM ""Users"";
                DELETE FROM ""VideoTags"";
                DELETE FROM ""VideoCategories"";
            ");

            return Ok(new { message = "Database cleared" });
        }

        [HttpPost("seed-categories")]
        public async Task<IActionResult> SeedCategories()
        {
            if (await _db.VideoCategories.AnyAsync())
                return BadRequest("Categories already exist");

            var categories = new List<VideoCategory>
    {
        new() { Id = Guid.NewGuid(), Name = "Dance" },
        new() { Id = Guid.NewGuid(), Name = "Funny" },
        new() { Id = Guid.NewGuid(), Name = "Gaming" },
        new() { Id = Guid.NewGuid(), Name = "Music" },
        new() { Id = Guid.NewGuid(), Name = "Story" },
        new() { Id = Guid.NewGuid(), Name = "Sport" },
        new() { Id = Guid.NewGuid(), Name = "Lifestyle" },
        new() { Id = Guid.NewGuid(), Name = "Education" }
    };

            _db.VideoCategories.AddRange(categories);
            await _db.SaveChangesAsync();

            return Ok(categories);
        }
    }
}
