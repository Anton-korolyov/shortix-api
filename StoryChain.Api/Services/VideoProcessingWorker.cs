using Microsoft.EntityFrameworkCore;
using StoryChain.Api.Data;
using StoryChain.Api.Models;

namespace StoryChain.Api.Services
{
    public class VideoProcessingWorker : BackgroundService
    {
        private readonly VideoJobQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;

        public VideoProcessingWorker(
            VideoJobQueue queue,
            IServiceScopeFactory scopeFactory)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var job = await _queue.Dequeue(stoppingToken);

                using var scope = _scopeFactory.CreateScope();

                var db = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                var analyzer = scope.ServiceProvider
                    .GetRequiredService<VideoAnalyzer>();

                var video = await db.Videos
                    .FirstOrDefaultAsync(x => x.Id == job.VideoId);

                if (video == null)
                    continue;

                try
                {
                    var duration =
                        await analyzer.GetDuration(job.VideoPath);

                    var (w, h) =
                        await analyzer.GetResolution(job.VideoPath);

                    var thumbPath =
                        await analyzer.GenerateThumbnail(job.VideoPath);

                    video.DurationSec = (int)Math.Round(duration);
                    video.ThumbnailUrl =
                        "/storage/" + Path.GetFileName(thumbPath);

                    video.Processing = false;
                    await db.SaveChangesAsync();
                }
                catch
                {
                    video.Processing = false;
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
