using System.Diagnostics;
using System.Globalization;
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
        private readonly R2VideoService _r2;

        private const int MAX_BRANCHES = 5;
        private const long MAX_FILE_SIZE = 100 * 1024 * 1024; // 100MB

        public VideoController(
            AppDbContext db,
            VideoJobQueue queue,
            R2VideoService r2
        )
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
        [RequestSizeLimit(100_000_000)]
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload([FromForm] UploadVideoRequest req)
        {
            const int MAX_DURATION = 60;
            const int MAX_DEPTH = 10;

            if (req.File == null || req.File.Length == 0)
                return BadRequest("File is empty");

            if (req.File.Length > MAX_FILE_SIZE)
                return BadRequest("File too large (max 100MB)");

            var allowedExtensions = new[] { ".mp4", ".mov", ".webm" };
            var ext = Path.GetExtension(req.File.FileName).ToLower();

            if (!allowedExtensions.Contains(ext))
                return BadRequest("Invalid video format");

            if (!req.File.ContentType.StartsWith("video/"))
                return BadRequest("Invalid file type");

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var category = await _db.VideoCategories
                .FirstOrDefaultAsync(c => c.Id == req.VideoCategoryId);

            if (category == null)
                return BadRequest("Invalid category");

            Directory.CreateDirectory("uploads");

            var tempOriginal = Path.Combine("uploads", Guid.NewGuid() + ext);
            var tempCompressed = Path.Combine("uploads", Guid.NewGuid() + "_compressed.mp4");

            var thumb1 = Path.Combine("uploads", Guid.NewGuid() + "_1.jpg");
            var thumb2 = Path.Combine("uploads", Guid.NewGuid() + "_2.jpg");
            var thumb3 = Path.Combine("uploads", Guid.NewGuid() + "_3.jpg");

            try
            {
                // SAVE ORIGINAL
                await using (var fs = new FileStream(tempOriginal, FileMode.Create))
                {
                    await req.File.CopyToAsync(fs);
                }

                // GET DURATION
                var duration = await GetVideoDuration(tempOriginal);

                if (duration > MAX_DURATION)
                    return BadRequest($"Video too long (max {MAX_DURATION} sec)");

                // COMPRESS VIDEO
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments =
                    $"-i \"{tempOriginal}\" -vf \"scale=-2:1280\" -r 30 -c:v libx264 -preset veryfast -crf 27 -b:v 1500k -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart \"{tempCompressed}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                var process = Process.Start(psi);

                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine("FFmpeg error:");
                    Console.WriteLine(error);

                    return StatusCode(500, "Video compression failed");
                }

                // освобождаем место
                SafeDelete(tempOriginal);

                // GENERATE THUMBNAILS
                var t1 = duration * 0.2;
                var t2 = duration * 0.5;
                var t3 = duration * 0.8;

                await RunFfmpegFrame(tempCompressed, thumb1, t1);
                await RunFfmpegFrame(tempCompressed, thumb2, t2);
                await RunFfmpegFrame(tempCompressed, thumb3, t3);

                var bestThumb = thumb2;

                // UPLOAD VIDEO
                var videoFileName = $"videos/{Guid.NewGuid()}.mp4";

                await using var videoStream = System.IO.File.OpenRead(tempCompressed);

                await _r2.UploadVideoAsync(
                    videoFileName,
                    videoStream,
                    "video/mp4"
                );

                var videoUrl = _r2.GetPublicUrl(videoFileName);

                // UPLOAD THUMBNAIL
                var thumbFileName = $"thumbs/{Guid.NewGuid()}.jpg";

                await using var thumbStream = System.IO.File.OpenRead(bestThumb);

                await _r2.UploadVideoAsync(
                    thumbFileName,
                    thumbStream,
                    "image/jpeg"
                );

                var thumbUrl = _r2.GetPublicUrl(thumbFileName);

                // CREATE VIDEO
                var video = new Video
                {
                    UserId = userId,
                    Url = videoUrl,
                    ThumbnailUrl = thumbUrl,
                    DurationSec = (int)duration,
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

                // HANDLE PARENT
                StoryNode? parent = null;

                if (req.ParentNodeId != null)
                {
                    parent = await _db.StoryNodes
                        .FirstOrDefaultAsync(n => n.Id == req.ParentNodeId);

                    if (parent == null)
                        return BadRequest("Parent not found");

                    if (parent.Depth >= MAX_DEPTH)
                        return BadRequest("Max story depth reached");

                    var childrenCount = await _db.StoryNodes
                        .CountAsync(n => n.ParentNodeId == parent.Id);

                    if (childrenCount >= MAX_BRANCHES)
                        return BadRequest("Branch limit reached");
                }

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
                    url = videoUrl,
                    thumbnail = thumbUrl,
                    duration = duration
                });
            }
            finally
            {
                SafeDelete(tempOriginal);
                SafeDelete(tempCompressed);
                SafeDelete(thumb1);
                SafeDelete(thumb2);
                SafeDelete(thumb3);
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
        [HttpGet("{id}")]
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
        // CATEGORIES
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
        // ===========================
        // DELETE VIDEO
        // ===========================
        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteVideo(Guid id)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var video = await _db.Videos
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (video == null)
                return NotFound();

            if (video.UserId != userId)
                return Forbid();

            // находим story node
            var node = await _db.StoryNodes
                .FirstOrDefaultAsync(n => n.VideoId == id);

            if (node == null)
                return BadRequest("Node not found");

            // проверяем есть ли дети
            var hasChildren = await _db.StoryNodes
                .AnyAsync(n => n.ParentNodeId == node.Id);

            if (hasChildren)
            {
                return BadRequest(new
                {
                    message = "Video has children"
                });
            }

            // soft delete
            video.IsDeleted = true;

            // удаляем node
            _db.StoryNodes.Remove(node);

            await _db.SaveChangesAsync();

            return Ok(new
            {
                redirectTo = "feed"
            });
        }
        private async Task<double> GetVideoDuration(string path)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            var process = Process.Start(psi);

            var result = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();

            return double.Parse(result, CultureInfo.InvariantCulture);
        }

        private async Task RunFfmpegFrame(string video, string output, double second)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-ss {second.ToString(CultureInfo.InvariantCulture)} -i \"{video}\" -frames:v 1 \"{output}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var p = Process.Start(psi);
            await p.WaitForExitAsync();
        }

        private void SafeDelete(string path)
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }
}