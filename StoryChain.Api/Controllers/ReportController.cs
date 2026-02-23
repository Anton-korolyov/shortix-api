using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ReportController(AppDbContext db)
        {
            _db = db;
        }

        // report video or comment
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create(
            Guid? videoId,
            Guid? commentId,
            string reason)
        {
            if (videoId == null && commentId == null)
                return BadRequest("videoId or commentId required");

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var alreadyReported = await _db.Reports.AnyAsync(x =>
                x.ReporterUserId == userId &&
                x.VideoId == videoId &&
                x.CommentId == commentId);

            if (alreadyReported)
                return BadRequest("Already reported");

            var report = new Report
            {
                ReporterUserId = userId,
                VideoId = videoId,
                CommentId = commentId,
                Reason = reason,
                IsResolved = false
            };

            _db.Reports.Add(report);
            await _db.SaveChangesAsync();

            return Ok();
        }
    }
}
