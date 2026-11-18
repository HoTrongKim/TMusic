using System.Net;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Providers;
using Xunit;

namespace TMusic.Tests;

public sealed class TikTokPhotoSourceTests
{
    [Theory]
    [InlineData("https://www.tiktok.com/@creator/photo/7709386748572323080?tracking=x")]
    [InlineData("https://m.tiktok.com/@creator/photo/7709386748572323080/")]
    public void PublicPhotoPostKeepsItsKindAndProviderIdentity(string url)
    {
        var source = Assert.IsType<SourceUrl>(SourceUrl.ParseVideo(url));
        Assert.Equal("tiktok", source.Provider);
        Assert.Equal("7709386748572323080", source.Id);
        Assert.Equal("https://www.tiktok.com/@creator/photo/7709386748572323080", source.CanonicalUrl);
    }

    [Fact]
    public async Task ShortPhotoLinkResolvesWithoutFetchingThePostOrWideningRedirects()
    {
        var calls = 0;
        using var http = new HttpClient(new ResponseHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.MovedPermanently)
            {
                Headers = { Location = new Uri("https://www.tiktok.com/@creator/photo/7709386748572323080?tracking=x") }
            };
        }));
        var result = await new TikTokRedirectResolver(http).ResolveAsync("https://vt.tiktok.com/ExamplePhoto/", CancellationToken.None);
        Assert.Equal("https://www.tiktok.com/@creator/photo/7709386748572323080", result?.CanonicalUrl);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://www.tiktok.com.evil.test/@creator/photo/123")]
    [InlineData("https://www.tiktok.com/@creator/photo/not-an-id")]
    [InlineData("https://www.tiktok.com/@creator/photos/123")]
    [InlineData("https://www.tiktok.com/@creator/photo/123/extra")]
    [InlineData("https://www.tiktok.com/@creator/photo/1234567890123456789012345")]
    public void PhotoSupportDoesNotAcceptMalformedOrUntrustedUrls(string url) =>
        Assert.Null(SourceUrl.ParseVideo(url));

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
