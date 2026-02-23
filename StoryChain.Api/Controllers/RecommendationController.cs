using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
namespace StoryChain.Api.Controllers
{
  
    [ApiController]
    [Route("api/recommendations")]
    public class RecommendationController : ControllerBase
    {
        private readonly AppDbContext _db;

        public RecommendationController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var now = DateTime.UtcNow;

            // ============================
            // USER
            // ============================
            Guid? userId = null;

            if (User.Identity?.IsAuthenticated == true)
            {
                userId = Guid.Parse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!
                );
            }

            // ============================
            // USER FAVORITE CATEGORIES
            // ============================
            List<Guid> favoriteCategoryIds = new();

            if (userId != null)
            {
                favoriteCategoryIds = await _db.WatchTimes
                    .Where(w => w.UserId == userId)
                    .Join(
                        _db.Videos,
                        w => w.VideoId,
                        v => v.Id,
                        (w, v) => v.VideoCategoryId
                    )
                    .Where(c => c != null)
                    .GroupBy(c => c)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key!.Value)
                    .Take(3)
                    .ToListAsync();
            }

            // ============================
            // LOAD VIDEOS
            // ============================
            var videos = await _db.Videos
                .Where(v => !v.IsDeleted && !v.Processing)
                .Select(v => new
                {
                    v.Id,
                    v.Url,
                    v.ThumbnailUrl,
                    v.VideoCategoryId,
                    v.CreatedAt,

                    Likes = _db.StoryNodes
                        .Where(n => n.VideoId == v.Id)
                        .Join(
                            _db.Likes,
                            n => n.Id,
                            l => l.StoryNodeId,
                            (n, l) => l
                        )
                        .Count(),

                    Comments = _db.StoryNodes
                        .Where(n => n.VideoId == v.Id)
                        .Join(
                            _db.Comments,
                            n => n.Id,
                            c => c.StoryNodeId,
                            (n, c) => c
                        )
                        .Count(),

                    AvgWatchSeconds = _db.WatchTimes
                        .Where(w => w.VideoId == v.Id)
                        .Select(w => w.Seconds)
                        .DefaultIfEmpty(0)
                        .Average(),

                    UserLikedSameStory =
                        userId != null &&
                        _db.Likes.Any(l =>
                            l.UserId == userId &&
                            _db.StoryNodes.Any(n =>
                                n.Id == l.StoryNodeId &&
                                n.VideoId == v.Id)),

                    IsFavoriteCategory =
    v.VideoCategoryId != null &&
    favoriteCategoryIds.Contains(v.VideoCategoryId.Value)
                })
                .ToListAsync();

            // ============================
            // SCORE
            // ============================
            var result = videos
                .Select(v => new
                {
                    v.Id,
                    v.Url,
                    v.ThumbnailUrl,

                    Score =
                        (v.Likes * 2) +
                        (v.Comments) +
                        (v.AvgWatchSeconds * 0.3) +
                        Math.Max(
                            0,
                            24 - (int)(now - v.CreatedAt).TotalHours
                        ) +
                        (v.UserLikedSameStory ? 10 : 0) +
                        (v.IsFavoriteCategory ? 8 : 0)
                })
                .OrderByDescending(x => x.Score)
                .Take(20)
                .ToList();

            return Ok(result);
        }
    }
}
