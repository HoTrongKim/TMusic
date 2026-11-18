using System.Net;
using System.Net.Sockets;
using System.Text;
using TMusic.Infrastructure.Downloads;
using Xunit;

namespace TMusic.Tests;

public sealed class AudioDownloadEgressTests
{
    [Theory]
    [InlineData("youtube", "www.youtube.com", true)]
    [InlineData("youtube", "rr1---sn-example.googlevideo.com", true)]
    [InlineData("youtube", "youtube.com.evil.example", false)]
    [InlineData("youtube", "evilgooglevideo.com", false)]
    [InlineData("youtube", "www.soundcloud.com", false)]
    [InlineData("youtube", "www.youtube.com.", false)]
    [InlineData("youtube", "127.0.0.1", false)]
    [InlineData("tiktok", "v16.tiktokcdn.com", true)]
    [InlineData("tiktok", "v16.tiktokcdn.com.evil.example", false)]
    [InlineData("soundcloud", "api-v2.soundcloud.com", true)]
    [InlineData("soundcloud", "cf-media.sndcdn.com", true)]
    [InlineData("soundcloud", "accounts.google.com", false)]
    public void DestinationPolicyMatchesOnlyProviderHostsAndKnownCdnBoundaries(string provider, string host, bool expected)
        => Assert.Equal(expected, AudioDownloadEgressProxy.IsAllowedHost(provider, host));

    [Theory]
    [InlineData("evil.example:443")]
    [InlineData("www.youtube.com:80")]
    [InlineData("127.0.0.1:443")]
    [InlineData("www.youtube.com:443@evil.example:443")]
    [InlineData("www.youtube.com.:443")]
    public async Task ConnectRejectsUnsupportedDestinationsBeforeDns(string destination)
    {
        var lookups = 0;
        await using var proxy = new AudioDownloadEgressProxy("youtube", (_, _) =>
        {
            Interlocked.Increment(ref lookups);
            return Task.FromResult(new[] { IPAddress.Loopback });
        });
        var response = await Request(proxy, $"CONNECT {destination} HTTP/1.1", authenticated: true);
        Assert.StartsWith("HTTP/1.1 403 ", response);
        Assert.Equal(0, lookups);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    public async Task ConnectRejectsPrivateOrMixedDnsAnswers(string blocked)
    {
        var lookups = 0;
        await using var proxy = new AudioDownloadEgressProxy("youtube", (_, _) =>
        {
            Interlocked.Increment(ref lookups);
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse(blocked) });
        });
        var response = await Request(proxy, "CONNECT www.youtube.com:443 HTTP/1.1", authenticated: true);
        Assert.StartsWith("HTTP/1.1 403 ", response);
        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task ConnectRequiresPerExtractionAuthentication()
    {
        await using var proxy = new AudioDownloadEgressProxy("youtube", (_, _) => throw new InvalidOperationException("DNS must not run before authentication."));
        var response = await Request(proxy, "CONNECT www.youtube.com:443 HTTP/1.1", authenticated: false);
        Assert.StartsWith("HTTP/1.1 407 ", response);
    }

    [Fact]
    public async Task PlainHttpRequestsAreBlocked()
    {
        await using var proxy = new AudioDownloadEgressProxy("youtube", (_, _) => throw new InvalidOperationException("DNS must not run for HTTP."));
        var response = await Request(proxy, "GET http://www.youtube.com/watch HTTP/1.1", authenticated: true);
        Assert.StartsWith("HTTP/1.1 405 ", response);
    }

    [Fact]
    public async Task OversizedConnectHeadersAreRejectedBeforeDns()
    {
        await using var proxy = new AudioDownloadEgressProxy("youtube", (_, _) => throw new InvalidOperationException("DNS must not run for oversized headers."));
        var response = await Request(proxy, "CONNECT www.youtube.com:443 HTTP/1.1\r\nX-Large: " + new string('x', 17_000), authenticated: true);
        Assert.StartsWith("HTTP/1.1 400 ", response);
    }

    private static async Task<string> Request(AudioDownloadEgressProxy proxy, string requestLine, bool authenticated)
    {
        var uri = new Uri(proxy.ProxyAddress);
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(IPAddress.Loopback, uri.Port, timeout.Token);
        var authorization = authenticated
            ? "Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(uri.UserInfo)) + "\r\n"
            : "";
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(requestLine + "\r\n" + authorization + "\r\n"), timeout.Token);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadLineAsync(timeout.Token) ?? "";
    }
}
