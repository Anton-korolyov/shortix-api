using Amazon.S3;
using Amazon.S3.Model;

namespace StoryChain.Api.Services
{
    public class R2VideoService
    {
        private readonly AmazonS3Client _client;
        private readonly string _bucket;
        private readonly string _publicUrl;

        public R2VideoService(IConfiguration config)
        {
            var endpoint = config["R2:Endpoint"]
                ?? throw new Exception("R2:Endpoint not configured");

            var accessKey = config["R2:AccessKey"]
                ?? throw new Exception("R2:AccessKey not configured");

            var secretKey = config["R2:SecretKey"]
                ?? throw new Exception("R2:SecretKey not configured");

            _bucket = config["R2:Bucket"]
                ?? throw new Exception("R2:Bucket not configured");

            _publicUrl = config["R2:PublicUrl"]
                ?? throw new Exception("R2:PublicUrl not configured");

            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                SignatureVersion = "4"
            };

            _client = new AmazonS3Client(accessKey, secretKey, s3Config);
        }

        // ================= UPLOAD VIDEO =================

        public async Task UploadVideoAsync(string fileName, Stream stream, string contentType)
        {
            if (stream == null || stream.Length == 0)
                throw new Exception("Empty stream");

            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = fileName,
                InputStream = stream,

                // ВАЖНО: video/mp4, video/webm, video/quicktime
                ContentType = contentType,

                // 🔥 Cloudflare R2 FIX
                DisablePayloadSigning = true,
                UseChunkEncoding = false
            };

            await _client.PutObjectAsync(request);
        }

        // ================= PUBLIC URL =================

        public string GetPublicUrl(string fileName)
        {
            var encoded = Uri.EscapeDataString(fileName);
            return $"{_publicUrl}/{encoded}";
        }
    }
}
