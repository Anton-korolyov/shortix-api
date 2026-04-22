using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StoryChain.Api.Data;

namespace StoryChain.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDatabase _redis;

    public FeedController(AppDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis.GetDatabase();
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        int page = 1,
        int pageSize = 10,
        Guid? categoryId = null,
        bool following = false)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 50) pageSize = 50;

        Guid? userId = null;

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (idClaim != null && Guid.TryParse(idClaim.Value, out var parsed))
        {
            userId = parsed;
        }

        string cacheKey = $"feed:{userId}:{page}:{categoryId}:{following}";

        /////////////////////////////////////////////////////
        // REDIS CACHE
        /////////////////////////////////////////////////////

        var cached = await _redis.StringGetAsync(cacheKey);

        if (!cached.IsNullOrEmpty)
        {
            return Content(cached!, "application/json");
        }

        var now = DateTime.UtcNow;

        /////////////////////////////////////////////////////
        // BOOST
        /////////////////////////////////////////////////////

        var boostedSet = (await _db.VideoBoosts
            .AsNoTracking()
            .Where(b => b.Active && b.EndDate > now)
            .Select(b => b.VideoId)
            .ToListAsync())
            .ToHashSet();

        /////////////////////////////////////////////////////
        // BASE QUERY
        /////////////////////////////////////////////////////

        var query = _db.StoryNodes
            .AsNoTracking()
            .Where(n =>
                n.ParentNodeId == null &&
                !n.Video.IsDeleted &&
                !n.Video.Processing
            )
            .Select(n => new
            {
                nodeId = n.Id,
                videoId = n.VideoId,
                url = n.Video.Url,
                thumbnailUrl = n.Video.ThumbnailUrl,
                createdAt = n.Video.CreatedAt,

                userId = n.Video.UserId,
                username = n.Video.User.Username,
                avatarUrl = n.Video.User.AvatarUrl,
                bio = n.Video.User.Bio
            });

        /////////////////////////////////////////////////////
        // FOLLOWING FILTER
        /////////////////////////////////////////////////////

        if (following && userId != null)
        {
            query = query.Where(n =>
                _db.Followers.Any(f =>
                    f.FollowerUserId == userId &&
                    f.FollowingUserId == n.userId
                )
            );
        }

        /////////////////////////////////////////////////////
        // CATEGORY FILTER
        /////////////////////////////////////////////////////

        if (categoryId != null)
        {
            query = query.Where(n =>
                n.videoId != Guid.Empty &&
                n.videoId == n.videoId
            );
        }

        /////////////////////////////////////////////////////
        // LOAD VIDEOS
        /////////////////////////////////////////////////////

        var loadedVideos = await query
            .OrderByDescending(v => v.createdAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize * 5)
            .ToListAsync();

        /////////////////////////////////////////////////////
        // BOOST MIX
        /////////////////////////////////////////////////////

        var normalVideos = loadedVideos
            .Where(v => !boostedSet.Contains(v.videoId))
            .ToList();

        var boostVideos = loadedVideos
            .Where(v => boostedSet.Contains(v.videoId))
            .ToList();

        var videos = new List<dynamic>();

        int normalIndex = 0;
        int boostIndex = 0;

        while (videos.Count < pageSize && normalIndex < normalVideos.Count)
        {
            for (int i = 0; i < 4 && normalIndex < normalVideos.Count && videos.Count < pageSize; i++)
            {
                videos.Add(normalVideos[normalIndex]);
                normalIndex++;
            }

            if (boostVideos.Count > 0 && videos.Count < pageSize)
            {
                videos.Add(boostVideos[boostIndex % boostVideos.Count]);
                boostIndex++;
            }
        }

        /////////////////////////////////////////////////////
        // ADS
        /////////////////////////////////////////////////////

        var ads = await _db.Ads
            .AsNoTracking()
            .Where(a =>
                a.Active &&
                a.StartDate <= now &&
                a.EndDate >= now &&
                a.Views < a.Budget
            )
            .Take(5)
            .ToListAsync();

        /////////////////////////////////////////////////////
        // MERGE ADS
        /////////////////////////////////////////////////////

        List<object> feed = new();

        int adIndex = 0;
        int adEvery = 5;

        foreach (var video in videos)
        {
            feed.Add(new
            {
                type = "video",
                id = video.nodeId,
                videoId = video.videoId,
                url = video.url,
                thumbnailUrl = video.thumbnailUrl,
                username = video.username,
                avatarUrl = video.avatarUrl,
                bio = video.bio
            });

            if (feed.Count % adEvery == 0 && ads.Count > 0)
            {
                var ad = ads[adIndex % ads.Count];

                feed.Add(new
                {
                    type = "ad",
                    id = ad.Id,
                    adType = ad.Type,
                    mediaUrl = ad.MediaUrl,
                    link = ad.Link
                });

                adIndex++;
            }
        }

        /////////////////////////////////////////////////////
        // UPDATE AD VIEWS
        /////////////////////////////////////////////////////

        if (ads.Count > 0)
        {
            var adIds = ads.Select(a => a.Id).ToList();

            var dbAds = await _db.Ads
                .Where(a => adIds.Contains(a.Id))
                .ToListAsync();

            foreach (var ad in dbAds)
            {
                ad.Views++;
            }

            await _db.SaveChangesAsync();
        }

        /////////////////////////////////////////////////////
        // RESULT
        /////////////////////////////////////////////////////

        var result = new
        {
            page,
            pageSize,
            items = feed
        };

        /////////////////////////////////////////////////////
        // SAVE REDIS
        /////////////////////////////////////////////////////

        await _redis.StringSetAsync(
            cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(30)
        );

        return Ok(result);
    }
}