using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace MyoTrack.Infrastructure.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";
    public string Endpoint { get; set; } = "http://localhost:9000";

    /// <summary>
    /// Endpoint que o browser alcança para URLs pré-assinadas (ex.: https://dominio/media).
    /// Vazio = usa <see cref="Endpoint"/> (suficiente em desenvolvimento local).
    /// </summary>
    public string PublicEndpoint { get; set; } = "";

    public string AccessKey { get; set; } = "myotrack";
    public string SecretKey { get; set; } = "dev-only-password";
    public string Bucket { get; set; } = "myotrack-media";
}

public record StoredObjectInfo(long SizeBytes, string ContentType);

public interface IMediaStorage
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<byte[]> DownloadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>URL pré-assinada de PUT para o browser subir a mídia direto no storage.</summary>
    Task<string> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>URL pré-assinada de GET para o browser baixar/reproduzir a mídia.</summary>
    Task<string> GetPresignedDownloadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>Metadados do objeto, ou null se não existir.</summary>
    Task<StoredObjectInfo?> GetObjectInfoAsync(string key, CancellationToken ct = default);
}

public class MinioMediaStorage : IMediaStorage
{
    private readonly IAmazonS3 _client;
    private readonly IAmazonS3 _presignClient;
    private readonly bool _presignOverHttp;
    private readonly string _bucket;
    private bool _bucketEnsured;

    public MinioMediaStorage(IOptions<StorageOptions> options)
    {
        _bucket = options.Value.Bucket;
        _client = CreateClient(options.Value, options.Value.Endpoint);

        // URLs pré-assinadas são válidas apenas para o host com que foram assinadas,
        // então o cliente de presign usa o endpoint que o browser alcança.
        var publicEndpoint = string.IsNullOrWhiteSpace(options.Value.PublicEndpoint)
            ? options.Value.Endpoint
            : options.Value.PublicEndpoint;
        _presignClient = publicEndpoint == options.Value.Endpoint
            ? _client
            : CreateClient(options.Value, publicEndpoint);
        _presignOverHttp = publicEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    private static AmazonS3Client CreateClient(StorageOptions options, string endpoint) =>
        new(options.AccessKey, options.SecretKey, new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true, // exigido pelo MinIO
        });

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

    public async Task<string> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);
        return await _presignClient.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = _presignOverHttp ? Protocol.HTTP : Protocol.HTTPS,
        });
    }

    public Task<string> GetPresignedDownloadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
        _presignClient.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = _presignOverHttp ? Protocol.HTTP : Protocol.HTTPS,
        });

    public async Task<StoredObjectInfo?> GetObjectInfoAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var metadata = await _client.GetObjectMetadataAsync(_bucket, key, ct);
            return new StoredObjectInfo(metadata.ContentLength, metadata.Headers.ContentType ?? "");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

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
