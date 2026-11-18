using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using TMusic.Infrastructure.Persistence;
namespace TMusic.Api.Identity;

public sealed class AppleTokens(IConfiguration config,IHttpClientFactory clients,IDataProtectionProvider protection,IExternalTokenVerifier verifier)
{
    private readonly IDataProtector protector=protection.CreateProtector("TMusic.V2.AppleRefreshToken");
    public static bool Configured(IConfiguration c) => c["Auth:Apple:TeamId"] is { Length: >0 } &&
        c["Auth:Apple:KeyId"] is { Length: >0 } && c["Auth:Apple:PrivateKeyPath"] is { Length: >0 } path && File.Exists(path);
    public async Task<string?> Exchange(string? code,ExternalUser identity,CancellationToken ct)
    {
        if (!Configured(config) || code is not { Length: >0 and <=4096 }) return null;
        using var response=await clients.CreateClient("apple").PostAsync("https://appleid.apple.com/auth/token",new FormUrlEncodedContent(new Dictionary<string,string>
        { ["client_id"]=identity.Audience,["client_secret"]=ClientSecret(identity.Audience),["grant_type"]="authorization_code",["code"]=code }),ct);
        if (!response.IsSuccessStatusCode) return null;
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!json.TryGetProperty("id_token",out var token) || !json.TryGetProperty("refresh_token",out var refresh)) return null;
        var verified=await verifier.Verify("apple",token.GetString()!,ct);
        if (verified?.Subject != identity.Subject || verified.Audience != identity.Audience) return null;
        return protector.Protect(JsonSerializer.Serialize(new StoredToken(identity.Audience,refresh.GetString()!)));
    }
    public async Task<bool> Revoke(Account account,UserManager<Account> users,CancellationToken ct)
    {
        var encoded=await users.GetAuthenticationTokenAsync(account,"apple","refresh");
        if (encoded is null || !Configured(config)) return false;
        StoredToken token;
        try { token=JsonSerializer.Deserialize<StoredToken>(protector.Unprotect(encoded))!; }
        catch (CryptographicException) { return false; }
        using var response=await clients.CreateClient("apple").PostAsync("https://appleid.apple.com/auth/revoke",new FormUrlEncodedContent(new Dictionary<string,string>
        { ["client_id"]=token.ClientId,["client_secret"]=ClientSecret(token.ClientId),["token"]=token.RefreshToken,["token_type_hint"]="refresh_token" }),ct);
        return response.IsSuccessStatusCode;
    }
    private string ClientSecret(string client)
    {
        using var key=ECDsa.Create(); key.ImportFromPem(File.ReadAllText(config["Auth:Apple:PrivateKeyPath"]!));
        var now=DateTime.UtcNow;
        var token=new JwtSecurityToken(config["Auth:Apple:TeamId"],"https://appleid.apple.com",[new Claim("sub",client)],now,now.AddMinutes(5),
            new SigningCredentials(new ECDsaSecurityKey(key) { KeyId=config["Auth:Apple:KeyId"] },SecurityAlgorithms.EcdsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private sealed record StoredToken(string ClientId,string RefreshToken);
}

