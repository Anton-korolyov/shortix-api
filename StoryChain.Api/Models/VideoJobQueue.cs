using System.Threading.Channels;

namespace StoryChain.Api.Models
{
    public class VideoJobQueue
    {
        private readonly Channel<VideoJob> _queue =
            Channel.CreateUnbounded<VideoJob>();

        public async Task Enqueue(VideoJob job)
        {
            await _queue.Writer.WriteAsync(job);
        }

        public async Task<VideoJob> Dequeue(CancellationToken token)
        {
            return await _queue.Reader.ReadAsync(token);
        }
    }
}
