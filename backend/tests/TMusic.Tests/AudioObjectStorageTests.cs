using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

public sealed class AudioObjectStorageTests
{
    [Fact]
    public async Task S3CompatibleStorageUploadsListsAndStreamsByteRanges()
    {
        var objects = new ConcurrentDictionary<string, (byte[] Bytes, DateTime LastModified)>(StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.MapMethods("/{**key}", ["PUT", "HEAD", "GET", "DELETE"], async context =>
        {
            var key = string.Join('/', context.Request.Path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1));
            if (context.Request.Method == "PUT")
            {
                await using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body, context.RequestAborted);
                objects[key] = (body.ToArray(), DateTime.UtcNow);
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ETag = "\"fixture-etag\"";
                return;
            }

            if (context.Request.Method == "DELETE")
            {
                objects.TryRemove(key, out _);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            if (context.Request.Query.ContainsKey("list-type"))
            {
                var prefix = context.Request.Query["prefix"].ToString();
                var contents = objects.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(pair => $"<Contents><Key>{WebUtility.HtmlEncode(pair.Key)}</Key><LastModified>{pair.Value.LastModified:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified><ETag>\"fixture-etag\"</ETag><Size>{pair.Value.Bytes.Length}</Size><StorageClass>STANDARD</StorageClass></Contents>");
                context.Response.ContentType = "application/xml";
                await context.Response.WriteAsync(
                    $"<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><Name>tmusic-test</Name>{string.Join(string.Empty, contents)}<IsTruncated>false</IsTruncated></ListBucketResult>",
                    context.RequestAborted);
                return;
            }

            if (!objects.TryGetValue(key, out var stored))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.ETag = "\"fixture-etag\"";
            context.Response.Headers.LastModified = stored.LastModified.ToString("R", CultureInfo.InvariantCulture);
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.ContentType = "audio/mpeg";
            if (context.Request.Method == "HEAD")
            {
                context.Response.ContentLength = stored.Bytes.Length;
                return;
            }

            var range = context.Request.Headers.Range.ToString();
            var start = 0;
            var end = stored.Bytes.Length - 1;
            if (range.StartsWith("bytes=", StringComparison.Ordinal))
            {
                var bounds = range[6..].Split('-', 2);
                if (bounds.Length != 2 || !int.TryParse(bounds[0], out start) || !int.TryParse(bounds[1], out end))
                {
                    context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status206PartialContent;
                context.Response.Headers.ContentRange = $"bytes {start}-{end}/{stored.Bytes.Length}";
            }
            context.Response.ContentLength = end - start + 1;
            await context.Response.Body.WriteAsync(stored.Bytes.AsMemory(start, end - start + 1), context.RequestAborted);
        });

        await server.StartAsync();
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var storage = new S3AudioStorage(Options.Create(new ObjectStorageOptions
            {
                Endpoint = address,
                Bucket = "tmusic-test",
                Region = "us-east-1",
                AccessKey = "fixture-access-key",
                SecretKey = "fixture-secret-key",
                Prefix = "test-audio"
            }));
            var userId = Guid.NewGuid();
            var payload = new byte[4096];
            payload[0] = (byte)'I';
            payload[1] = (byte)'D';
            payload[2] = (byte)'3';
            payload[10] = 0xff;
            payload[11] = 0xfb;

            var saved = await storage.SaveBytesAsync(userId, payload, CancellationToken.None);
            Assert.Equal(payload.Length, saved.SizeBytes);
            var listing = storage.ListAllFiles();
            Assert.Contains(listing, file => file.UserId == userId && file.FileName == saved.FileName && file.SizeBytes == payload.Length);

            var opened = await storage.OpenReadAsync(userId, saved.FileName, CancellationToken.None);
            Assert.NotNull(opened);
            Assert.Equal(payload.Length, opened.Value.Length);
            await using var stream = opened.Value.Stream;
            stream.Position = 100;
            var slice = new byte[40];
            Assert.Equal(slice.Length, await stream.ReadAsync(slice));
            Assert.Equal(payload[100..140], slice);

            var legacyRoot = Path.Combine(Path.GetTempPath(), "TMusicLegacyAudio_" + Guid.NewGuid().ToString("N"));
            try
            {
                var legacyLocal = new AudioStorage(new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Storage:AudioPath"] = legacyRoot }).Build());
                var legacyFile = await legacyLocal.SaveBytesAsync(userId, payload, CancellationToken.None);
                var hybrid = new HybridAudioStorage(legacyLocal, storage);
                var legacyRead = await hybrid.OpenReadAsync(userId, legacyFile.FileName, CancellationToken.None);
                Assert.NotNull(legacyRead);
                await using var legacyStream = legacyRead.Value.Stream;
                var legacyBytes = new byte[payload.Length];
                Assert.Equal(legacyBytes.Length, await legacyStream.ReadAsync(legacyBytes));
                Assert.Equal(payload, legacyBytes);
                Assert.Contains(hybrid.ListAllFiles(), file => file.UserId == userId && file.FileName == legacyFile.FileName);
            }
            finally
            {
                if (Directory.Exists(legacyRoot)) Directory.Delete(legacyRoot, recursive: true);
            }
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public void S3CompatibleStorageRejectsPublicHttpAndCredentialBearingEndpoints()
    {
        var options = Options.Create(new ObjectStorageOptions
        {
            Endpoint = "http://objects.example.com",
            Bucket = "bucket",
            AccessKey = "fixture-access-key",
            SecretKey = "fixture-secret-key"
        });

        var error = Assert.Throws<InvalidOperationException>(() => new S3AudioStorage(options));
        Assert.DoesNotContain(options.Value.SecretKey!, error.ToString());

        options = Options.Create(new ObjectStorageOptions
        {
            Endpoint = "https://access:secret@objects.example.com",
            Bucket = "bucket",
            AccessKey = "fixture-access-key",
            SecretKey = "fixture-secret-key"
        });
        error = Assert.Throws<InvalidOperationException>(() => new S3AudioStorage(options));
        Assert.DoesNotContain("access:secret", error.ToString());
    }
}
