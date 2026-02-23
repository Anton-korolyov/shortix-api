using System.Diagnostics;
using System.Text.Json;

namespace StoryChain.Api.Services
{
   
    public class VideoAnalyzer
    {
        public async Task<double> GetDuration(string filePath)
        {
            var json = await RunFfprobe(
                $"-v error -show_entries format=duration -of json \"{filePath}\""
            );

            using var doc = JsonDocument.Parse(json);

            return double.Parse(
                doc.RootElement
                    .GetProperty("format")
                    .GetProperty("duration")
                    .GetString()!,
                System.Globalization.CultureInfo.InvariantCulture
            );
        }

        public async Task<(int width, int height)> GetResolution(string filePath)
        {
            var json = await RunFfprobe(
                "-v error -select_streams v:0 -show_entries stream=width,height -of json " +
                $"\"{filePath}\""
            );

            using var doc = JsonDocument.Parse(json);

            var stream = doc.RootElement
                .GetProperty("streams")[0];

            return (
                stream.GetProperty("width").GetInt32(),
                stream.GetProperty("height").GetInt32()
            );
        }

        private async Task<string> RunFfprobe(string args)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            return output;
        }

        public async Task<string> GenerateThumbnail(string videoPath)
        {
            var thumbnailPath = Path.ChangeExtension(videoPath, ".jpg");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments =
                        $"-y -i \"{videoPath}\" -ss 00:00:01 -vframes 1 \"{thumbnailPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return thumbnailPath;
        }
    }

}
