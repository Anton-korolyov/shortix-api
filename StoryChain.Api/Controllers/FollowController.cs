using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Api;
using StoryChain.Api.Data;
using StoryChain.Api.DTO;
using StoryChain.Api.Models;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/follow")]
    public class FollowController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public FollowController(
            AppDbContext db,
            IHubContext<NotificationHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // POST api/follow/{username}
        [Authorize]
        [HttpPost("{username}")]
        public async Task<IActionResult> Toggle(string username)
        {
            var myId = Guid.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value
            );

            var me = await _db.Users.FindAsync(myId);

            var target = await _db.Users
                .FirstOrDefaultAsync(x => x.Username == username);

            if (target == null)
                return NotFound();

            if (target.Id == myId)
                return BadRequest("You cannot follow yourself");

            var existing = await _db.Followers.FirstOrDefaultAsync(x =>
                x.FollowerUserId == myId &&
                x.FollowingUserId == target.Id
            );

            // ======================
            // UNFOLLOW
            // ======================
            if (existing != null)
            {
                _db.Followers.Remove(existing);
                await _db.SaveChangesAsync();

                return Ok(new { following = false });
            }

            // ======================
            // FOLLOW
            // ======================

            _db.Followers.Add(new Follower
            {
                FollowerUserId = myId,
                FollowingUserId = target.Id
            });

            // сохраняем уведомление
            var notification = new Notification
            {
                UserId = target.Id,
                Type = "follow",
                Message = $"{me!.Username} followed you"
            };

            _db.Notifications.Add(notification);

            await _db.SaveChangesAsync();

            // отправляем realtime
            await _hub.Clients
      .Group(target.Id.ToString())
      .SendAsync("ReceiveNotification", new
      {
          type = notification.Type,
          message = notification.Message,
          link = "/profile/" + me!.Username
      });

            return Ok(new { following = true });
        }

        // GET api/follow/{username}/count
        [HttpGet("{username}/count")]
        public async Task<IActionResult> Count(string username)
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(x => x.Username == username);

            if (user == null) return NotFound();

            var followers = await _db.Followers
                .CountAsync(x => x.FollowingUserId == user.Id);

            var following = await _db.Followers
                .CountAsync(x => x.FollowerUserId == user.Id);

            return Ok(new { followers, following });
        }

        // GET api/follow/{username}/is-following
        [Authorize]
        [HttpGet("{username}/is-following")]
        public async Task<IActionResult> IsFollowing(string username)
        {
            var myId = Guid.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value
            );

            var user = await _db.Users
                .FirstOrDefaultAsync(x => x.Username == username);

            if (user == null) return NotFound();

            var exists = await _db.Followers.AnyAsync(x =>
                x.FollowerUserId == myId &&
                x.FollowingUserId == user.Id
            );

            return Ok(new { following = exists });
        }

        // GET api/follow/{username}/followers
        [HttpGet("{username}/followers")]
        public async Task<IActionResult> GetFollowers(string username)
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(x => x.Username == username);

            if (user == null)
                return NotFound();

            var followers = await _db.Followers
                .Where(f => f.FollowingUserId == user.Id)
                .Select(f => new FollowUserDto
                {
                    Username = f.FollowerUser.Username,
                    AvatarUrl = f.FollowerUser.AvatarUrl
                })
                .ToListAsync();

            return Ok(followers);
        }

        // GET api/follow/{username}/following
        [HttpGet("{username}/following")]
        public async Task<IActionResult> GetFollowing(string username)
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(x => x.Username == username);

            if (user == null)
                return NotFound();

            var following = await _db.Followers
                .Where(f => f.FollowerUserId == user.Id)
                .Select(f => new FollowUserDto
                {
                    Username = f.FollowingUser.Username,
                    AvatarUrl = f.FollowingUser.AvatarUrl
                })
                .ToListAsync();

            return Ok(following);
        }
    }
}
