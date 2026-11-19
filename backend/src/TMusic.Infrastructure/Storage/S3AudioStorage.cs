using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.RegularExpressions;

namespace TMusic.Infrastructure.Storage;

public sealed partial class S3AudioStorage : IAudioStorage, IDisposable
{
    private readonly IAmazonS3 client;
    private readonly ObjectStorageOptions options;

    [GeneratedRegex(@"\A[0-9a-f]{32}\.mp3\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SafeFilePattern();

    public S3AudioStorage(IOptions<ObjectStorageOptions> configured)
    {
        options = configured.Value;
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("https" or "http") ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            (endpoint.Scheme == "http" && !endpoint.IsLoopback) ||
            string.IsNullOrWhiteSpace(options.Bucket) ||
            string.IsNullOrWhiteSpace(options.AccessKey) ||
            string.IsNullOrWhiteSpace(options.SecretKey) || !ValidPrefix(options.Prefix))
            throw new InvalidOperationException("Storage:Object requires endpoint, bucket and credentials when Storage:Provider is s3.");

        client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint.ToString().TrimEnd('/'),
                ForcePathStyle = options.PathStyle,
                AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "auto" : options.Region,
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120))
            });
    }

    public string StorageRootPath => $"s3://{options.Bucket}/{Prefix}";
    public bool IsObjectStorage => true;
    public void Dispose() => client.Dispose();
    private string Prefix => options.Prefix.Trim('/');

    private static bool ValidPrefix(string prefix)
        => prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Length > 0 &&
           prefix.Split('/', StringSplitOptions.RemoveEmptyEntries)
               .All(segment => segment is not ("." or "..") &&
                   segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));

    public bool IsValidMp3Header(ReadOnlySpan<byte> header) => AudioStorage.IsValidMp3HeaderBytes(header);

    public async Task<StoredAudio> SaveFromTempFileAsync(Guid userId, string tempFilePath, CancellationToken ct)
    {
        var info = new FileInfo(tempFilePath);
        if (!info.Exists || info.Length is <= 16 or > AudioStorage.MaxAudioBytes)
            throw new AudioStorageException("AUDIO_FILE_INVALID", "File MP3 trích xuất không hợp lệ hoặc vượt quá 100 MB.");
        await ValidateHeaderAsync(tempFilePath, ct);

        var fileName = $"{Guid.NewGuid():N}.mp3";
        try
        {
            await using var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, useAsync: true);
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = Key(userId, fileName),
                InputStream = stream,
                ContentType = "audio/mpeg",
                AutoCloseStream = false,
                UseChunkEncoding = false
            }, ct);
            return new StoredAudio(fileName, info.Length);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AudioStorageException("AUDIO_STORAGE_UNAVAILABLE", "Object storage timeout.") { Retryable = true };
        }
        catch (AmazonServiceException error)
        {
            throw StorageFailure(error);
        }
        catch (AmazonClientException)
        {
            throw new AudioStorageException("AUDIO_STORAGE_UNAVAILABLE", "Object storage is unavailable.") { Retryable = true };
        }
    }

    public async Task<StoredAudio> SaveBytesAsync(Guid userId, ReadOnlyMemory<byte> mp3Bytes, CancellationToken ct)
    {
        if (mp3Bytes.Length is <= 16 or > (int)AudioStorage.MaxAudioBytes)
            throw new AudioStorageException("AUDIO_FILE_INVALID", "File MP3 không hợp lệ hoặc vượt quá 100 MB.");
        if (!IsValidMp3Header(mp3Bytes.Span))
            throw new AudioStorageException("AUDIO_FORMAT_INVALID", "Định dạng âm thanh không phải MP3 hợp lệ.");

        var fileName = $"{Guid.NewGuid():N}.mp3";
        try
        {
            await using var stream = new MemoryStream(mp3Bytes.ToArray(), writable: false);
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = Key(userId, fileName),
                InputStream = stream,
                ContentType = "audio/mpeg",
                AutoCloseStream = false,
                UseChunkEncoding = false
            }, ct);
            return new StoredAudio(fileName, mp3Bytes.Length);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AudioStorageException("AUDIO_STORAGE_UNAVAILABLE", "Object storage timeout.") { Retryable = true };
        }
        catch (AmazonServiceException error)
        {
            throw StorageFailure(error);
        }
        catch (AmazonClientException)
        {
            throw new AudioStorageException("AUDIO_STORAGE_UNAVAILABLE", "Object storage is unavailable.") { Retryable = true };
        }
    }

    public async Task<(Stream Stream, long Length)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct)
    {
        if (!SafeFilePattern().IsMatch(fileName)) return null;
        try
        {
            var key = Key(userId, fileName);
            var metadata = await client.GetObjectMetadataAsync(options.Bucket, key, ct);
            return (new RangedObjectStream(client, options.Bucket!, key, metadata.ContentLength), metadata.ContentLength);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonServiceException error)
        {
            throw StorageFailure(error);
        }
        catch (AmazonClientException)
        {
            throw new AudioStorageException("AUDIO_STORAGE_UNAVAILABLE", "Object storage is unavailable.") { Retryable = true };
        }
    }

    public void DeleteAudioFile(Guid userId, string? fileName)
    {
        if (fileName is null || !SafeFilePattern().IsMatch(fileName)) return;
        try { client.DeleteObjectAsync(options.Bucket, Key(userId, fileName)).GetAwaiter().GetResult(); }
        catch (AmazonServiceException) { }
        catch (AmazonClientException) { }
    }

    public void DeleteUserAudio(Guid userId)
    {
        try
        {
            foreach (var file in ListUserFilesAsync(userId, CancellationToken.None).GetAwaiter().GetResult())
                client.DeleteObjectAsync(options.Bucket, Key(userId, file.FileName)).GetAwaiter().GetResult();
        }
        catch (AmazonServiceException) { }
        catch (AmazonClientException) { }
    }

    public IReadOnlyList<StoredFileEntry> ListAllFiles()
        => ListAllFilesAsync(CancellationToken.None).GetAwaiter().GetResult();

    public bool QuarantineFile(Guid userId, string fileName)
    {
        if (!SafeFilePattern().IsMatch(fileName)) return false;
        var sourceKey = Key(userId, fileName);
        var quarantineKey = $"{Prefix}/_quarantine/{userId:N}/{fileName}";
        try
        {
            using var response = client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = options.Bucket,
                Key = sourceKey
            }).GetAwaiter().GetResult();
            using var stream = response.ResponseStream;
            client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = quarantineKey,
                InputStream = stream,
                ContentType = "audio/mpeg",
                AutoCloseStream = false,
                UseChunkEncoding = false
            }).GetAwaiter().GetResult();
            client.DeleteObjectAsync(options.Bucket, sourceKey).GetAwaiter().GetResult();
            return true;
        }
        catch (AmazonServiceException) { return false; }
        catch (AmazonClientException) { return false; }
    }

    private async Task<IReadOnlyList<StoredFileEntry>> ListAllFilesAsync(CancellationToken ct)
    {
        var result = new List<StoredFileEntry>();
        string? token = null;
        do
        {
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = options.Bucket,
                Prefix = Prefix + "/",
                ContinuationToken = token
            }, ct);
            foreach (var item in response.S3Objects)
            {
                if (TryParseFile(item.Key, out var userId, out var fileName))
                    result.Add(new StoredFileEntry(userId, fileName, item.Size ?? 0,
                        new DateTimeOffset(DateTime.SpecifyKind(item.LastModified ?? DateTime.UnixEpoch, DateTimeKind.Utc))));
            }
            token = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (token is not null);
        return result;
    }

    private async Task<IReadOnlyList<StoredFileEntry>> ListUserFilesAsync(Guid userId, CancellationToken ct)
        => (await ListAllFilesAsync(ct)).Where(x => x.UserId == userId).ToArray();

    private string Key(Guid userId, string fileName) => $"{Prefix}/{userId:N}/{fileName}";

    private bool TryParseFile(string key, out Guid userId, out string fileName)
    {
        userId = default;
        fileName = string.Empty;
        var prefix = Prefix + "/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var parts = key[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out userId) || !SafeFilePattern().IsMatch(parts[1])) return false;
        fileName = parts[1];
        return true;
    }

    private static async Task ValidateHeaderAsync(string path, CancellationToken ct)
    {
        var header = new byte[16];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var read = await stream.ReadAsync(header.AsMemory(), ct);
        if (!AudioStorage.IsValidMp3HeaderBytes(header.AsSpan(0, read)))
            throw new AudioStorageException("AUDIO_FORMAT_INVALID", "Định dạng âm thanh không phải MP3 hợp lệ.");
    }

    private static AudioStorageException StorageFailure(AmazonServiceException error)
    {
        var retryable = error.StatusCode == HttpStatusCode.TooManyRequests ||
            (int)error.StatusCode >= 500 || error.StatusCode == HttpStatusCode.RequestTimeout || error.StatusCode == 0;
        return new AudioStorageException(retryable ? "AUDIO_STORAGE_UNAVAILABLE" : "AUDIO_STORAGE_REJECTED",
            "Object storage rejected the audio operation.") { Retryable = retryable };
    }

    private sealed class RangedObjectStream(IAmazonS3 client, string bucket, string key, long length) : Stream
    {
        private const int ReadAheadBytes = 1024 * 1024;
        private byte[] cached = [];
        private long cachedStart = -1;
        private int cachedLength;
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override int Read(Span<byte> buffer)
        {
            var scratch = new byte[buffer.Length];
            var read = Read(scratch, 0, scratch.Length);
            scratch.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => await ReadAsync(buffer.AsMemory(offset, count), ct);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (position >= length || buffer.Length == 0) return 0;
            if (cachedStart < 0 || position < cachedStart || position >= cachedStart + cachedLength)
                await LoadRangeAsync(ct);
            var offset = checked((int)(position - cachedStart));
            var count = Math.Min(buffer.Length, cachedLength - offset);
            cached.AsMemory(offset, count).CopyTo(buffer);
            position += count;
            return count;
        }

        private async Task LoadRangeAsync(CancellationToken ct)
        {
            cachedStart = position;
            cached = new byte[(int)Math.Min(ReadAheadBytes, length - position)];
            var lastByte = position + cached.Length - 1;
            try
            {
                using var response = await client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    ByteRange = new ByteRange(position, lastByte)
                }, ct);
                cachedLength = await response.ResponseStream.ReadAsync(cached, ct);
                if (cachedLength == 0) throw new IOException("Object storage returned an empty range.");
            }
        catch (AmazonServiceException error)
            {
                throw StorageFailure(error);
            }
        catch (AmazonClientException)
        {
            throw new AudioStorageException("AUDIO_STORAGE_UNAVAILABLE", "Object storage is unavailable.") { Retryable = true };
        }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0 || target > length) throw new IOException("Seek position is outside the object.");
            position = target;
            return position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
