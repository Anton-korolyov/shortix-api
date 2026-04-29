using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StoryChain.Api.Services
{
    public class PayPalService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _baseUrl;

        public PayPalService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
            _clientId = config["PayPal:ClientId"]!;
            _clientSecret = config["PayPal:ClientSecret"]!;

            // Sandbox для теста, для прода: https://api-m.paypal.com
            _baseUrl = "https://api-m.sandbox.paypal.com";
        }

        // ========== Шаг 1: Получить токен ==========
        private async Task<string> GetAccessTokenAsync()
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

            var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            request.Content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            return doc.RootElement.GetProperty("access_token").GetString()!;
        }

        // ========== Шаг 2: Создать заказ ==========
        public async Task<(string orderId, string approvalUrl)> CreateOrderAsync(
            decimal amount,
            string description,
            Guid internalId,
            string type)          // "ad" или "boost"
        {
            var token = await GetAccessTokenAsync();
            var appBaseUrl = _config["App:BaseUrl"];

            var body = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                new
                {
                    reference_id = $"{type}:{internalId}",
                    description = description,
                    amount = new
                    {
                        currency_code = "USD",
                        value = amount.ToString("F2")
                    }
                }
            },
                application_context = new
                {
                    brand_name = "StoryChain",
                    landing_page = "BILLING",        // Сразу форма карты!
                    user_action = "PAY_NOW",
                    shipping_preference = "NO_SHIPPING",
                    return_url = $"{appBaseUrl}/api/payments/success",
                    cancel_url = $"{appBaseUrl}/api/payments/cancel"
                }
            };

            var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_baseUrl}/v2/checkout/orders");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var orderId = doc.RootElement.GetProperty("id").GetString()!;

            // Находим approve ссылку
            var approvalUrl = doc.RootElement
                .GetProperty("links")
                .EnumerateArray()
                .First(l => l.GetProperty("rel").GetString() == "approve")
                .GetProperty("href")
                .GetString()!;

            return (orderId, approvalUrl);
        }

        // ========== Шаг 3: Capture после оплаты ==========
        public async Task<(bool success, string? referenceId)> CaptureOrderAsync(
            string orderId)
        {
            var token = await GetAccessTokenAsync();

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_baseUrl}/v2/checkout/orders/{orderId}/capture");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            request.Content = new StringContent(
                "{}", Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var status = doc.RootElement.GetProperty("status").GetString();

            var referenceId = doc.RootElement
                .GetProperty("purchase_units")[0]
                .GetProperty("reference_id")
                .GetString();

            return (status == "COMPLETED", referenceId);
        }
    }
}
