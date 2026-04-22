using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdsController(AppDbContext db)
        {
            _db = db;
        }

        // ================= CREATE AD =================

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Ad model)
        {
            model.Id = Guid.NewGuid();
            model.Views = 0;
            model.Clicks = 0;
            model.Active = false;

            _db.Ads.Add(model);
            await _db.SaveChangesAsync();

            return Ok(model);
        }

        // ================= ACTIVATE AD (after payment) =================

        [HttpPost("activate/{id}")]
        public async Task<IActionResult> Activate(Guid id)
        {
            var ad = await _db.Ads.FindAsync(id);

            if (ad == null)
                return NotFound();

            ad.Active = true;
            ad.StartDate = DateTime.UtcNow;
            ad.EndDate = DateTime.UtcNow.AddDays(ad.Days);

            await _db.SaveChangesAsync();

            return Ok(ad);
        }

        // ================= GET ACTIVE ADS =================

        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            var now = DateTime.UtcNow;

            var ads = await _db.Ads
                .Where(a =>
                    a.Active &&
                    a.StartDate <= now &&
                    a.EndDate >= now &&
                    a.Views < a.Budget
                )
                .Select(a => new
                {
                    type = "ad",
                    id = a.Id,
                    adType = a.Type,
                    mediaUrl = a.MediaUrl,
                    link = a.Link
                })
                .ToListAsync();

            return Ok(ads);
        }

        // ================= REGISTER AD VIEW =================

        [HttpPost("view/{id}")]
        public async Task<IActionResult> RegisterView(Guid id)
        {
            var ad = await _db.Ads.FindAsync(id);

            if (ad == null)
                return NotFound();

            if (ad.Views >= ad.Budget)
                return Ok();

            ad.Views += 1;

            if (ad.Views >= ad.Budget)
                ad.Active = false;

            await _db.SaveChangesAsync();

            return Ok();
        }

        // ================= REGISTER CLICK =================

        [HttpPost("click/{id}")]
        public async Task<IActionResult> RegisterClick(Guid id)
        {
            var ad = await _db.Ads.FindAsync(id);

            if (ad == null)
                return NotFound();

            ad.Clicks += 1;

            await _db.SaveChangesAsync();

            return Ok();
        }

        // ================= GET USER ADS =================

        [HttpGet("my/{userId}")]
        public async Task<IActionResult> GetUserAds(Guid userId)
        {
            var ads = await _db.Ads
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.StartDate)
                .ToListAsync();

            return Ok(ads);
        }
    }
}
