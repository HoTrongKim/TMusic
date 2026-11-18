using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace TMusic.Tests;

public sealed class PublicApiContractTests
{
    [Fact]
    public async Task HealthAndOpenApiWorkWithoutDatabaseOrCredentialsButLibraryRemainsProtected()
    {
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(web=> {
            web.UseEnvironment("Testing");
            web.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:TMusic"]="Server=127.0.0.1,1;Database=NeverUsed;Integrated Security=True;Connect Timeout=1;Encrypt=True",
                ["Auth:SigningKey"]="CONTRACT-TEST-ONLY-KEY-THIRTYTWOCHARS-PLUS",
                ["Auth:Google:Audiences"]="",["Auth:Apple:Audiences"]="",["Mail:Host"]="",["Mail:From"]=""
            }));
        });
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health")).StatusCode);
        var schema=await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/auth/register",schema); Assert.Contains("/api/v1/library/songs",schema); Assert.Contains("/api/v1/albums/{id}/songs/order",schema);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/library/songs")).StatusCode);
        var capabilities=await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/system/capabilities");
        Assert.False(capabilities.GetProperty("googleSignIn").GetBoolean());
        Assert.False(capabilities.GetProperty("appleSignIn").GetBoolean());
        Assert.False(capabilities.GetProperty("multiPlaylistMerge").GetBoolean());
        Assert.False(capabilities.GetProperty("licensedAudioDownload").GetBoolean());
    }
}
