using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedController : ControllerBase
    {
        private readonly AppDbContext _db;

        public FeedController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            int page = 1,
            int pageSize = 10,
            Guid? categoryId = null,
            bool following = false // 👈 ДОБАВИЛИ
        )
        {
            if (page < 1) page = 1;
            if (pageSize > 50) pageSize = 50;

            // =========================
            // GET CURRENT USER
            // =========================

            Guid? userId = null;

            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (idClaim != null)
            {
                userId = Guid.Parse(idClaim.Value);
            }

            // =========================
            // BASE QUERY
            // =========================

            var query = _db.StoryNodes
      .Include(n => n.Video)
          .ThenInclude(v => v.User)

      // 🔥 КАТЕГОРИЯ
      .Include(n => n.Video)
          .ThenInclude(v => v.VideoCategory)

      // 🔥 ТЕГИ
      .Include(n => n.Video)
          .ThenInclude(v => v.Tags)

      .Where(n =>
          n.ParentNodeId == null &&
          !n.Video.IsDeleted &&
          !n.Video.Processing
      );

            // =========================
            // 🔥 FOLLOWING MODE
            // =========================

            if (following && userId != null)
            {
                query = query.Where(n =>
                    _db.Followers.Any(f =>
                        f.FollowerUserId == userId &&
                        f.FollowingUserId == n.Video.UserId
                    )
                    && n.Video.UserId != userId   // 👈 ВАЖНО
                );
            }

            // =========================
            // CATEGORY FILTER
            // =========================

            if (categoryId != null)
            {
                query = query.Where(n =>
                    n.Video.VideoCategoryId == categoryId
                );
            }

            query = query.OrderByDescending(n => n.Video.CreatedAt);

            var total = await query.CountAsync();

            // =========================
            // PAGE
            // =========================

            var nodes = await query
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .Select(n => new
      {
          type = "video",
          id = n.Id,
          videoId = n.VideoId,
          url = n.Video.Url,
          thumbnailUrl = n.Video.ThumbnailUrl,

          // 🔥 ВОТ ОНО
          category = n.Video.VideoCategory != null
              ? n.Video.VideoCategory.Name
              : null,

          tags = n.Video.Tags
              .Select(t => t.Tag)
              .ToList(),

          createdAt = n.Video.CreatedAt,

          likes = _db.Likes.Count(l => l.StoryNodeId == n.Id),
          comments = _db.Comments.Count(c => c.StoryNodeId == n.Id),

          hasChildren = _db.StoryNodes.Any(c => c.ParentNodeId == n.Id),

          isLiked = userId != null &&
                     _db.Likes.Any(l =>
                         l.StoryNodeId == n.Id &&
                         l.UserId == userId),

          username = n.Video.User.Username,
          avatarUrl = n.Video.User.AvatarUrl,
          bio = n.Video.User.Bio
      })
      .ToListAsync();
            var result = new List<object>();

            for (int i = 0; i < nodes.Count; i++)
            {
                result.Add(nodes[i]);

                if ((i + 1) % 5 == 0)
                {
                    //result.Add(new
                    //{
                    //    type = "ad",
                    //    adId = Guid.NewGuid(),
                    //    videoUrl = "/ads/sample.mp4",
                    //    targetUrl = "https://example.com"
                    //});
                }
            }
            return Ok(new
            {
                page,
                pageSize,
                total,
                hasMore = page * pageSize < total,
                items = result
            });
        }



        [HttpGet("explore")]
        public async Task<IActionResult> Explore(
          int page = 1,
          string? q = null,
          string? category = null,
          string? tags = null
      )
        {
            const int pageSize = 12;

            var query = _db.StoryNodes
                .Include(n => n.Video)
                    .ThenInclude(v => v.User)
                .Include(n => n.Video)
                    .ThenInclude(v => v.VideoCategory)
                .Include(n => n.Video)
                    .ThenInclude(v => v.Tags)
                .Where(n =>
                    n.ParentNodeId == null &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .AsQueryable();

            /* ===========================
               CATEGORY FILTER
            =========================== */

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();

                query = query.Where(n =>
                    n.Video.VideoCategory != null &&
                    n.Video.VideoCategory.Name == cat
                );
            }

            /* ===========================
               TAGS FILTER
            =========================== */

            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagList = tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .ToList();

                query = query.Where(n =>
                    n.Video.Tags.Any(t => tagList.Contains(t.Tag))
                );
            }

            /* ===========================
               SEARCH (AUTHOR + CAPTION + CATEGORY + TAGS)
            =========================== */

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();

                query = query.Where(n =>
                    EF.Functions.ILike(n.Video.User.Username, $"%{term}%") ||
               
                    (n.Video.VideoCategory != null && EF.Functions.ILike(n.Video.VideoCategory.Name, $"%{term}%")) ||
                    n.Video.Tags.Any(t => EF.Functions.ILike(t.Tag, $"%{term}%"))
                );
            }

            /* ===========================
               PAGINATION
            =========================== */

            var items = await query
                .OrderByDescending(n => n.Video.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize + 1)
                .ToListAsync();

            return Ok(new
            {
                items = items.Take(pageSize).Select(n => new
                {
                    id = n.Video.Id,
                    storyNodeId = n.Id,
                    url = n.Video.Url,
                    username = n.Video.User.Username,
                    avatarUrl = n.Video.User.AvatarUrl,
                    bio = n.Video.User.Bio,
                    category = n.Video.VideoCategory != null ? n.Video.VideoCategory.Name : null,
                    tags = n.Video.Tags.Select(t => t.Tag),

                    likes = _db.Likes.Count(l => l.StoryNodeId == n.Id),
                    comments = _db.Comments.Count(c => c.StoryNodeId == n.Id),
                    hasChildren = _db.StoryNodes.Any(c => c.ParentNodeId == n.Id)
                }),
                hasMore = items.Count > pageSize
            });
        }

    }
}
