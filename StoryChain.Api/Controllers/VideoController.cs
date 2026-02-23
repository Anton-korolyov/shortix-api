using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.DTO;
using StoryChain.Api.Models;
using StoryChain.Api.Services;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/video")]
    public class VideoController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly VideoJobQueue _queue;
        private readonly R2StorageService _r2;

        private const int MAX_BRANCHES = 5;

        public VideoController(
            AppDbContext db,
            VideoJobQueue queue,
            R2StorageService r2)
        {
            _db = db;
            _queue = queue;
            _r2 = r2;
        }

        // ===========================
        // UPLOAD VIDEO
        // ===========================
        [Authorize]
        [EnableRateLimiting("VideoUploadPolicy")]
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload(
            [FromForm] UploadVideoRequest req
        )
        {
            Console.WriteLine("start upload");
            try
            {
                if (req.File == null || req.File.Length == 0)
                    return BadRequest("File is empty");

                if (req.File.Length > 20 * 1024 * 1024)
                    return BadRequest("File too large (max 20MB)");

                var allowedExtensions = new[] { ".mp4", ".mov", ".webm" };
                var ext = Path.GetExtension(req.File.FileName).ToLower();

                if (!allowedExtensions.Contains(ext))
                    return BadRequest("Invalid video format");

                var userId = Guid.Parse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!
                );

                // ===========================
                // CHECK CATEGORY
                // ===========================
                var category = await _db.VideoCategories
                    .FirstOrDefaultAsync(c => c.Id == req.VideoCategoryId);

                if (category == null)
                    return BadRequest("Invalid category");

                // ===========================
                // UPLOAD TO R2
                // ===========================
                var fileName = $"{Guid.NewGuid()}{ext}";
                var key = $"videos/{fileName}";

                string videoUrl;

                try
                {
                    videoUrl = await _r2.UploadAsync(req.File, key);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(  ex.Message);
                    return StatusCode(500, "Upload failed: " + ex.Message);
                }

                // ===========================
                // CREATE VIDEO
                // ===========================
                var video = new Video
                {
                    UserId = userId,
                    Url = videoUrl,
                    VideoCategoryId = req.VideoCategoryId,
                    Processing = false,
                    IsDeleted = false
                };

                if (req.Tags != null && req.Tags.Any())
                {
                    foreach (var t in req.Tags.Distinct())
                    {
                        video.Tags.Add(new VideoTag
                        {
                            Tag = t.ToLower().Trim()
                        });
                    }
                }

                _db.Videos.Add(video);
                await _db.SaveChangesAsync();

                // ===========================
                // HANDLE PARENT
                // ===========================
                StoryNode? parent = null;

                if (req.ParentNodeId != null)
                {
                    parent = await _db.StoryNodes
                        .FirstOrDefaultAsync(n => n.Id == req.ParentNodeId);

                    if (parent == null)
                        return BadRequest("Parent not found");

                    var childrenCount = await _db.StoryNodes
                        .CountAsync(n => n.ParentNodeId == parent.Id);

                    if (childrenCount >= MAX_BRANCHES)
                        return BadRequest("Branch limit reached");
                }

                // ===========================
                // CREATE STORY NODE
                // ===========================
                var node = new StoryNode
                {
                    StoryId = parent == null
                        ? Guid.NewGuid()
                        : parent.StoryId,

                    VideoId = video.Id,
                    ParentNodeId = req.ParentNodeId,
                    Depth = parent == null ? 0 : parent.Depth + 1
                };

                _db.StoryNodes.Add(node);
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    videoId = video.Id,
                    nodeId = node.Id,
                    url = video.Url
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("UPLOAD ENDPOINT HIT");
                Console.WriteLine("UPLOAD ENDPOINT HIT" + ex.Message);
                return StatusCode(500, "An error occurred: " + ex.Message);
            }
        }

        // ===========================
        // CAN CONTINUE
        // ===========================
        [HttpGet("node/{id}/can-continue")]
        public async Task<IActionResult> CanContinue(Guid id)
        {
            var count = await _db.StoryNodes
                .CountAsync(n => n.ParentNodeId == id);

            return Ok(new
            {
                canContinue = count < MAX_BRANCHES,
                used = count,
                max = MAX_BRANCHES
            });
        }

        // ===========================
        // GET VIDEO
        // ===========================
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetVideo(Guid id)
        {
            var video = await _db.Videos
                .Include(v => v.User)
                .Include(v => v.VideoCategory)
                .Include(v => v.Tags)
                .FirstOrDefaultAsync(v =>
                    v.Id == id &&
                    !v.IsDeleted
                );

            if (video == null)
                return NotFound();

            var node = await _db.StoryNodes
                .FirstOrDefaultAsync(n => n.VideoId == video.Id);

            if (node == null)
                return NotFound("Story node not found");

            var hasChildren = await _db.StoryNodes
                .AnyAsync(n => n.ParentNodeId == node.Id);

            return Ok(new
            {
                id = video.Id,
                url = video.Url,
                username = video.User.Username,
                storyNodeId = node.Id,
                hasChildren,
                category = video.VideoCategory?.Name,
                tags = video.Tags.Select(t => t.Tag)
            });
        }

        // ===========================
        // GET CATEGORIES
        // ===========================
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _db.VideoCategories
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name
                })
                .ToListAsync();

            return Ok(categories);
        }
    }
}