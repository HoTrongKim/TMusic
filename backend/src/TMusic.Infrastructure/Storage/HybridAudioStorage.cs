namespace TMusic.Infrastructure.Storage;

public sealed class HybridAudioStorage(AudioStorage local, S3AudioStorage objects) : IAudioStorage
{
    public string StorageRootPath => objects.StorageRootPath;
    public bool IsObjectStorage => true;
    public bool IsValidMp3Header(ReadOnlySpan<byte> header) => local.IsValidMp3Header(header);

    public Task<StoredAudio> SaveFromTempFileAsync(Guid userId, string tempFilePath, CancellationToken ct)
        => objects.SaveFromTempFileAsync(userId, tempFilePath, ct);

    public Task<StoredAudio> SaveBytesAsync(Guid userId, ReadOnlyMemory<byte> mp3Bytes, CancellationToken ct)
        => objects.SaveBytesAsync(userId, mp3Bytes, ct);

    public async Task<(Stream Stream, long Length)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct)
        => await objects.OpenReadAsync(userId, fileName, ct) ?? await local.OpenReadAsync(userId, fileName, ct);

    public void DeleteAudioFile(Guid userId, string? fileName)
    {
        objects.DeleteAudioFile(userId, fileName);
        local.DeleteAudioFile(userId, fileName);
    }

    public void DeleteUserAudio(Guid userId)
    {
        objects.DeleteUserAudio(userId);
        local.DeleteUserAudio(userId);
    }

    public IReadOnlyList<StoredFileEntry> ListAllFiles()
        => objects.ListAllFiles().Concat(local.ListAllFiles())
            .GroupBy(file => (file.UserId, file.FileName))
            .Select(group => group.OrderByDescending(file => file.LastModifiedUtc).First())
            .ToArray();

    public bool QuarantineFile(Guid userId, string fileName)
        => objects.QuarantineFile(userId, fileName) || local.QuarantineFile(userId, fileName);
}
