using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using StoryChain.Api.Api;
using StoryChain.Api.Data;
using StoryChain.Api.Models;
using StoryChain.Api.Services;

var builder = WebApplication.CreateBuilder(args);

/////////////////////////////////////////////////////
// CORS
/////////////////////////////////////////////////////

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(origin => true);
    });
});

/////////////////////////////////////////////////////
// DATABASE
/////////////////////////////////////////////////////

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

/////////////////////////////////////////////////////
// REDIS
/////////////////////////////////////////////////////

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConnection =
        builder.Configuration.GetConnectionString("Redis") ??
        "tredo-redis:6379";

    return ConnectionMultiplexer.Connect(redisConnection);
});

/////////////////////////////////////////////////////
// CONTROLLERS
/////////////////////////////////////////////////////

builder.Services.AddControllers();

/////////////////////////////////////////////////////
// SWAGGER + JWT
/////////////////////////////////////////////////////

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient<PayPalService>();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Bearer {token}"
        });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

/////////////////////////////////////////////////////
// SERVICES
/////////////////////////////////////////////////////

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<VideoAnalyzer>();

builder.Services.AddSingleton<VideoJobQueue>();
builder.Services.AddHostedService<VideoProcessingWorker>();

builder.Services.AddSingleton<R2VideoService>();

/////////////////////////////////////////////////////
// SIGNALR
/////////////////////////////////////////////////////

builder.Services.AddSignalR();

/////////////////////////////////////////////////////
// JWT AUTH
/////////////////////////////////////////////////////

builder.Services
.AddAuthentication("Bearer")
.AddJwtBearer("Bearer", options =>
{
    options.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
        )
    };

    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs/notifications"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

/////////////////////////////////////////////////////
// UPLOAD LIMIT
/////////////////////////////////////////////////////

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB
});

/////////////////////////////////////////////////////
// RATE LIMITER (video upload)
/////////////////////////////////////////////////////

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("VideoUploadPolicy", context =>
    {
        var userId =
            context.User
                .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?.Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId ??
                context.Connection.RemoteIpAddress!.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

/////////////////////////////////////////////////////
// BUILD
/////////////////////////////////////////////////////

var app = builder.Build();

/////////////////////////////////////////////////////
// AUTO MIGRATION
/////////////////////////////////////////////////////

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

/////////////////////////////////////////////////////
// MIDDLEWARE
/////////////////////////////////////////////////////

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

/////////////////////////////////////////////////////
// ENDPOINTS
/////////////////////////////////////////////////////

app.MapHub<NotificationHub>("/hubs/notifications");

app.MapControllers();

/////////////////////////////////////////////////////

app.Run();