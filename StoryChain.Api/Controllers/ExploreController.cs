using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;

namespace StoryChain.Api.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class ExploreController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ExploreController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            int page = 1,
            int pageSize = 20,
            string? q = null
        )
        {
            if (page < 1) page = 1;
            if (pageSize > 60) pageSize = 60;

            var query = _db.StoryNodes
                .Where(n =>
                    n.ParentNodeId == null &&
                    !n.Video.IsDeleted &&
                    !n.Video.Processing
                )
                .Select(n => new
                {
                    id = n.Id,
                    url = n.Video.Url,
                    thumbnailUrl = n.Video.ThumbnailUrl,
                    createdAt = n.Video.CreatedAt,
                    username = n.Video.User.Username,
                    category = n.Video.VideoCategory != null
                        ? n.Video.VideoCategory.Name
                        : "",
                    tags = n.Video.Tags.Select(t => t.Tag)
                });

            // SEARCH
            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.ToLower();

                query = query.Where(v =>
                    v.username.ToLower().Contains(search) ||
                    v.category.ToLower().Contains(search) ||
                    v.tags.Any(t => t.ToLower().Contains(search))
                );
            }

            query = query.OrderByDescending(v => v.createdAt);

            var total = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new
            {
                page,
                pageSize,
                total,
                hasMore = page * pageSize < total,
                items
            });
        }
    }
}