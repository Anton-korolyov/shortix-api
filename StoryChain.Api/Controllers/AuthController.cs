using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.DTO;
using StoryChain.Api.Models;
using StoryChain.Api.Services;

namespace StoryChain.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly JwtService _jwt;
        private readonly IConfiguration _config;

        public AuthController(AppDbContext db, JwtService jwt, IConfiguration config)
        {
            _db = db;
            _jwt = jwt;
            _config = config;
        }

        // ================= REGISTER =================

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest req)
        {
            if (await _db.Users.AnyAsync(x => x.Email == req.Email))
                return BadRequest("User already exists");

            var user = new User
            {
                Email = req.Email,
                PasswordHash = Hash(req.Password),
                Username = await GenerateUsername(req.Email)
            };

            SetRefreshToken(user);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Ok(CreateAuthResponse(user));
        }

        // ================= LOGIN =================

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest req)
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(x => x.Email == req.Email);

            if (user == null || user.PasswordHash != Hash(req.Password))
                return Unauthorized();

            SetRefreshToken(user);
            await _db.SaveChangesAsync();

            return Ok(CreateAuthResponse(user));
        }

        // ================= REFRESH =================

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x =>
                x.RefreshToken == req.RefreshToken &&
                x.RefreshTokenExpiry > DateTime.UtcNow);

            if (user == null)
                return Unauthorized();

            SetRefreshToken(user);
            await _db.SaveChangesAsync();

            return Ok(CreateAuthResponse(user));
        }

        // ================= HELPERS =================

        private void SetRefreshToken(User user)
        {
            user.RefreshToken = _jwt.GenerateRefreshToken();
            user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(
                int.Parse(_config["Jwt:RefreshTokenDays"]!)
            );
        }

        private AuthResponse CreateAuthResponse(User user)
        {
            return new AuthResponse
            {
                AccessToken = _jwt.GenerateAccessToken(user),
                RefreshToken = user.RefreshToken!
            };
        }

        private static string Hash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }

        private async Task<string> GenerateUsername(string email)
        {
            var baseName = email.Split('@')[0]
                .ToLower()
                .Replace(".", "")
                .Replace("_", "")
                .Replace("-", "");

            var username = baseName;
            int i = 1;

            while (await _db.Users.AnyAsync(u => u.Username == username))
            {
                username = baseName + i;
                i++;
            }

            return username;
        }
    }
}
