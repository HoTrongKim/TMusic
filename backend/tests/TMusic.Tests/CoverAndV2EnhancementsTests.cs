using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TMusic.Api.Identity;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Providers;
using TMusic.Infrastructure.Storage;
using Xunit;
using static TMusic.Tests.SqlApiFixture;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class CoverAndV2EnhancementsTests(SqlApiFixture fixture)
{
    [Fact]
    public void PngSanitizerStripsExifAndTextMetadataWhilePreservingImageChunks()
    {
        var storage = new CoverImageStorage(new ConfigurationBuilder().Build());
        var pngWithMetadata = CreateSamplePng(2, 2, "tEXt", Encoding.ASCII.GetBytes("Comment\0SecretLocationGPS:<script>alert(1)</script>"));
        Assert.Contains("SecretLocationGPS", Encoding.ASCII.GetString(pngWithMetadata));

        var sanitized = storage.InspectAndSanitize(pngWithMetadata);
        Assert.Equal("png", sanitized.Extension);
        Assert.Equal("image/png", sanitized.ContentType);
        Assert.Equal(2, sanitized.Width);
        Assert.Equal(2, sanitized.Height);
        Assert.DoesNotContain("SecretLocationGPS", Encoding.ASCII.GetString(sanitized.Bytes));
        Assert.DoesNotContain("tEXt", Encoding.ASCII.GetString(sanitized.Bytes));
    }

    [Fact]
    public void JpegSanitizerStripsApp1ExifAndComments()
    {
        var storage = new CoverImageStorage(new ConfigurationBuilder().Build());
        var jpegWithExif = CreateMinimalJpegWithExif("Exif\0\0PrivateCameraSerial12345");
        Assert.Contains("PrivateCameraSerial12345", Encoding.ASCII.GetString(jpegWithExif));

        var sanitized = storage.InspectAndSanitize(jpegWithExif);
        Assert.Equal("jpg", sanitized.Extension);
        Assert.Equal(1, sanitized.Width);
        Assert.Equal(1, sanitized.Height);
        Assert.DoesNotContain("PrivateCameraSerial12345", Encoding.ASCII.GetString(sanitized.Bytes));
    }

    [Fact]
    public void InvalidOrPolyglotCoversAreRejected()
    {
        var storage = new CoverImageStorage(new ConfigurationBuilder().Build());
        var svgBytes = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");
        var svgError = Assert.Throws<CoverValidationException>(() => storage.InspectAndSanitize(svgBytes));
        Assert.Equal("COVER_TYPE_UNSUPPORTED", svgError.Code);

        var oversizedDimensionsPng = CreateSamplePng(5000, 5000);
        var dimError = Assert.Throws<CoverValidationException>(() => storage.InspectAndSanitize(oversizedDimensionsPng));
        Assert.Equal("COVER_DIMENSIONS_INVALID", dimError.Code);
    }

    [Fact]
    public void GoogleClientIdConfigurationSupportsAllMasterPromptAliases()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TMUSIC_GOOGLE_WEB_CLIENT_ID"] = "web-id.apps.googleusercontent.comcom\\",
            ["TMUSIC_GOOGLE_ANDROID_CLIENT_ID"] = "android-id.apps.googleusercontent.com",
            ["TMUSIC_GOOGLE_IOS_CLIENT_ID"] = "ios-id.apps.googleusercontent.com"
        }).Build();

        var verifier = new ExternalTokenVerifier(config);
        Assert.True(verifier.Available("google"));
        var audiences = ExternalTokenVerifier.ConfiguredAudiences(config, "google");
        Assert.Contains("web-id.apps.googleusercontent.com", audiences);
        Assert.Contains("android-id.apps.googleusercontent.com", audiences);
        Assert.Contains("ios-id.apps.googleusercontent.com", audiences);
    }

    [Fact]
    public void TikTokThumbnailAllowlistAcceptsOfficialCdnAndBlocksArbitraryHosts()
    {
        using var allowedDoc = JsonDocument.Parse("{\"thumbnail_url\":\"https://p16-sign-va.tiktokcdn.com/tos-maliva-p-0068/cover.jpeg\"}");
        Assert.Equal("https://p16-sign-va.tiktokcdn.com/tos-maliva-p-0068/cover.jpeg", TikTokMetadata.Thumbnail(allowedDoc.RootElement));

        using var blockedDoc = JsonDocument.Parse("{\"thumbnail_url\":\"https://tracker.attacker.example/cover.jpeg\"}");
        Assert.Null(TikTokMetadata.Thumbnail(blockedDoc.RootElement));
    }

    [SqlFact]
    public async Task AlbumCoverUploadSanitizesMetadataEnforcesOwnershipAndCleansUpOnDelete()
    {
        var alice = await fixture.RegisterAsync();
        var bob = await fixture.RegisterAsync();
        using var a = fixture.Client(alice.AccessToken);
        using var b = fixture.Client(bob.AccessToken);

        var created = await Json(await a.PostAsJsonAsync("/api/v1/albums", new { name = "Album Có Ảnh Bìa" }));
        var albumId = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString()!;

        // Reject disguised SVG upload
        using (var badForm = new MultipartFormDataContent())
        {
            var badFile = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><script>evil()</script></html>"));
            badFile.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            badForm.Add(badFile, "file", "avatar.png");
            using var badReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/albums/{albumId}/cover") { Content = badForm };
            badReq.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            await Expect(await a.SendAsync(badReq), HttpStatusCode.UnprocessableEntity);
        }

        // Upload valid PNG containing metadata that must be stripped
        var png = CreateSamplePng(4, 4, "tEXt", Encoding.ASCII.GetBytes("Author\0SensitiveExifPayload"));
        JsonElement updated;
        using (var form = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(png);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", "../unsafe-name.png");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/albums/{albumId}/cover") { Content = form };
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            updated = await Json(await a.SendAsync(req));
        }

        var coverUrl = updated.GetProperty("coverImageUrl").GetString()!;
        Assert.StartsWith($"/api/v1/covers/{alice.UserId:D}/", coverUrl);
        Assert.DoesNotContain("unsafe-name", coverUrl);

        // Alice can fetch her sanitized cover with strict CSP headers
        var fetched = await a.GetAsync(coverUrl);
        await Expect(fetched, HttpStatusCode.OK);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'none'", fetched.Headers.GetValues("Content-Security-Policy").Single());
        var fetchedBytes = await fetched.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("SensitiveExifPayload", Encoding.ASCII.GetString(fetchedBytes));

        // Bob cannot read Alice's private cover (IDOR check)
        await Expect(await b.GetAsync(coverUrl), HttpStatusCode.NotFound);

        // Deleting cover removes file
        var nextVersion = updated.GetProperty("version").GetString()!;
        await Expect(await Mutation(a, HttpMethod.Delete, $"/api/v1/albums/{albumId}/cover", version: nextVersion), HttpStatusCode.OK);
        await Expect(await a.GetAsync(coverUrl), HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task AlbumReorderUnorganizedFilterAndDuplicateResolveReturnFullContext()
    {
        var user = await fixture.RegisterAsync();
        using var client = fixture.Client(user.AccessToken);

        var a1 = await Json(await client.PostAsJsonAsync("/api/v1/albums", new { name = "Album 1" }));
        var a2 = await Json(await client.PostAsJsonAsync("/api/v1/albums", new { name = "Album 2" }));
        var id1 = a1.GetProperty("id").GetGuid();
        var id2 = a2.GetProperty("id").GetGuid();

        await Expect(await Mutation(client, HttpMethod.Patch, "/api/v1/albums/order", new { albumIds = new[] { id2, id1 } }), HttpStatusCode.NoContent);
        var ordered = await Json(await client.GetAsync("/api/v1/albums?sort=order"));
        Assert.Equal(id2, ordered.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(id1, ordered.GetProperty("items")[1].GetProperty("id").GetGuid());

        // Save one organized song and one unorganized song
        var organizedSong = await Json(await client.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://youtu.be/AbCdEfGhI91",
            customTitle = "Bài Đã Vào Album",
            customArtist = "Nghệ sĩ A",
            albumIds = new[] { id1 }
        }));
        await Json(await client.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://youtu.be/AbCdEfGhI92",
            customTitle = "Bài Chưa Sắp Xếp"
        }));

        var unorganized = await Json(await client.GetAsync("/api/v1/library/songs?unorganized=true"));
        Assert.Equal(1, unorganized.GetProperty("total").GetInt32());
        Assert.Equal("Bài Chưa Sắp Xếp", unorganized.GetProperty("items")[0].GetProperty("customTitle").GetString());

        // Resolve duplicate link returns existingSong details for UI preview
        var resolved = await Json(await client.PostAsJsonAsync("/api/v1/songs/resolve", new { url = "https://www.youtube.com/watch?v=AbCdEfGhI91" }));
        Assert.Equal(organizedSong.GetProperty("id").GetGuid(), resolved.GetProperty("existingLibrarySongId").GetGuid());
        Assert.Equal("Bài Đã Vào Album", resolved.GetProperty("existingSong").GetProperty("customTitle").GetString());
        Assert.Equal(id1, resolved.GetProperty("existingSong").GetProperty("albumIds")[0].GetGuid());
    }

    [SqlFact]
    public async Task MixedSourceAlbumAcceptsEveryProviderThroughCreateAndAddRoutesAndPreservesShareImport()
    {
        var owner = await fixture.RegisterAsync();
        var recipient = await fixture.RegisterAsync();
        using var ownerClient = fixture.Client(owner.AccessToken);
        using var recipientClient = fixture.Client(recipient.AccessToken);

        var album = await Json(await ownerClient.PostAsJsonAsync("/api/v1/albums", new { name = "Album đa nguồn" }));
        var albumId = album.GetProperty("id").GetGuid();

        var youtube = await Json(await ownerClient.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://youtu.be/MixSource01",
            customTitle = "YouTube track",
            albumIds = new[] { albumId }
        }));
        Assert.Equal("youtube", youtube.GetProperty("provider").GetString());

        var tiktok = await Json(await ownerClient.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://www.tiktok.com/@artist/video/7123456789012345678",
            customTitle = "TikTok track"
        }));
        Assert.Equal("tiktok", tiktok.GetProperty("provider").GetString());

        var currentAlbum = await Json(await ownerClient.GetAsync($"/api/v1/albums/{albumId}"));
        await Expect(await Mutation(ownerClient, HttpMethod.Post, $"/api/v1/albums/{albumId}/songs",
            new { userLibrarySongId = tiktok.GetProperty("id").GetGuid() },
            currentAlbum.GetProperty("version").GetString()), HttpStatusCode.NoContent);

        var soundCloud = await Json(await ownerClient.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://soundcloud.com/indie-producer/mixed-track",
            customTitle = "SoundCloud track",
            albumIds = new[] { albumId }
        }));
        Assert.Equal("soundcloud", soundCloud.GetProperty("provider").GetString());

        var albumSongs = await Json(await ownerClient.GetAsync($"/api/v1/albums/{albumId}/songs"));
        Assert.Equal(3, albumSongs.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "youtube", "tiktok", "soundcloud" },
            albumSongs.GetProperty("items").EnumerateArray()
                .Select(song => song.GetProperty("provider").GetString()).ToArray());

        var tiktokOnly = await Json(await ownerClient.GetAsync($"/api/v1/albums/{albumId}/songs?provider=tiktok"));
        Assert.Equal(1, tiktokOnly.GetProperty("total").GetInt32());
        Assert.Equal("tiktok", tiktokOnly.GetProperty("items")[0].GetProperty("provider").GetString());
        await Expect(await ownerClient.GetAsync($"/api/v1/albums/{albumId}/songs?provider=unknown"), HttpStatusCode.BadRequest);

        var capabilities = await Json(await ownerClient.GetAsync("/api/v1/system/capabilities"));
        Assert.True(capabilities.GetProperty("crossProviderMixedCollections").GetBoolean());

        var share = await Json(await ownerClient.PostAsync($"/api/v1/albums/{albumId}/share", null));
        var shareCode = share.GetProperty("shareCode").GetString()!;
        var imported = await Json(await recipientClient.PostAsync($"/api/v1/shared-albums/{shareCode}/import", null));
        var importedSongs = await Json(await recipientClient.GetAsync($"/api/v1/albums/{imported.GetProperty("id").GetGuid()}/songs"));
        Assert.Equal(new[] { "youtube", "tiktok", "soundcloud" },
            importedSongs.GetProperty("items").EnumerateArray()
                .Select(song => song.GetProperty("provider").GetString()).ToArray());
    }

    [SqlFact]
    public async Task SoundCloudLinksGuestPreviewAndRevocableAlbumSharingBetweenUsersWorkEndToEnd()
    {
        var alice = await fixture.RegisterAsync();
        var bob = await fixture.RegisterAsync();
        using var aliceClient = fixture.Client(alice.AccessToken);
        using var bobClient = fixture.Client(bob.AccessToken);
        using var guestClient = fixture.Client();

        // Guest can preview SoundCloud metadata without login
        var guestPreview = await Json(await guestClient.PostAsJsonAsync(
            "/api/v1/guest/songs/resolve",
            new { url = "https://soundcloud.com/indie-producer/sunset-chill?si=abc" }));
        Assert.Equal("soundcloud", guestPreview.GetProperty("provider").GetString());
        Assert.Equal("https://soundcloud.com/indie-producer/sunset-chill", guestPreview.GetProperty("canonicalUrl").GetString());

        // Alice creates a SoundCloud album and saves a SoundCloud song
        var aliceAlbum = await Json(await aliceClient.PostAsJsonAsync("/api/v1/albums", new
        {
            name = "Chill SoundCloud Đêm",
            description = "Tuyển chọn nhạc đêm",
            coverImageUrl = "cover:green"
        }));
        var aliceAlbumId = aliceAlbum.GetProperty("id").GetGuid();

        await Expect(await aliceClient.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://soundcloud.com/indie-producer/sunset-chill",
            customTitle = "Hoàng Hôn Lofi",
            customArtist = "Indie Producer",
            albumIds = new[] { aliceAlbumId }
        }), HttpStatusCode.Created);

        // Alice creates a share code for her album
        var shareResp = await Json(await aliceClient.PostAsync($"/api/v1/albums/{aliceAlbumId}/share", null));
        var code = shareResp.GetProperty("shareCode").GetString()!;
        Assert.Equal(8, code.Length);

        // Guest & Bob can preview the shared album with source attribution
        var sharedView = await Json(await guestClient.GetAsync($"/api/v1/shared-albums/{code}"));
        Assert.Equal("Chill SoundCloud Đêm", sharedView.GetProperty("albumName").GetString());
        Assert.Equal(1, sharedView.GetProperty("songCount").GetInt32());
        Assert.Equal("soundcloud", sharedView.GetProperty("songs")[0].GetProperty("provider").GetString());

        // Bob imports the shared album into his own account
        var importResp = await bobClient.PostAsync($"/api/v1/shared-albums/{code}/import", null);
        await Expect(importResp, HttpStatusCode.Created);
        var imported = await Json(importResp);
        var bobAlbumId = imported.GetProperty("id").GetGuid();
        Assert.NotEqual(aliceAlbumId, bobAlbumId);
        Assert.Equal(1, imported.GetProperty("songCount").GetInt32());

        var bobSongs = await Json(await bobClient.GetAsync($"/api/v1/albums/{bobAlbumId}/songs"));
        Assert.Equal("Hoàng Hôn Lofi", bobSongs.GetProperty("items")[0].GetProperty("customTitle").GetString());
        Assert.Equal("soundcloud", bobSongs.GetProperty("items")[0].GetProperty("provider").GetString());

        // Alice revokes the share code -> code immediately returns 404 while Bob keeps his imported copy
        await Expect(await aliceClient.DeleteAsync($"/api/v1/albums/{aliceAlbumId}/share"), HttpStatusCode.NoContent);
        await Expect(await guestClient.GetAsync($"/api/v1/shared-albums/{code}"), HttpStatusCode.NotFound);
    }

    private static byte[] CreateSamplePng(int width, int height, string? extraChunkType = null, byte[]? extraChunkData = null)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8; // 8-bit
        ihdr[9] = 2; // RGB
        WritePngChunk(ms, "IHDR", ihdr);

        if (extraChunkType is not null && extraChunkData is not null)
            WritePngChunk(ms, extraChunkType, extraChunkData);

        byte[] compressedScanline;
        using (var zlibOut = new MemoryStream())
        {
            using (var zlib = new ZLibStream(zlibOut, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write([0, 23, 40, 61]); // filter 0 + 1 navy pixel
            }
            compressedScanline = zlibOut.ToArray();
        }
        WritePngChunk(ms, "IDAT", compressedScanline);
        WritePngChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)data.Length);
        output.Write(lenBytes);

        var combined = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, combined.AsSpan(0, 4));
        data.CopyTo(combined.AsSpan(4));
        output.Write(combined);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, CoverImageStorage.ComputePngCrc(combined));
        output.Write(crcBytes);
    }

    private static byte[] CreateMinimalJpegWithExif(string exifPayload)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]); // SOI

        // APP1 (EXIF)
        var exifBytes = Encoding.ASCII.GetBytes(exifPayload);
        WriteJpegSegment(ms, 0xE1, exifBytes);

        // DQT (quantization table)
        var dqt = new byte[65];
        for (var i = 1; i < dqt.Length; i++) dqt[i] = 16;
        WriteJpegSegment(ms, 0xDB, dqt);

        // SOF0 (1x1 grayscale)
        WriteJpegSegment(ms, 0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]);

        // DHT (minimal DC/AC table)
        WriteJpegSegment(ms, 0xC4, [0x00, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        // SOS + 2 scan bytes + EOI
        WriteJpegSegment(ms, 0xDA, [1, 1, 0, 0, 63, 0]);
        ms.Write([0x7F, 0x00, 0xFF, 0xD9]);
        return ms.ToArray();
    }

    private static void WriteJpegSegment(Stream ms, byte marker, ReadOnlySpan<byte> payload)
    {
        ms.WriteByte(0xFF);
        ms.WriteByte(marker);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)(payload.Length + 2));
        ms.Write(len);
        ms.Write(payload);
    }
}
