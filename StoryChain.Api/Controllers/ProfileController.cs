using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.Formats.Jpeg;
using StoryChain.Api.Data;
using StoryChain.Api.DTO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ProfileController(AppDbContext db)
        {
            _db = db;
        }

        // ============================
        // GET api/profile/my-videos
        // ============================
        [Authorize]
        [HttpGet("my-videos")]
        public async Task<IActionResult> GetMyVideos([FromQuery] int page = 1, [FromQuery] int pageSize = 18, [FromQuery] string? q = null)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdStr == null) return Unauthorized();

            if (page < 1) page = 1;
            if (pageSize < 6) pageSize = 6;
            if (pageSize > 60) pageSize = 60;

            var userId = Guid.Parse(userIdStr);

            var query = _db.Videos.AsNoTracking()
                .Where(v =>
                    v.UserId == userId &&
                    !v.IsDeleted &&
                    !v.Processing
                );

            // 🔎 ПОИСК (подстрой под свои поля!)
            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();

                query = query.Where(v =>
                    // v.Caption != null && v.Caption.Contains(q) ||
                    // v.Title != null && v.Title.Contains(q) ||
                    // v.Tags != null && v.Tags.Contains(q) ||
                    v.Url.Contains(q) // fallback, чтобы компилилось
                );
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(v => v.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(v => new
                {
                    id = v.Id,
                    // ⚠️ Для грида лучше НЕ отдавать mp4. Но раз jpeg нет — пока отдаём previewUrl как раньше.
                    previewUrl = v.ThumbnailUrl ?? v.Url,
                    createdAt = v.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                items,
                page,
                pageSize,
                total,
                hasMore = page * pageSize < total
            });
        }




        // ============================
        // GET api/profile/me
        // ============================
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdStr == null)
                return Unauthorized();

            var user = await _db.Users
                .Where(u => u.Id == Guid.Parse(userIdStr))
                .Select(u => new
                {
                    username = u.Username,
                    avatarUrl = u.AvatarUrl,
                    bio = u.Bio
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound();

            return Ok(user);
        }

        // ============================
        // PUT api/profile/me
        // ============================
        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile(UpdateProfileDto dto)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdStr == null)
                return Unauthorized();

            var user = await _db.Users.FindAsync(Guid.Parse(userIdStr));
            if (user == null)
                return NotFound();

            user.Bio = dto.Bio;
            user.AvatarUrl = dto.AvatarUrl;

            await _db.SaveChangesAsync();

            return Ok(new { success = true });
        }

        // ============================
        // POST api/profile/avatar
        // ============================
        [Authorize]
        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File missing");

            if (file.Length > 5 * 1024 * 1024)
                return BadRequest("Max 5MB");

            var allowedTypes = new[] { "image/jpeg", "image/png" };

            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest("Only JPG and PNG allowed");

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdStr == null)
                return Unauthorized();

            var user = await _db.Users.FindAsync(Guid.Parse(userIdStr));
            if (user == null)
                return NotFound();

            // ============================
            // DELETE OLD AVATAR
            // ============================

            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                var oldPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Storage",
                    user.AvatarUrl.Replace("/storage/", "").Replace("/", Path.DirectorySeparatorChar.ToString())
                );

                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            // ============================
            // PROCESS IMAGE
            // ============================

            using var image = await Image.LoadAsync(file.OpenReadStream());

            int size = Math.Min(image.Width, image.Height);

            image.Mutate(x =>
            {
                x.Crop(new Rectangle(
                    (image.Width - size) / 2,
                    (image.Height - size) / 2,
                    size,
                    size
                ));

                x.Resize(256, 256);
            });

            var fileName = Guid.NewGuid() + ".jpg";

            var folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Storage",
                "avatars"
            );

            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, fileName);

            await image.SaveAsync(path, new JpegEncoder
            {
                Quality = 75
            });

            var url = "/storage/avatars/" + fileName;

            user.AvatarUrl = url;
            await _db.SaveChangesAsync();

            return Ok(new { avatarUrl = url });
        }



        // ============================
        // GET api/profile/{username}
        // ============================
        [HttpGet("{username}")]
        public async Task<IActionResult> GetProfile(string username)
        {
            var user = await _db.Users
                .Where(u => u.Username == username)
                .Select(u => new
                {
                    id = u.Id,
                    username = u.Username,
                    avatarUrl = u.AvatarUrl,
                    bio = u.Bio,
                    videosCount = _db.Videos.Count(v =>
                        v.UserId == u.Id &&
                        !v.IsDeleted
                    )
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound();

            return Ok(user);
        }

        // ============================
        // GET api/profile/{username}/videos
        // ============================
        [HttpGet("{username}/videos")]
        public async Task<IActionResult> GetUserVideos(string username, [FromQuery] int page = 1, [FromQuery] int pageSize = 18, [FromQuery] string? q = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 6) pageSize = 6;
            if (pageSize > 60) pageSize = 60;

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null) return NotFound();

            var query = _db.Videos.AsNoTracking()
                .Where(v =>
                    v.UserId == user.Id &&
                    !v.IsDeleted &&
                    !v.Processing
                );

            // 🔎 ПОИСК (подстрой под свои поля!)
            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();

                query = query.Where(v =>
                    // v.Caption != null && v.Caption.Contains(q) ||
                    // v.Title != null && v.Title.Contains(q) ||
                    // v.Tags != null && v.Tags.Contains(q) ||
                    v.Url.Contains(q) // fallback
                );
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(v => v.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(v => new
                {
                    id = v.Id,
                    previewUrl = v.ThumbnailUrl ?? v.Url,
                    createdAt = v.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                items,
                page,
                pageSize,
                total,
                hasMore = page * pageSize < total
            });
        }

    }
}
