using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Providers;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

public sealed class ProviderDependencyInjectionTests
{
    [Fact]
    public void DefaultLocalStorageBuildsWithProductionDependencyValidationEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "local"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<TMusicDb>(options => options.UseSqlServer(
            "Server=127.0.0.1,1;Database=NeverUsed;Integrated Security=True;Connect Timeout=1;Encrypt=True;TrustServerCertificate=True"));
        services.AddProviders(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.IsType<AudioStorage>(provider.GetRequiredService<IAudioStorage>());
    }
}
