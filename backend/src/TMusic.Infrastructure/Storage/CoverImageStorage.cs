using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace TMusic.Infrastructure.Storage;

public sealed record SanitizedCover(byte[] Bytes, string Extension, string ContentType, int Width, int Height);

public interface ICoverImageStorage
{
    SanitizedCover InspectAndSanitize(ReadOnlySpan<byte> input);
    Task<string> SaveAsync(Guid userId, ReadOnlyMemory<byte> rawBytes, CancellationToken ct);
    Task<(Stream Stream, string ContentType)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct);
    void DeleteCoverUrl(Guid userId, string? coverImageUrl);
    void DeleteUserCovers(Guid userId);
}

public sealed partial class CoverImageStorage(IConfiguration config) : ICoverImageStorage
{
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxDimension = 4096;

    [GeneratedRegex(@"\A[0-9a-f]{32}\.(png|jpg|webp)\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SafeFilePattern();

    private string RootPath => config["Storage:CoversPath"] is { Length: > 0 } configured
        ? Path.GetFullPath(configured)
        : Path.Combine(AppContext.BaseDirectory, "App_Data", "covers");

    public static string? ExtractOwnedFileName(Guid userId, string? coverImageUrl)
    {
        var prefix = $"/api/v1/covers/{userId:D}/";
        if (coverImageUrl is null || !coverImageUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var file = coverImageUrl[prefix.Length..];
        return SafeFilePattern().IsMatch(file) ? file : null;
    }

    public async Task<string> SaveAsync(Guid userId, ReadOnlyMemory<byte> rawBytes, CancellationToken ct)
    {
        if (rawBytes.Length is 0 or > MaxFileBytes)
            throw new CoverValidationException("COVER_SIZE_INVALID", "Ảnh bìa phải lớn hơn 0 byte và không vượt quá 2 MB.");

        var sanitized = InspectAndSanitize(rawBytes.Span);
        var userDir = Path.Combine(RootPath, userId.ToString("N"));
        Directory.CreateDirectory(userDir);

        var fileName = $"{Guid.NewGuid():N}.{sanitized.Extension}";
        var fullPath = Path.Combine(userDir, fileName);
        await File.WriteAllBytesAsync(fullPath, sanitized.Bytes, ct);
        return $"/api/v1/covers/{userId:D}/{fileName}";
    }

    public Task<(Stream Stream, string ContentType)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!SafeFilePattern().IsMatch(fileName))
            return Task.FromResult<(Stream, string)?>(null);

        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        var fullPath = Path.GetFullPath(Path.Combine(userDir, fileName));
        if (!fullPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return Task.FromResult<(Stream, string)?>(null);

        var contentType = Path.GetExtension(fileName) switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, useAsync: true);
        return Task.FromResult<(Stream, string)?>((stream, contentType));
    }

    public void DeleteCoverUrl(Guid userId, string? coverImageUrl)
    {
        if (ExtractOwnedFileName(userId, coverImageUrl) is not { } fileName) return;
        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        var fullPath = Path.GetFullPath(Path.Combine(userDir, fileName));
        if (fullPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch (IOException) { }
        }
    }

    public void DeleteUserCovers(Guid userId)
    {
        var userDir = Path.GetFullPath(Path.Combine(RootPath, userId.ToString("N")));
        if (Directory.Exists(userDir))
        {
            try { Directory.Delete(userDir, recursive: true); } catch (IOException) { }
        }
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public SanitizedCover InspectAndSanitize(ReadOnlySpan<byte> input)
    {
        if (input.Length is < 16 or > MaxFileBytes)
            throw new CoverValidationException("COVER_SIZE_INVALID", "Ảnh bìa không hợp lệ hoặc vượt quá 2 MB.");

        if (input.StartsWith(PngSignature))
            return SanitizePng(input);
        if (input[0] == 0xFF && input[1] == 0xD8 && input[2] == 0xFF)
            return SanitizeJpeg(input);
        if (input[..4].SequenceEqual("RIFF"u8) && input.Slice(8, 4).SequenceEqual("WEBP"u8))
            return SanitizeWebp(input);

        throw new CoverValidationException("COVER_TYPE_UNSUPPORTED", "Chỉ chấp nhận ảnh PNG, JPEG hoặc WebP hợp lệ.");
    }

    private static SanitizedCover SanitizePng(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream(input.Length);
        output.Write(PngSignature);

        var offset = 8;
        var sawIhdr = false;
        var sawIdat = false;
        var sawIend = false;
        int width = 0, height = 0;

        while (offset < input.Length)
        {
            if (sawIend || offset + 12 > input.Length)
                throw new CoverValidationException("COVER_CORRUPTED", "Dữ liệu PNG không hợp lệ.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(offset, 4));
            if (length > MaxFileBytes || offset + 12L + length > input.Length)
                throw new CoverValidationException("COVER_CORRUPTED", "Cấu trúc chunk PNG không hợp lệ.");

            var typeSpan = input.Slice(offset + 4, 4);
            var dataSpan = input.Slice(offset + 8, (int)length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(offset + 8 + (int)length, 4));

            var computedCrc = ComputePngCrc(input.Slice(offset + 4, 4 + (int)length));
            if (computedCrc != expectedCrc)
                throw new CoverValidationException("COVER_CORRUPTED", "Checksum PNG không khớp.");

            var type = Encoding.ASCII.GetString(typeSpan);
            if (!sawIhdr)
            {
                if (type != "IHDR" || length != 13)
                    throw new CoverValidationException("COVER_CORRUPTED", "Thiếu header PNG hợp lệ.");
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(dataSpan[..4]);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(dataSpan.Slice(4, 4));
                ValidateDimensions(width, height);
                sawIhdr = true;
            }

            if (type == "IDAT") sawIdat = true;
            if (type == "IEND")
            {
                if (length != 0) throw new CoverValidationException("COVER_CORRUPTED", "Chunk IEND không hợp lệ.");
                sawIend = true;
            }

            // Keep only critical rendering chunks; strip eXIf, tEXt, zTXt, iTXt, tIME, iCCP, etc.
            if (type is "IHDR" or "PLTE" or "tRNS" or "IDAT" or "IEND")
            {
                output.Write(input.Slice(offset, 12 + (int)length));
            }

            offset += 12 + (int)length;
        }

        if (!sawIhdr || !sawIdat || !sawIend)
            throw new CoverValidationException("COVER_CORRUPTED", "Ảnh PNG không đầy đủ.");

        return new SanitizedCover(output.ToArray(), "png", "image/png", width, height);
    }

    private static readonly uint[] PngCrcTable = BuildPngCrcTable();

    private static uint[] BuildPngCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint ComputePngCrc(ReadOnlySpan<byte> chunkTypeAndData)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in chunkTypeAndData)
            crc = PngCrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static SanitizedCover SanitizeJpeg(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream(input.Length);
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);

        var offset = 2;
        int width = 0, height = 0;
        var sawSof = false;
        var sawEoi = false;

        while (offset < input.Length)
        {
            if (input[offset] != 0xFF)
                throw new CoverValidationException("COVER_CORRUPTED", "Marker JPEG không hợp lệ.");

            while (offset < input.Length && input[offset] == 0xFF) offset++;
            if (offset >= input.Length)
                throw new CoverValidationException("COVER_CORRUPTED", "Ảnh JPEG bị cắt cụt.");

            var marker = input[offset++];
            if (marker == 0xD9)
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                sawEoi = true;
                break;
            }

            if (marker is 0x00 or (>= 0xD0 and <= 0xD8))
                throw new CoverValidationException("COVER_CORRUPTED", "Marker JPEG không hợp lệ.");

            if (offset + 2 > input.Length)
                throw new CoverValidationException("COVER_CORRUPTED", "Segment JPEG không hợp lệ.");

            var segLength = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(offset, 2));
            if (segLength < 2 || offset + segLength > input.Length)
                throw new CoverValidationException("COVER_CORRUPTED", "Độ dài segment JPEG không hợp lệ.");

            // SOF0, SOF1, SOF2
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (segLength < 8)
                    throw new CoverValidationException("COVER_CORRUPTED", "Header khung hình JPEG không hợp lệ.");
                height = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(offset + 5, 2));
                var components = input[offset + 7];
                if (components is not (1 or 3))
                    throw new CoverValidationException("COVER_CORRUPTED", "Hệ màu JPEG không được hỗ trợ.");
                ValidateDimensions(width, height);
                sawSof = true;
            }

            // Strip APP1..APP15 (EXIF, XMP, ICC, IPTC) and COM (0xFE) comments; keep APP0 (JFIF) andessential tables/frames.
            var keepSegment = marker switch
            {
                0xE0 => true, // APP0 (JFIF)
                >= 0xE1 and <= 0xEF => false, // APP1..APP15 metadata
                0xFE => false, // COM comment
                0xC0 or 0xC1 or 0xC2 or 0xC4 or 0xDB or 0xDD or 0xDA => true,
                _ => false
            };

            if (keepSegment)
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                output.Write(input.Slice(offset, segLength));
            }

            offset += segLength;

            if (marker == 0xDA) // SOS: scan entropy-coded data until next real marker (not 0xFF00 or RST0..RST7)
            {
                if (!sawSof)
                    throw new CoverValidationException("COVER_CORRUPTED", "Thiếu thông tin kích thước trước dữ liệu ảnh JPEG.");
                var scanStart = offset;
                while (offset < input.Length)
                {
                    if (input[offset] != 0xFF)
                    {
                        offset++;
                        continue;
                    }
                    var markerStart = offset;
                    while (offset < input.Length && input[offset] == 0xFF) offset++;
                    if (offset >= input.Length)
                        throw new CoverValidationException("COVER_CORRUPTED", "Dữ liệu quét JPEG bị cắt cụt.");
                    var next = input[offset];
                    if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                    {
                        offset++;
                        continue;
                    }
                    output.Write(input.Slice(scanStart, markerStart - scanStart));
                    offset = markerStart;
                    break;
                }
            }
        }

        if (!sawSof || !sawEoi)
            throw new CoverValidationException("COVER_CORRUPTED", "Ảnh JPEG không hoàn chỉnh.");

        return new SanitizedCover(output.ToArray(), "jpg", "image/jpeg", width, height);
    }

    private static SanitizedCover SanitizeWebp(ReadOnlySpan<byte> input)
    {
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4, 4));
        if (riffSize + 8L > input.Length || input.Length < 20)
            throw new CoverValidationException("COVER_CORRUPTED", "Container WebP không hợp lệ.");

        var containerEnd = (int)(riffSize + 8);
        using var chunks = new MemoryStream(containerEnd);
        Span<byte> cleanedVp8x = stackalloc byte[18];
        var offset = 12;
        int width = 0, height = 0;
        var sawImage = false;

        while (offset < containerEnd)
        {
            if (offset + 8 > containerEnd)
                throw new CoverValidationException("COVER_CORRUPTED", "Chunk WebP không hợp lệ.");

            var fourCc = Encoding.ASCII.GetString(input.Slice(offset, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 4, 4));
            var padded = chunkSize + (chunkSize & 1);
            if (chunkSize > MaxFileBytes || offset + 8L + padded > containerEnd)
                throw new CoverValidationException("COVER_CORRUPTED", "Độ dài chunk WebP không hợp lệ.");

            var payload = input.Slice(offset + 8, (int)chunkSize);
            if (fourCc == "VP8X")
            {
                if (chunkSize != 10)
                    throw new CoverValidationException("COVER_CORRUPTED", "Header VP8X không hợp lệ.");
                width = 1 + (payload[4] | (payload[5] << 8) | (payload[6] << 16));
                height = 1 + (payload[7] | (payload[8] << 8) | (payload[9] << 16));
                ValidateDimensions(width, height);
                input.Slice(offset, 18).CopyTo(cleanedVp8x);
                // Clear EXIF (0x08) and XMP (0x04) flags in VP8X feature byte
                cleanedVp8x[8] &= unchecked((byte)~(0x08 | 0x04));
                chunks.Write(cleanedVp8x);
            }
            else if (fourCc == "VP8 ")
            {
                if (chunkSize < 10 || payload[3] != 0x9D || payload[4] != 0x01 || payload[5] != 0x2A)
                    throw new CoverValidationException("COVER_CORRUPTED", "Khung VP8 không hợp lệ.");
                var w = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)) & 0x3FFF;
                var h = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)) & 0x3FFF;
                if (width == 0) { width = w; height = h; ValidateDimensions(width, height); }
                sawImage = true;
                chunks.Write(input.Slice(offset, (int)(8 + padded)));
            }
            else if (fourCc == "VP8L")
            {
                if (chunkSize < 5 || payload[0] != 0x2F)
                    throw new CoverValidationException("COVER_CORRUPTED", "Khung VP8L không hợp lệ.");
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
                var w = (int)(bits & 0x3FFF) + 1;
                var h = (int)((bits >> 14) & 0x3FFF) + 1;
                if (width == 0) { width = w; height = h; ValidateDimensions(width, height); }
                sawImage = true;
                chunks.Write(input.Slice(offset, (int)(8 + padded)));
            }
            else if (fourCc == "ALPH")
            {
                chunks.Write(input.Slice(offset, (int)(8 + padded)));
            }
            // Drop EXIF, XMP , ICCP, ANIM, ANMF and unknown chunks

            offset += (int)(8 + padded);
        }

        if (!sawImage || width == 0 || height == 0)
            throw new CoverValidationException("COVER_CORRUPTED", "Không tìm thấy khung hình WebP hợp lệ.");

        var chunkBytes = chunks.ToArray();
        var result = new byte[12 + chunkBytes.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)(4 + chunkBytes.Length));
        "WEBP"u8.CopyTo(result.AsSpan(8, 4));
        chunkBytes.CopyTo(result.AsSpan(12));

        return new SanitizedCover(result, "webp", "image/webp", width, height);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension)
            throw new CoverValidationException("COVER_DIMENSIONS_INVALID", $"Kích thước ảnh phải trong khoảng 1–{MaxDimension}px.");
    }
}

public sealed class CoverValidationException(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
    public string Detail { get; } = detail;
}
