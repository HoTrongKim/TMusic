using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Application.Identity;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Persistence;
using Xunit;

namespace TMusic.Tests;

[CollectionDefinition("SQL API", DisableParallelization = true)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFixture>;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TMUSIC_SQL_TEST_SERVER")))
            Skip = "Set TMUSIC_SQL_TEST_SERVER to a SQL Server with permission to create isolated TMusicV2Test_* databases.";
    }
}

// A real SQL Server and the actual API pipeline are used. Only email delivery and provider metadata
// are substituted explicitly. This is not a Google/Apple/YouTube/TikTok live-service test.
public sealed class SqlApiFixture : IAsyncLifetime
{
    public const string Password = "Test-Only!2026Strong";
    public CaptureMail Mail { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; } = "TMusicV2Test_" + Guid.NewGuid().ToString("N");
    public string CoversPath { get; } = Path.Combine(Path.GetTempPath(), "TMusicV2TestCovers_" + Guid.NewGuid().ToString("N"));
    public string AudioPath { get; } = Path.Combine(Path.GetTempPath(), "TMusicV2TestAudio_" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("TMUSIC_SQL_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(server)) return;
        var builder = new SqlConnectionStringBuilder(server) { InitialCatalog = DatabaseName };
        ConnectionString = builder.ConnectionString;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => {
            web.UseEnvironment("Testing");
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:TMusic"] = ConnectionString,
                ["Storage:CoversPath"] = CoversPath,
                ["Storage:AudioPath"] = AudioPath,
                ["Auth:SigningKey"] = "INTEGRATION-TEST-ONLY-NONPRODUCTION-KEY-2026-11-17",
                ["Auth:Jwt:SigningKey"] = "INTEGRATION-TEST-ONLY-NONPRODUCTION-KEY-2026-11-17",
                ["RateLimits:AdminAuthPermitLimit"] = "1000",
                ["RateLimits:AdminApiPermitLimit"] = "1000",
                ["RateLimits:AuthPermitLimit"] = "1000",
                ["RateLimits:ProviderPermitLimit"] = "1000",
                ["YouTube:ApiKey"] = "fixture-only",
                ["Features:EnableTikTokVideoImport"] = "true",
                ["Features:EnableCrossProviderMixedCollections"] = "false",
                ["Features:EnableMultiYouTubePlaylistMerge"] = "false",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
            web.ConfigureTestServices(services => {
                services.RemoveAll<TMusicDb>();
                services.RemoveAll<DbContextOptions<TMusicDb>>();
                services.AddDbContext<TMusicDb>(options => options.UseSqlServer(ConnectionString));
                services.RemoveAll<IAccountMailSender>();
                services.AddSingleton<IAccountMailSender>(Mail);
                services.RemoveAll<ISourceResolver>();
                services.AddSingleton<ISourceResolver, FixtureResolver>();
            });
        });
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TMusicDb>().Database.MigrateAsync();
    }

    public HttpClient Client(string? accessToken = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (accessToken is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
    public async Task<TestAccount> RegisterAsync(string? email = null)
    {
        email ??= $"test-{Guid.NewGuid():N}@tmusic.test";
        using var client = Client();
        var session = await Json(await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password, displayName = "Kiểm thử V2", deviceName = "SQL integration" }));
        return new(email, session.GetProperty("userId").GetGuid(), session.GetProperty("accessToken").GetString()!, session.GetProperty("refreshToken").GetString()!);
    }
    public static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {body}");
    }
    public static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
    public static async Task<HttpResponseMessage> Mutation(HttpClient client, HttpMethod method, string path, object? body = null, string? version = null)
    {
        using var request = new HttpRequestMessage(method,path);
        if (body is not null) request.Content=JsonContent.Create(body);
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request);
    }
    public async Task DisposeAsync()
    {
        if (Factory is null) return;
        // Never execute database deletion unless both the generated name and exact connection match.
        if (!DatabaseName.StartsWith("TMusicV2Test_", StringComparison.Ordinal) || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != DatabaseName)
            throw new InvalidOperationException("Unsafe test database cleanup rejected.");
        await using (var scope = Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TMusicDb>().Database.EnsureDeletedAsync();
        await Factory.DisposeAsync();
        if (Directory.Exists(CoversPath))
        {
            try { Directory.Delete(CoversPath, recursive: true); } catch (IOException) { }
        }
        if (Directory.Exists(AudioPath))
        {
            try { Directory.Delete(AudioPath, recursive: true); } catch (IOException) { }
        }
    }
}
public sealed record TestAccount(string Email, Guid UserId, string AccessToken, string RefreshToken);
public sealed class CaptureMail : IAccountMailSender
{
    private readonly ConcurrentDictionary<(string,AccountCodePurpose),string> messages = new();
    public bool IsConfigured => true;
    public Task SendCodeAsync(string address, AccountCodePurpose purpose, string code, CancellationToken cancellationToken)
    { messages[(address,purpose)]=code; return Task.CompletedTask; }
    public string Get(string address,AccountCodePurpose purpose) => messages[(address,purpose)];
}
public sealed class FixtureResolver : ISourceResolver
{
    public Task<ResolvedSource> ResolveAsync(string input,CancellationToken cancellationToken)
    {
        var source=SourceUrl.ParseVideo(input) ?? throw new ProviderException("INVALID_SOURCE_URL");
        return Task.FromResult(new ResolvedSource(source.Provider,source.Id,input,source.CanonicalUrl,
            "Fixture title; not live metadata","Fixture creator",null,120,"EXTERNAL_OPEN_ONLY",DateTimeOffset.UtcNow,"TEST_FIXTURE_ONLY"));
    }
}

