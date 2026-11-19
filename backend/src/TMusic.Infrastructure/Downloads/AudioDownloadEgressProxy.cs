using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Providers;

namespace TMusic.Infrastructure.Downloads;

/// <summary>
/// Per-extraction HTTPS CONNECT gate. Every new destination is allowlisted, resolved once,
/// checked for public addresses, then connected by IP. TLS remains end-to-end with yt-dlp.
/// Cross-host redirects must establish another validated tunnel; HTTP destinations fail closed.
/// </summary>
public sealed class AudioDownloadEgressProxy : IAsyncDisposable
{
    private const int MaximumConnections = 64;
    private const long MaximumTransferBytes = 256L * 1024 * 1024;
    private readonly string provider;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> lookup;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource shutdown = new(TimeSpan.FromMinutes(3));
    private readonly SemaphoreSlim concurrent = new(16);
    private readonly ConcurrentBag<Task> connections = [];
    private readonly string credentials = "tmusic:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Task accepting;
    private int connectionCount;
    private long transferred;

    public AudioDownloadEgressProxy(string provider, Func<string, CancellationToken, Task<IPAddress[]>>? lookup = null)
    {
        if (provider is not ("youtube" or "tiktok" or "soundcloud")) throw new ProviderException("UNSUPPORTED_SOURCE_URL");
        this.provider = provider;
        this.lookup = lookup ?? ((host, ct) => Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct));
        listener.Start(16);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ProxyAddress = $"http://{credentials}@127.0.0.1:{port}";
        accepting = AcceptAsync();
    }

    // Contains an ephemeral proxy credential; pass only to the child process, never logs.
    public string ProxyAddress { get; }

    public static bool IsAllowedHost(string provider, string host)
    {
        if (host.Length is 0 or > 253 || host.EndsWith('.') || IPAddress.TryParse(host, out _) ||
            host.Split('.').Any(label => label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-' ||
                label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) return false;
        host = host.ToLowerInvariant();
        static bool Domain(string host, string root) => host == root || host.EndsWith('.' + root, StringComparison.Ordinal);
        return provider switch
        {
            "youtube" => host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or
                "youtu.be" or "youtubei.googleapis.com" or "www.googleapis.com" ||
                Domain(host, "googlevideo.com") || Domain(host, "ytimg.com"),
            "tiktok" => host is "tiktok.com" or "www.tiktok.com" or "m.tiktok.com" or "vt.tiktok.com" or "vm.tiktok.com" ||
                Domain(host, "tiktokv.com") || Domain(host, "tiktokcdn.com") ||
                Domain(host, "tiktokcdn-us.com") || Domain(host, "tiktokcdn-eu.com"),
            "soundcloud" => host is "soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com" or
                "on.soundcloud.com" or "api.soundcloud.com" or "api-v2.soundcloud.com" ||
                Domain(host, "sndcdn.com") || Domain(host, "soundcloud.cloud"),
            _ => false
        };
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(shutdown.Token);
                if (Interlocked.Increment(ref connectionCount) > MaximumConnections || !concurrent.Wait(0))
                {
                    client.Dispose();
                    continue;
                }
                connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (SocketException) when (shutdown.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (shutdown.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var tunnelStarted = false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var headers = await ReadHeadersAsync(stream, deadline.Token);
                if (headers is null) { await RejectAsync(stream, 400, deadline.Token); return; }
                var lines = headers.Split("\r\n", StringSplitOptions.None);
                var first = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (first.Length != 3 || first[0] != "CONNECT" || first[2] is not ("HTTP/1.1" or "HTTP/1.0"))
                { await RejectAsync(stream, 405, deadline.Token); return; }
                var authorization = lines.Where(x => x.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase)).ToArray();
                var expected = Encoding.ASCII.GetBytes("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials)));
                var actual = authorization.Length == 1 ? Encoding.ASCII.GetBytes(authorization[0][20..].Trim()) : [];
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                { await RejectAsync(stream, 407, deadline.Token); return; }
                var authority = first[1];
                if (!authority.EndsWith(":443", StringComparison.Ordinal) ||
                    !IsAllowedHost(provider, authority[..^4]))
                { await RejectAsync(stream, 403, deadline.Token); return; }
                var host = authority[..^4];
                var addresses = await lookup(host, deadline.Token);
                if (addresses.Length == 0 || addresses.Any(x => !SafeHttpTransport.IsPublicAddress(x)))
                { await RejectAsync(stream, 403, deadline.Token); return; }

                using var upstream = await ConnectPinnedAsync(addresses, deadline.Token);
                await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), deadline.Token);
                tunnelStarted = true;
                using var pumping = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                var outbound = PumpAsync(stream, upstream, pumping.Token);
                var inbound = PumpAsync(upstream, stream, pumping.Token);
                await Task.WhenAny(outbound, inbound);
                pumping.Cancel();
                try { await Task.WhenAll(outbound, inbound); } catch (OperationCanceledException) { }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                if (!tunnelStarted)
                {
                    try { await RejectAsync(stream, 502, shutdown.Token); }
                    catch (Exception writeError) when (writeError is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
            finally { concurrent.Release(); }
        }
    }

    private static async Task<NetworkStream> ConnectPinnedAsync(IPAddress[] addresses, CancellationToken ct)
    {
        foreach (var ip in addresses.Take(4))
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, 443), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new IOException("Provider network unavailable.");
    }

    private async Task PumpAsync(Stream input, Stream output, CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            idle.CancelAfter(TimeSpan.FromSeconds(20));
            var read = await input.ReadAsync(buffer, idle.Token);
            if (read == 0) return;
            if (Interlocked.Add(ref transferred, read) > MaximumTransferBytes) throw new IOException("Download transfer limit exceeded.");
            idle.CancelAfter(TimeSpan.FromSeconds(20));
            await output.WriteAsync(buffer.AsMemory(0, read), idle.Token);
        }
    }

    private static async Task<string?> ReadHeadersAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new byte[16 * 1024];
        for (var length = 1; length <= bytes.Length; length++)
        {
            if (await stream.ReadAsync(bytes.AsMemory(length - 1, 1), ct) == 0) return null;
            var value = bytes[length - 1];
            if (value != 9 && value != 10 && value != 13 && (value < 32 || value > 126)) return null;
            if (length >= 4 && bytes[length - 4] == 13 && bytes[length - 3] == 10 && bytes[length - 2] == 13 && value == 10)
                return Encoding.ASCII.GetString(bytes, 0, length);
        }
        return null;
    }

    private static Task RejectAsync(Stream stream, int code, CancellationToken ct) => stream.WriteAsync(Encoding.ASCII.GetBytes(
        $"HTTP/1.1 {code} Rejected\r\nConnection: close\r\nContent-Length: 0\r\nProxy-Authenticate: Basic realm=\"TMusic\"\r\n\r\n"), ct).AsTask();

    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync();
        listener.Stop();
        await accepting;
        await Task.WhenAll(connections);
        concurrent.Dispose();
        shutdown.Dispose();
    }
}
