using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace RealEstateApi.Infrastructure.Services;

public class R2ImageService : IImageStorage, IDisposable
{
    private readonly R2Options _options;
    private readonly Lazy<AmazonS3Client> _s3Client;

    public R2ImageService(IOptions<R2Options> options)
    {
        _options = options.Value;
        _s3Client = new Lazy<AmazonS3Client>(CreateClient);
    }

    private AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true
        };
        return new AmazonS3Client(_options.AccessKeyId, _options.SecretAccessKey, config);
    }

    public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = fileName,
            InputStream = fileStream,
            ContentType = contentType,
            AutoCloseStream = false
        };

        var response = await _s3Client.Value.PutObjectAsync(request, cancellationToken);
        if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            throw new InvalidOperationException($"R2 upload failed with status {response.HttpStatusCode}");

        return $"{_options.PublicUrl.TrimEnd('/')}/{fileName}";
    }

    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = fileName
        };

        var response = await _s3Client.Value.DeleteObjectAsync(request, cancellationToken);
        if (response.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
            throw new InvalidOperationException($"R2 delete failed with status {response.HttpStatusCode}");
    }

    public void Dispose()
    {
        if (_s3Client.IsValueCreated)
            _s3Client.Value.Dispose();
    }
}
