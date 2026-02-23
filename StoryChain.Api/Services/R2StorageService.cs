using Amazon.S3;
using Amazon.S3.Model;

namespace StoryChain.Api.Services
{
    public class R2StorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly IConfiguration _config;

        public R2StorageService(IConfiguration config)
        {
            _config = config;

            var s3Config = new AmazonS3Config
            {
                ServiceURL = _config["R2:Endpoint"],
                ForcePathStyle = true
            };

            _s3 = new AmazonS3Client(
                _config["R2:AccessKey"],
                _config["R2:SecretKey"],
                s3Config
            );
        }

        public async Task<string> UploadAsync(IFormFile file, string key)
        {
            using var stream = file.OpenReadStream();

            var request = new PutObjectRequest
            {
                BucketName = _config["R2:Bucket"],
                Key = key,
                InputStream = stream,
                ContentType = file.ContentType
            };

            await _s3.PutObjectAsync(request);

            var baseUrl = _config["R2:PublicBaseUrl"];
            return $"{baseUrl}/{key}";
        }
    }
}
