using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace TMusic.Infrastructure.Storage;

public sealed record StoredAudio(string FileName, long SizeBytes);

public interface IAudioStorage
{
    bool IsValidMp3Header(ReadOnlySpan<byte> header);
    Task<StoredAudio> SaveFromTempFileAsync(Guid userId, string tempFilePath, CancellationToken ct);
    Task<StoredAudio> SaveBytesAsync(Guid userId, ReadOnlyMemory<byte> mp3Bytes, CancellationToken ct);
    Task<(Stream Stream, long Length)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct);
    void DeleteAudioFile(Guid userId, string? fileName);
    void DeleteUserAudio(Guid userId);
}

public sealed partial class AudioStorage(IConfiguration config) : IAudioStorage
{
    public const long MaxAudioBytes = 100L * 1024 * 1024;

    [GeneratedRegex(@"\A[0-9a-f]{32}\.mp3\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SafeFilePattern();

    private string RootPath => config["Storage:AudioPath"] is { Length: > 0 } configured
        ? Path.GetFullPath(configured)
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TMusicV2",
            "audio");

    public bool IsValidMp3Header(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4) return false;
        // ID3v2 tag ("ID3")
        if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) return true;
        // Raw MPEG audio frame sync (11 bits all 1s, valid MPEG version & layer != 0)
        return header[0] == 0xFF && (header[1] & 0xE0) == 0xE0 && (header[1] & 0x18) != 0x08 && (header[1] & 0x06) != 0x00;
    }

    public async Task<StoredAudio> SaveFromTempFileAsync(Guid userId, string tempFilePath, CancellationToken ct)
    {
        var info = new FileInfo(tempFilePath);
        if (!info.Exists || info.Length is <= 16 or > MaxAudioBytes)
            throw new AudioStorageException("AUDIO_FILE_INVALID", "File MP3 trích xuất không hợp lệ hoặc vượt quá 100 MB.");

        var header = new byte[16];
        await using (var fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            var read = await fs.ReadAsync(header.AsMemory(0, header.Length), ct);
            if (!IsValidMp3Header(header.AsSpan(0, read)))
                throw new AudioStorageException("AUDIO_FORMAT_INVALID", "Định dạng âm thanh không phải MP3 hợp lệ.");
        }

        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        Directory.CreateDirectory(userDir);

        var fileName = $"{Guid.NewGuid():N}.mp3";
        var destPath = Path.GetFullPath(Path.Combine(userDir, fileName));
        if (!destPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new AudioStorageException("AUDIO_PATH_INVALID", "Đường dẫn lưu trữ không hợp lệ.");

        File.Copy(tempFilePath, destPath, overwrite: false);
        var savedInfo = new FileInfo(destPath);
        return new StoredAudio(fileName, savedInfo.Length);
    }

    public async Task<StoredAudio> SaveBytesAsync(Guid userId, ReadOnlyMemory<byte> mp3Bytes, CancellationToken ct)
    {
        if (mp3Bytes.Length is <= 16 or > (int)MaxAudioBytes)
            throw new AudioStorageException("AUDIO_FILE_INVALID", "File MP3 không hợp lệ hoặc vượt quá 100 MB.");
        if (!IsValidMp3Header(mp3Bytes.Span))
            throw new AudioStorageException("AUDIO_FORMAT_INVALID", "Định dạng âm thanh không phải MP3 hợp lệ.");

        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        Directory.CreateDirectory(userDir);

        var fileName = $"{Guid.NewGuid():N}.mp3";
        var destPath = Path.GetFullPath(Path.Combine(userDir, fileName));
        if (!destPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new AudioStorageException("AUDIO_PATH_INVALID", "Đường dẫn lưu trữ không hợp lệ.");

        await File.WriteAllBytesAsync(destPath, mp3Bytes.ToArray(), ct);
        return new StoredAudio(fileName, mp3Bytes.Length);
    }

    public Task<(Stream Stream, long Length)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!SafeFilePattern().IsMatch(fileName))
            return Task.FromResult<(Stream, long)?>(null);

        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        var fullPath = Path.GetFullPath(Path.Combine(userDir, fileName));
        if (!fullPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return Task.FromResult<(Stream, long)?>(null);

        var info = new FileInfo(fullPath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, useAsync: true);
        return Task.FromResult<(Stream, long)?>((stream, info.Length));
    }

    public void DeleteAudioFile(Guid userId, string? fileName)
    {
        if (fileName is null || !SafeFilePattern().IsMatch(fileName)) return;
        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        var fullPath = Path.GetFullPath(Path.Combine(userDir, fileName));
        if (fullPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch (IOException) { }
        }
    }

    public void DeleteUserAudio(Guid userId)
    {
        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        if (Directory.Exists(userDir))
        {
            try { Directory.Delete(userDir, recursive: true); } catch (IOException) { }
        }
    }
}

public sealed class AudioStorageException(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
    public string Detail { get; } = detail;
}
