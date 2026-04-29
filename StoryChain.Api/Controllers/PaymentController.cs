using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryChain.Api.Data;
using StoryChain.Api.Models;
using StoryChain.Api.Services;

namespace StoryChain.Api.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly PayPalService _paypal;
        private readonly AppDbContext _db;

        public PaymentController(PayPalService paypal, AppDbContext db)
        {
            _paypal = paypal;
            _db = db;
        }

        // Создать платёж для рекламы
        [HttpPost("ad/{adId}")]
        public async Task<IActionResult> PayForAd(Guid adId)
        {
            var ad = await _db.Ads.FindAsync(adId);
            if (ad == null) return NotFound();

            // Считаем стоимость: например $1 за каждые 100 показов
            decimal amount = Math.Round(ad.Budget / 100m, 2);
            if (amount < 1) amount = 1;

            var (orderId, approvalUrl) = await _paypal.CreateOrderAsync(
                amount,
                $"Ad Campaign - {ad.Days} days",
                adId,
                "ad"
            );

            // Сохраняем orderId чтобы потом найти
            ad.PayPalOrderId = orderId;
            await _db.SaveChangesAsync();

            return Ok(new { orderId, approvalUrl });
        }

        // Создать платёж для буста видео
        [HttpPost("boost/{videoId}")]
        public async Task<IActionResult> PayForBoost(
            Guid videoId,
            [FromQuery] int budget,
            [FromQuery] int days)
        {
            decimal amount = Math.Round(budget / 100m, 2);
            if (amount < 1) amount = 1;

            // Сначала создаём boost с Active = false
            var userId = Guid.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var boost = new VideoBoost
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                UserId = userId,
                Budget = budget,
                Days = days,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(days),
                Active = false  // ← пока не оплачено!
            };

            _db.VideoBoosts.Add(boost);

            var (orderId, approvalUrl) = await _paypal.CreateOrderAsync(
                amount,
                $"Video Boost - {days} days",
                boost.Id,
                "boost"
            );

            boost.PayPalOrderId = orderId;
            await _db.SaveChangesAsync();

            return Ok(new { orderId, approvalUrl, boostId = boost.Id });
        }

        // PayPal редиректит сюда после оплаты
        [HttpGet("success")]
        public async Task<IActionResult> Success([FromQuery] string token)
        {
            // token = PayPal orderId
            var (success, referenceId) = await _paypal.CaptureOrderAsync(token);

            if (!success)
                return Redirect("yourapp://payment/failed");

            // referenceId = "ad:guid" или "boost:guid"
            var parts = referenceId.Split(':');
            var type = parts[0];
            var id = Guid.Parse(parts[1]);

            if (type == "ad")
            {
                var ad = await _db.Ads.FindAsync(id);
                if (ad != null)
                {
                    ad.Active = true;
                    ad.StartDate = DateTime.UtcNow;
                    ad.EndDate = DateTime.UtcNow.AddDays(ad.Days);
                    await _db.SaveChangesAsync();
                }
            }
            else if (type == "boost")
            {
                var boost = await _db.VideoBoosts.FindAsync(id);
                if (boost != null)
                {
                    boost.Active = true;
                    await _db.SaveChangesAsync();
                }
            }

            // Редирект обратно в React Native приложение
            return Redirect($"yourapp://payment/success?type={type}&id={id}");
        }

        [HttpGet("cancel")]
        public IActionResult Cancel()
        {
            return Redirect("yourapp://payment/cancel");
        }
    }

}
