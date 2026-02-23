using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
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
// DB
/////////////////////////////////////////////////////
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

/////////////////////////////////////////////////////
// Controllers
/////////////////////////////////////////////////////
builder.Services.AddControllers();

/////////////////////////////////////////////////////
// Swagger + JWT support
/////////////////////////////////////////////////////
builder.Services.AddEndpointsApiExplorer();
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
// Services
/////////////////////////////////////////////////////
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<VideoAnalyzer>();
builder.Services.AddSingleton<VideoJobQueue>();
builder.Services.AddHostedService<VideoProcessingWorker>();
builder.Services.AddSingleton<R2StorageService>();

/////////////////////////////////////////////////////
// SignalR
/////////////////////////////////////////////////////
builder.Services.AddSignalR();

/////////////////////////////////////////////////////
// JWT Auth
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

    // 🔥 JWT FOR SIGNALR
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
// Upload limit
/////////////////////////////////////////////////////
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB
});

/////////////////////////////////////////////////////
// Rate Limiter (upload videos)
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



using (var scope = app.Services.CreateScope()) 
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

      db.Database.Migrate();
}
/////////////////////////////////////////////////////
// STORAGE FOLDER
/////////////////////////////////////////////////////
var storagePath =
    Path.Combine(Directory.GetCurrentDirectory(), "Storage");

Directory.CreateDirectory(storagePath);

/////////////////////////////////////////////////////
// MIDDLEWARE PIPELINE
/////////////////////////////////////////////////////

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storagePath),
    RequestPath = "/storage"
});

app.UseRouting();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

/////////////////////////////////////////////////////
// MAPS
/////////////////////////////////////////////////////

app.MapHub<NotificationHub>("/hubs/notifications");
app.MapControllers();

/////////////////////////////////////////////////////
app.Run();
/////////////////////////////////////////////////////
