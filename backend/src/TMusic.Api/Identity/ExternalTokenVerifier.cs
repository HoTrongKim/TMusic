using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
namespace TMusic.Api.Identity;

public sealed record ExternalUser(string Subject,string? Email,bool EmailVerified,string? Nonce,string Audience);
public interface IExternalTokenVerifier
{
    bool Available(string provider);
    Task<ExternalUser?> Verify(string provider,string token,CancellationToken ct);
}
public sealed class ExternalTokenVerifier(IConfiguration config) : IExternalTokenVerifier
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> google = Metadata("https://accounts.google.com/.well-known/openid-configuration");
    private readonly ConfigurationManager<OpenIdConnectConfiguration> apple = Metadata("https://appleid.apple.com/.well-known/openid-configuration");
    public bool Available(string provider) => ConfiguredAudiences(config, provider).Length > 0 && (provider != "apple" ||
        config.GetValue("TMUSIC_ENABLE_APPLE_SIGN_IN", false) && AppleTokens.Configured(config));

    public static string[] ConfiguredAudiences(IConfiguration config, string provider) => provider switch
    {
        "google" => new[]
            {
                config["Auth:Google:Audiences"],
                config["Auth:google:Audiences"],
                config["TMUSIC_GOOGLE_SERVER_CLIENT_ID"],
                config["TMUSIC_GOOGLE_WEB_CLIENT_ID"],
                config["TMUSIC_GOOGLE_ANDROID_CLIENT_ID"],
                config["TMUSIC_GOOGLE_IOS_CLIENT_ID"]
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => x!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(NormalizeGoogleClientId)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
        "apple" => (config["Auth:Apple:Audiences"] ?? config["Auth:apple:Audiences"] ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
        _ => []
    };

    public static string NormalizeGoogleClientId(string raw)
    {
        var s = raw.Trim().TrimEnd('\\', '/').Trim();
        while (s.EndsWith(".apps.googleusercontent.comcom", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^3];
        }
        return s;
    }

    public async Task<ExternalUser?> Verify(string provider,string token,CancellationToken ct)
    {
        if (!Available(provider) || token.Length is 0 or > 16384) return null;
        var manager = provider == "apple" ? apple : google;
        var metadata = await manager.GetConfigurationAsync(ct);
        var parameters = ValidationParameters(provider,ConfiguredAudiences(config, provider),metadata.SigningKeys);
        var result = await new JsonWebTokenHandler { MapInboundClaims=false,MaximumTokenSizeInBytes=16384 }.ValidateTokenAsync(token,parameters);
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException) manager.RequestRefresh();
        if (!result.IsValid) return null;
        var claims=result.ClaimsIdentity!; var sub=claims.FindFirst("sub")?.Value; var email=claims.FindFirst("email")?.Value;
        if (sub is not { Length: >0 and <=128 }) return null;
        var verified=string.Equals(claims.FindFirst("email_verified")?.Value,"true",StringComparison.OrdinalIgnoreCase);
        var hd=claims.FindFirst("hd")?.Value;
        var authoritative=provider == "apple" || email?.EndsWith("@gmail.com",StringComparison.OrdinalIgnoreCase)==true ||
            (hd is { Length: >0 } && email?.EndsWith("@"+hd,StringComparison.OrdinalIgnoreCase)==true);
        return new(sub,email,verified && authoritative,claims.FindFirst("nonce")?.Value,claims.FindFirst("aud")!.Value);
    }
    public static TokenValidationParameters ValidationParameters(string provider,IEnumerable<string> audiences,IEnumerable<SecurityKey> keys) => new()
    {
        RequireSignedTokens=true,RequireExpirationTime=true,ValidateIssuerSigningKey=true,ValidateLifetime=true,
        ValidateIssuer=true,ValidateAudience=true,ValidAlgorithms=[SecurityAlgorithms.RsaSha256],
        ValidIssuers=provider == "apple" ? ["https://appleid.apple.com"] : ["https://accounts.google.com","accounts.google.com"],
        ValidAudiences=audiences,IssuerSigningKeys=keys,ClockSkew=TimeSpan.FromSeconds(15)
    };
    private static ConfigurationManager<OpenIdConnectConfiguration> Metadata(string url) => new(url,new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect=false,UseCookies=false }) { Timeout=TimeSpan.FromSeconds(8) }) { RequireHttps=true });
}

