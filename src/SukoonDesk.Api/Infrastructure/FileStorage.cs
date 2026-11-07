using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace SukoonDesk.Api.Infrastructure;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "Local";
    public string LocalPath { get; set; } = "App_Data/uploads";
    public string ServiceUrl { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
}

public sealed record StoredObject(byte[] Content, string ContentType);

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<StoredObject?> ReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

public sealed class LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(Path.IsPathRooted(options.Value.LocalPath)
        ? options.Value.LocalPath
        : Path.Combine(environment.ContentRootPath, options.Value.LocalPath));

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = SafePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = File.Create(path);
        await content.CopyToAsync(output, cancellationToken);
    }

    public async Task<StoredObject?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = SafePath(key);
        if (!File.Exists(path)) return null;
        return new StoredObject(await File.ReadAllBytesAsync(path, cancellationToken), "application/octet-stream");
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = SafePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var path = SafePath(prefix);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        return Task.CompletedTask;
    }

    private string SafePath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, _root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage key.");
        return path;
    }
}

public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly AmazonS3Client _client;

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.Bucket))
            throw new InvalidOperationException("S3 storage requires ServiceUrl and Bucket.");
        _client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,
            ForcePathStyle = true
        });
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = _options.Bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false }, cancellationToken);

    public async Task<StoredObject?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_options.Bucket, key, cancellationToken);
            await using var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, cancellationToken);
            return new StoredObject(memory.ToArray(), response.Headers.ContentType ?? "application/octet-stream");
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await _client.DeleteObjectAsync(_options.Bucket, key, cancellationToken);

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        string? token = null;
        do
        {
            var page = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix, ContinuationToken = token }, cancellationToken);
            if (page.S3Objects.Count > 0)
                await _client.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = _options.Bucket, Objects = page.S3Objects.Select(x => new KeyVersion { Key = x.Key }).ToList() }, cancellationToken);
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (token is not null);
    }

    public void Dispose() => _client.Dispose();
}

public static class StorageKeys
{
    public static string Complaint(Guid tenantId, Guid complaintId, string storedName) => $"{tenantId:N}/{complaintId:N}/{storedName}";
    public static string ComplaintPrefix(Guid tenantId, Guid complaintId) => $"{tenantId:N}/{complaintId:N}/";
}
