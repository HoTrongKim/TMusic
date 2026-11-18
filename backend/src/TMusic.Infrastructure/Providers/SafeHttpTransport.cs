using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TMusic.Infrastructure.Providers;

public static class SafeHttpTransport
{
    public static SocketsHttpHandler Create(params string[] hosts)
    {
        var allowed = hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(4), PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            MaxResponseHeadersLength = 16,
            ConnectCallback = async (context, ct) =>
            {
                var endpoint = context.DnsEndPoint;
                if (!allowed.Contains(endpoint.Host) || endpoint.Port != 443)
                    throw new HttpRequestException("Blocked provider destination.");
                // Resolve once, validate every address, and connect to that exact address. TLS still validates original host/SNI.
                var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, AddressFamily.InterNetwork, ct);
                if (addresses.Length == 0 || addresses.Any(ip => !IsPublicAddress(ip)))
                    throw new HttpRequestException("Blocked provider address.");
                foreach (var ip in addresses.Take(4))
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(ip, endpoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException) { socket.Dispose(); }
                    catch { socket.Dispose(); throw; }
                }
                throw new HttpRequestException("Provider unavailable.");
            }
        };
    }

    public static bool IsPublicAddress(IPAddress ip)
    {
        // Only IPv4 is used by this connector; reject IPv6 (including mapped/transition addresses) fail-closed.
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        return ip.GetAddressBytes() switch
        {
            [0 or 10 or 127 or >= 224, _, _, _] => false,
            [100, >= 64 and <= 127, _, _] => false,
            [169, 254, _, _] => false,
            [172, >= 16 and <= 31, _, _] => false,
            [192, 0, 0 or 2, _] or [192, 88, 99, _] or [192, 168, _, _] => false,
            [198, 18 or 19, _, _] or [198, 51, 100, _] => false,
            [203, 0, 113, _] => false,
            _ => true
        };
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new HttpRequestException("Provider response too large.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (output.Length + read > limit) throw new HttpRequestException("Provider response too large.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }
}
