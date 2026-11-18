using System.Net;
using TMusic.Application.Providers;

namespace TMusic.Infrastructure.Providers;

public sealed class TikTokRedirectResolver(HttpClient http)
{
    public async Task<SourceUrl?> ResolveAsync(string input, CancellationToken ct)
    {
        if (!SourceUrl.TryHttps(input, out var current)) throw new ProviderException("INVALID_SOURCE_URL");
        if (SourceUrl.ParseVideo(input) is { Provider: "tiktok" } canonical) return canonical;
        if (!SourceUrl.IsTikTokShort(current)) throw new ProviderException("INVALID_TIKTOK_URL");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            for (var hop = 0; hop < 5; hop++)
            {
                if (!visited.Add(current.AbsoluteUri)) throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                    HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return null;
                if (response.Headers.Location is not { } location || !Uri.TryCreate(current, location, out var target) ||
                    !SourceUrl.TryHttps(target.AbsoluteUri, out var validated)) throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
                if (SourceUrl.ParseVideo(validated.AbsoluteUri) is { Provider: "tiktok" } result) return result;
                if (!SourceUrl.IsTikTokShort(validated)) throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
                current = validated;
            }
            throw new ProviderException("TOO_MANY_SOURCE_REDIRECTS");
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
}
