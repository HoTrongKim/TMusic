using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Imports;
using TMusic.Infrastructure.Storage;

namespace TMusic.Infrastructure.Providers;

public static class ProviderServices
{
    public static IServiceCollection AddProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAudioStorage, AudioStorage>();
        services.TryAddSingleton<IAudioExtractor, YtDlpAudioExtractor>();
        services.AddScoped<ProviderQuota>();
        services.AddHttpClient<YouTubeApi>(http =>
        {
            http.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
            http.Timeout = TimeSpan.FromSeconds(12);
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => SafeHttpTransport.Create("www.googleapis.com"));
        services.AddHttpClient<TikTokRedirectResolver>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() =>
                SafeHttpTransport.Create("vt.tiktok.com", "vm.tiktok.com", "www.tiktok.com", "tiktok.com"));
        services.AddHttpClient<TikTokMetadata>(http => http.Timeout = TimeSpan.FromSeconds(8))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => SafeHttpTransport.Create("www.tiktok.com"));
        services.AddHttpClient<SoundCloudRedirectResolver>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() =>
                SafeHttpTransport.Create("on.soundcloud.com", "soundcloud.com", "www.soundcloud.com", "m.soundcloud.com"));
        services.AddHttpClient<SoundCloudMetadata>(http => http.Timeout = TimeSpan.FromSeconds(8))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => SafeHttpTransport.Create("soundcloud.com"));
        services.AddScoped<IYouTubePlaylist>(provider => provider.GetRequiredService<YouTubeApi>());
        services.AddScoped<ISourceResolver, SourceResolver>();
        services.AddScoped<ImportProcessor>();
        services.AddScoped<AudioDownloadProcessor>();
        return services;
    }
}
