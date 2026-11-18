using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Api.Identity;
using TMusic.Infrastructure.Persistence;
using Xunit;
using static TMusic.Tests.SqlApiFixture;
namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class ExternalAccountLinkTests(SqlApiFixture fixture)
{
    // This suite tests local identity/link/session behavior with an explicitly fake token verifier.
    // Cryptographic token checks are covered by ExternalTokenSecurityTests; real provider sign-in requires credentials.
    [SqlFact]
    public async Task MatchingEmailRequiresPasswordProofAndAProviderSubjectCannotLinkToTwoUsers()
    {
        var alice=await fixture.RegisterAsync(); var bob=await fixture.RegisterAsync();
        var fake=new FixtureExternalVerifier(new ExternalUser("google-subject-"+Guid.NewGuid().ToString("N"),alice.Email,true,null,"fixture-audience"));
        await using var factory=fixture.Factory.WithWebHostBuilder(web=>web.ConfigureTestServices(services=> {
            services.RemoveAll<IExternalTokenVerifier>(); services.AddSingleton<IExternalTokenVerifier>(fake);
        }));
        using var anonymous=factory.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost")});
        await Expect(await anonymous.PostAsJsonAsync("/api/v1/auth/google",new{idToken="fixture-token"}),HttpStatusCode.Conflict);
        anonymous.DefaultRequestHeaders.Authorization=new("Bearer",alice.AccessToken);
        await Expect(await anonymous.PostAsJsonAsync("/api/v1/auth/links/google",new{idToken="fixture-token",password="incorrect"}),HttpStatusCode.Unauthorized);
        await Expect(await anonymous.PostAsJsonAsync("/api/v1/auth/links/google",new{idToken="fixture-token",password=Password}),HttpStatusCode.NoContent);
        anonymous.DefaultRequestHeaders.Authorization=new("Bearer",bob.AccessToken);
        await Expect(await anonymous.PostAsJsonAsync("/api/v1/auth/links/google",new{idToken="fixture-token",password=Password}),HttpStatusCode.Conflict);
        anonymous.DefaultRequestHeaders.Authorization=null;
        var session=await Json(await anonymous.PostAsJsonAsync("/api/v1/auth/google",new{idToken="fixture-token",deviceName="External test"}));
        Assert.Equal(alice.UserId,session.GetProperty("userId").GetGuid());
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<TMusicDb>();
        Assert.Equal(1,await db.UserLogins.CountAsync(x=>x.ProviderKey==fake.Identity.Subject));
    }
    [SqlFact]
    public async Task VerifiedProviderCreatesInternalAccountWhileUnverifiedEmailIsRejected()
    {
        var fake=new FixtureExternalVerifier(new ExternalUser("google-subject-"+Guid.NewGuid().ToString("N"),$"external-{Guid.NewGuid():N}@gmail.com",true,null,"fixture-audience"));
        await using var factory=fixture.Factory.WithWebHostBuilder(web=>web.ConfigureTestServices(services=> {
            services.RemoveAll<IExternalTokenVerifier>(); services.AddSingleton<IExternalTokenVerifier>(fake);
        }));
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost")});
        var session=await Json(await client.PostAsJsonAsync("/api/v1/auth/google",new{idToken="fixture-token",displayName="Người dùng nhà cung cấp"}));
        client.DefaultRequestHeaders.Authorization=new("Bearer",session.GetProperty("accessToken").GetString());
        var profile=await Json(await client.GetAsync("/api/v1/users/me")); Assert.True(profile.GetProperty("emailVerified").GetBoolean());
        Assert.Contains("google",profile.GetProperty("authProviders").ToString());
        fake.Identity=new ExternalUser("another-subject",$"unverified-{Guid.NewGuid():N}@gmail.com",false,null,"fixture-audience");
        client.DefaultRequestHeaders.Authorization=null;
        await Expect(await client.PostAsJsonAsync("/api/v1/auth/google",new{idToken="fixture-token"}),(HttpStatusCode)422);
    }
    private sealed class FixtureExternalVerifier(ExternalUser identity) : IExternalTokenVerifier
    {
        public ExternalUser Identity {get;set;}=identity;
        public bool Available(string provider)=>provider=="google";
        public Task<ExternalUser?> Verify(string provider,string token,CancellationToken ct)=>Task.FromResult<ExternalUser?>(token=="fixture-token"?Identity:null);
    }
}
