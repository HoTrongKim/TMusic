using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TMusic.Application.Downloads;
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
        services.AddOptions<AudioDownloadOptions>().BindConfiguration("AudioDownload");
        services.AddOptions<ExternalMediaProviderOptions>().BindConfiguration(ExternalMediaProviderOptions.SectionName);
        services.AddOptions<ObjectStorageOptions>().BindConfiguration("Storage:Object");
        services.TryAddTransient<AudioStorage>();
        services.AddTransient<S3AudioStorage>();
        services.AddTransient<HybridAudioStorage>();
        services.TryAddSingleton<IAudioStorage>(provider =>
        {
            var storageProvider = provider.GetRequiredService<IConfiguration>().GetValue<string>("Storage:Provider");
            if (string.IsNullOrWhiteSpace(storageProvider) || storageProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
                return ActivatorUtilities.CreateInstance<AudioStorage>(provider);
            if (storageProvider.Equals("s3", StringComparison.OrdinalIgnoreCase))
                return provider.GetRequiredService<HybridAudioStorage>();
            throw new InvalidOperationException("Storage:Provider must be 'local' or 's3'.");
        });
        services.TryAddSingleton<YtDlpAudioExtractor>();
        services.TryAddSingleton<IAudioExtractor>(sp => sp.GetRequiredService<YtDlpAudioExtractor>());
        services.TryAddSingleton<SourceAccessCircuitBreaker>();
        services.TryAddSingleton<MediaDownloadDiagnostics>();
        services.AddHttpClient<TunelioExternalAudioProvider>((_, http) =>
        {
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TMusic/3.1");
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<ExternalMediaProviderOptions>>().Value;
            var allowed = opt.BuildAllowedDownloadHosts().ToArray();
            return allowed.Length > 0
                ? SafeHttpTransport.Create(allowed)
                : SafeHttpTransport.Create("tunelio.dev");
        });
        services.TryAddTransient<IExternalAudioProvider>(sp => sp.GetRequiredService<TunelioExternalAudioProvider>());
        services.TryAddTransient<MediaDependencyHealthService>();
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
        services.AddScoped<IMediaProvider, YouTubeMediaProvider>();
        services.AddScoped<IMediaProvider, TikTokMediaProvider>();
        services.AddScoped<IMediaProvider, SoundCloudMediaProvider>();
        services.AddScoped<IMediaProvider, AuthorizedExternalMediaProvider>();
        services.AddScoped<MediaProviderRegistry>();
        services.AddScoped<DownloadOrchestrator>();
        services.AddScoped<ImportProcessor>();
        services.AddScoped<AudioDownloadProcessor>();
        services.AddSingleton<AudioDownloadQueueRunner>();
        return services;
    }
}
