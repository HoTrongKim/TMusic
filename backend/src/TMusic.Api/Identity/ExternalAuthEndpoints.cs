using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Infrastructure.Persistence;
namespace TMusic.Api.Identity;

public static class ExternalAuthEndpoints
{
    public static void MapExternalAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth=api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.MapPost("/apple/challenge",(IExternalTokenVerifier v,TMusicDb db,TimeProvider t,CancellationToken ct)=>Challenge(v,db,t,null,ct));
        auth.MapPost("/links/apple/challenge",(ClaimsPrincipal u,IExternalTokenVerifier v,TMusicDb db,TimeProvider t,CancellationToken ct)=>Challenge(v,db,t,ApiSupport.UserId(u),ct)).RequireAuthorization();
        auth.MapPost("/google",(ExternalRequest r,IExternalTokenVerifier v,UserManager<Account> u,TMusicDb db,SessionService s,AppleTokens a,TimeProvider t,CancellationToken ct)=>SignIn("google",r,v,u,db,s,a,t,ct));
        auth.MapPost("/apple",(ExternalRequest r,IExternalTokenVerifier v,UserManager<Account> u,TMusicDb db,SessionService s,AppleTokens a,TimeProvider t,CancellationToken ct)=>SignIn("apple",r,v,u,db,s,a,t,ct));
        auth.MapPost("/links/{provider}",Link).RequireAuthorization();
    }
    private static async Task<IResult> Challenge(IExternalTokenVerifier verifier,TMusicDb db,TimeProvider clock,Guid? user,CancellationToken ct)
    {
        if (!verifier.Available("apple")) return ApiSupport.Problem("SOCIAL_AUTH_UNAVAILABLE",503);
        var nonce=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.ExternalChallenges.Add(new ExternalChallenge { Id=ApiSupport.Hash(nonce),UserId=user,ExpiresAt=clock.GetUtcNow().AddMinutes(5) });
        await db.SaveChangesAsync(ct); return Results.Ok(new { nonce });
    }
    private static async Task<IResult> SignIn(string provider,ExternalRequest r,IExternalTokenVerifier verifier,UserManager<Account> users,TMusicDb db,SessionService sessions,AppleTokens apple,TimeProvider clock,CancellationToken ct)
    {
        if (!verifier.Available(provider) || !sessions.IsConfigured) return ApiSupport.Problem("SOCIAL_AUTH_UNAVAILABLE",503);
        var identity=await Verify(provider,r,verifier,ct);
        if (identity is null || !await Redeem(provider,identity,null,db,clock,ct)) return ApiSupport.Problem("INVALID_EXTERNAL_TOKEN",401);
        string? encrypted=null;
        if (provider=="apple")
        {
            encrypted=await apple.Exchange(r.AuthorizationCode,identity,ct);
            if (encrypted is null) return ApiSupport.Problem("APPLE_AUTHORIZATION_FAILED",401);
        }
        var account=await users.FindByLoginAsync(provider,identity.Subject);
        if (account is not null)
        {
            if (account.DeletedAt is not null || account.IsCurrentlySuspended(clock.GetUtcNow()) || await users.IsLockedOutAsync(account)) return ApiSupport.Problem("INVALID_EXTERNAL_TOKEN",401);
            if (!await db.UserAuthProviders.AnyAsync(x=>x.Provider==provider && x.ProviderSubject==identity.Subject,ct))
                db.UserAuthProviders.Add(new UserAuthProvider { UserId=account.Id,Provider=provider,ProviderSubject=identity.Subject,LinkedAt=clock.GetUtcNow() });
            if (encrypted is not null) await users.SetAuthenticationTokenAsync(account,"apple","refresh",encrypted);
            return Results.Ok(await sessions.Create(account.Id,r.DeviceName,ct));
        }
        if (!identity.EmailVerified || !AccountEndpoints.ValidEmail(identity.Email)) return ApiSupport.Problem("PROVIDER_EMAIL_REQUIRED",422,"Đăng ký email trước, sau đó liên kết nhà cung cấp trong Bảo mật.");
        if (await users.FindByEmailAsync(identity.Email!) is not null) return ApiSupport.Problem("ACCOUNT_LINK_PROOF_REQUIRED",409,"Đăng nhập tài khoản hiện có để liên kết.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now=clock.GetUtcNow();
            account=new Account { Id=Guid.NewGuid(),Email=identity.Email,UserName=identity.Email,EmailConfirmed=true,
                DisplayName=r.DisplayName?.Trim() is { Length: >0 and <=100 } name ? name : "",CreatedAt=now,UpdatedAt=now };
            if (!(await users.CreateAsync(account)).Succeeded || !(await users.AddLoginAsync(account,new UserLoginInfo(provider,identity.Subject,provider))).Succeeded)
                return ApiSupport.Problem("ACCOUNT_LINK_PROOF_REQUIRED",409);
            db.UserAuthProviders.Add(new UserAuthProvider { UserId=account.Id,Provider=provider,ProviderSubject=identity.Subject,LinkedAt=now });
            if (encrypted is not null) await users.SetAuthenticationTokenAsync(account,"apple","refresh",encrypted);
            var result=await sessions.Create(account.Id,r.DeviceName,ct); await tx.CommitAsync(ct); return Results.Ok(result);
        }
        catch (DbUpdateException e) when (ApiSupport.IsUnique(e)) { return ApiSupport.Problem("ACCOUNT_LINK_PROOF_REQUIRED",409); }
    }
    private static async Task<IResult> Link(string provider,ExternalRequest r,ClaimsPrincipal principal,IExternalTokenVerifier verifier,UserManager<Account> users,SignInManager<Account> signIn,TMusicDb db,AppleTokens apple,TimeProvider time,CancellationToken ct)
    {
        if (!verifier.Available(provider)) return ApiSupport.Problem("SOCIAL_AUTH_UNAVAILABLE",503);
        var account=await users.FindByIdAsync(ApiSupport.UserId(principal).ToString());
        if (account is null || r.Password is not { Length: >0 and <=128 } password || !(await signIn.CheckPasswordSignInAsync(account,password,true)).Succeeded)
            return ApiSupport.Problem("ACCOUNT_LINK_PROOF_REQUIRED",401);
        var identity=await Verify(provider,r,verifier,ct);
        if (identity is null || !await Redeem(provider,identity,account.Id,db,time,ct)) return ApiSupport.Problem("INVALID_EXTERNAL_TOKEN",401);
        var linked=await users.FindByLoginAsync(provider,identity.Subject);
        if (linked is not null) return linked.Id==account.Id ? Results.NoContent() : ApiSupport.Problem("EXTERNAL_LOGIN_ALREADY_LINKED",409);
        string? encrypted=null;
        if (provider=="apple") { encrypted=await apple.Exchange(r.AuthorizationCode,identity,ct); if (encrypted is null) return ApiSupport.Problem("APPLE_AUTHORIZATION_FAILED",401); }
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (!(await users.AddLoginAsync(account,new UserLoginInfo(provider,identity.Subject,provider))).Succeeded) return ApiSupport.Problem("EXTERNAL_LOGIN_ALREADY_LINKED",409);
            db.UserAuthProviders.Add(new UserAuthProvider { UserId=account.Id,Provider=provider,ProviderSubject=identity.Subject,LinkedAt=time.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            if (encrypted is not null) await users.SetAuthenticationTokenAsync(account,"apple","refresh",encrypted);
            await tx.CommitAsync(ct); return Results.NoContent();
        }
        catch (DbUpdateException e) when (ApiSupport.IsUnique(e)) { return ApiSupport.Problem("EXTERNAL_LOGIN_ALREADY_LINKED",409); }
    }
    private static async Task<ExternalUser?> Verify(string provider,ExternalRequest r,IExternalTokenVerifier verifier,CancellationToken ct)
    {
        if (r.IdToken is not { Length: >0 and <=16384 } token) return null;
        try { return await verifier.Verify(provider,token,ct); }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or ArgumentException) { return null; }
    }
    private static async Task<bool> Redeem(string provider,ExternalUser user,Guid? owner,TMusicDb db,TimeProvider time,CancellationToken ct)
    {
        if (provider!="apple") return true;
        if (user.Nonce is not { Length: 43 } nonce) return false;
        var hash=ApiSupport.Hash(nonce); var now=time.GetUtcNow();
        return await db.ExternalChallenges.Where(x=>x.Id==hash && x.UserId==owner && x.ExpiresAt>now).ExecuteDeleteAsync(ct)==1;
    }
    public sealed record ExternalRequest(string? IdToken,string? DeviceName,string? DisplayName,string? Password,string? AuthorizationCode);
}

