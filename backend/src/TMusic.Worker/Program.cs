using Microsoft.EntityFrameworkCore;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Imports;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Providers;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var connection = builder.Configuration.GetConnectionString("DefaultConnection") ?? builder.Configuration.GetConnectionString("TMusic")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDbContext<TMusicDb>(options => options.UseSqlServer(connection));
builder.Services.AddProviders(builder.Configuration);
builder.Services.AddHostedService<ImportWorker>();
await builder.Build().RunAsync();

internal sealed class ImportWorker(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = clock.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var downloads = scope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>();
                var processor = scope.ServiceProvider.GetRequiredService<ImportProcessor>();
                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await processor.PurgeExpiredAsync(stoppingToken);
                    nextCleanup = clock.GetUtcNow().AddMinutes(5);
                }
                if (await downloads.ProcessOneAsync(stoppingToken)) continue;
                if (!await processor.ProcessOneAsync(stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogWarning("Worker loop failed with {ErrorType}; uncommitted work will resume after lease expiry.", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
