using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/story")]
    public class StoryController : ControllerBase
    {
        private readonly AppDbContext _db;

        public StoryController(AppDbContext db)
        {
            _db = db;
        }

        [Authorize]
        [HttpPost("create")]
        public async Task<IActionResult> Create(Guid videoId)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var story = new Story
            {
                RootVideoId = videoId,
                CreatedBy = userId
            };

            _db.Stories.Add(story);
            await _db.SaveChangesAsync();

            _db.StoryNodes.Add(new StoryNode
            {
                StoryId = story.Id,
                VideoId = videoId,
                Depth = 0
            });

            await _db.SaveChangesAsync();

            return Ok(story);
        }

        [Authorize]
        [HttpPost("continue")]
        public async Task<IActionResult> Continue(
            Guid storyId,
            Guid parentNodeId,
            Guid videoId)
        {
            var parent = await _db.StoryNodes.FindAsync(parentNodeId);

            var node = new StoryNode
            {
                StoryId = storyId,
                VideoId = videoId,
                ParentNodeId = parentNodeId,
                Depth = parent!.Depth + 1
            };

            _db.StoryNodes.Add(node);
            await _db.SaveChangesAsync();

            return Ok(node);
        }

        [HttpGet("{storyId}")]
        public async Task<IActionResult> GetTree(Guid storyId)
        {
            var nodes = await _db.StoryNodes
                .Where(x => x.StoryId == storyId)
                .ToListAsync();

            return Ok(nodes);
        }

        [HttpGet("{storyId}/top")]
        public async Task<IActionResult> GetTopBranches(Guid storyId)
        {
            var result = await _db.StoryNodes
                .Where(n => n.StoryId == storyId)
                .Select(n => new
                {
                    node = n,
                    likes = _db.Likes.Count(l => l.StoryNodeId == n.Id)
                })
                .OrderByDescending(x => x.likes)
                .Take(10)
                .ToListAsync();

            return Ok(result);
        }
    }
}
