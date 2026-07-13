using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace MyoTrack.Infrastructure.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";
    public string Endpoint { get; set; } = "http://localhost:9000";
    public string AccessKey { get; set; } = "myotrack";
    public string SecretKey { get; set; } = "dev-only-password";
    public string Bucket { get; set; } = "myotrack-media";
}

public interface IMediaStorage
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<byte[]> DownloadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public class MinioMediaStorage : IMediaStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private bool _bucketEnsured;

    public MinioMediaStorage(IOptions<StorageOptions> options)
    {
        _bucket = options.Value.Bucket;
        _client = new AmazonS3Client(
            options.Value.AccessKey,
            options.Value.SecretKey,
            new AmazonS3Config
            {
                ServiceURL = options.Value.Endpoint,
                ForcePathStyle = true, // exigido pelo MinIO
            });
    }

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        }, ct);
    }

    public async Task<byte[]> DownloadAsync(string key, CancellationToken ct = default)
    {
        using var response = await _client.GetObjectAsync(_bucket, key, ct);
        using var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _client.DeleteObjectAsync(_bucket, key, ct);

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketEnsured) return;
        try
        {
            await _client.PutBucketAsync(_bucket, ct);
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // já existe
        }
        _bucketEnsured = true;
    }
}
