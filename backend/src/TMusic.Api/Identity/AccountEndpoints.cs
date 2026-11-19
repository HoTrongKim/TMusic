using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Application.Identity;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;
namespace TMusic.Api.Identity;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
        auth.MapPost("/forgot-password", SendResetCode);
        auth.MapPost("/reset-password", Reset);
        auth.MapPost("/change-password", ChangePassword).RequireAuthorization();
        auth.MapPost("/refresh", async (RefreshRequest r, SessionService s, CancellationToken ct) =>
        {
            if (!s.IsConfigured) return ApiSupport.Problem("AUTH_UNAVAILABLE", 503);
            if (r.RefreshToken is not { Length: 64 }) return ApiSupport.Problem("SESSION_EXPIRED", 401);
            var result = await s.Rotate(r.RefreshToken, ct);
            return result is null ? ApiSupport.Problem("SESSION_EXPIRED", 401) : Results.Ok(result);
        });
        auth.MapPost("/logout", async (ClaimsPrincipal u, SessionService s, CancellationToken ct) =>
        {
            await s.Revoke(ApiSupport.UserId(u), Guid.Parse(u.FindFirstValue("sid")!), ct);
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapPost("/logout-all", async (ClaimsPrincipal u, SessionService s, CancellationToken ct) =>
        {
            await s.Revoke(ApiSupport.UserId(u), null, ct);
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapGet("/sessions", Sessions).RequireAuthorization();
        auth.MapDelete("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal u, SessionService s, CancellationToken ct) =>
        {
            await s.Revoke(ApiSupport.UserId(u), id, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        var me = api.MapGroup("/users/me").RequireAuthorization();
        me.MapGet("", Profile);
        me.MapPatch("", UpdateProfile);
        me.MapDelete("", DeleteAccount);
        me.MapGet("/settings", GetSettings);
        me.MapPatch("/settings", UpdateSettings);
    }

    private static async Task<IResult> Register(RegisterRequest r, UserManager<Account> users, TMusicDb db, SessionService sessions, TimeProvider clock, CancellationToken ct)
    {
        if (!ValidEmail(r.Email) || !ValidPassword(r.Password) || r.DisplayName?.Trim().Length is > 100)
            return ApiSupport.Problem("INVALID_REGISTRATION", 400, "Email hợp lệ và mật khẩu 12–128 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.");
        if (!sessions.IsConfigured)
            return ApiSupport.Problem("AUTH_UNAVAILABLE", 503);

        var now = clock.GetUtcNow();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = r.Email!.Trim(),
            UserName = r.Email.Trim(),
            DisplayName = r.DisplayName?.Trim() ?? "",
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(account, r.Password!);
        }
        catch (DbUpdateException e) when (ApiSupport.IsUnique(e))
        {
            return ApiSupport.Problem("EMAIL_ALREADY_REGISTERED", 409, "Email này đã được sử dụng. Hãy đăng nhập hoặc chọn Quên mật khẩu.");
        }

        if (!result.Succeeded)
        {
            return result.Errors.All(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                ? ApiSupport.Problem("EMAIL_ALREADY_REGISTERED", 409, "Email này đã được sử dụng. Hãy đăng nhập hoặc chọn Quên mật khẩu.")
                : ApiSupport.Problem("INVALID_REGISTRATION");
        }

        var session = await sessions.Create(account.Id, r.DeviceName, ct);
        await tx.CommitAsync(ct);
        return Results.Created("/api/v1/users/me", session);
    }

    private static async Task<IResult> Login(LoginRequest r, UserManager<Account> users, SignInManager<Account> signIn, IPasswordHasher<Account> hash, SessionService sessions, TimeProvider clock, CancellationToken ct)
    {
        if (!ValidEmail(r.Email) || r.Password is not { Length: > 0 and <= 128 }) return ApiSupport.Problem("INVALID_CREDENTIALS", 401);
        if (!sessions.IsConfigured) return ApiSupport.Problem("AUTH_UNAVAILABLE", 503);
        var account = await users.FindByEmailAsync(r.Email!.Trim());
        if (account is null || account.DeletedAt is not null)
        {
            hash.HashPassword(new Account(), r.Password);
            return ApiSupport.Problem("INVALID_CREDENTIALS", 401);
        }
        var result = await signIn.CheckPasswordSignInAsync(account, r.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return ApiSupport.Problem("INVALID_CREDENTIALS", 401, "Email hoặc mật khẩu chưa đúng, hoặc tài khoản đang tạm khóa.");
        if (account.IsCurrentlySuspended(clock.GetUtcNow()))
            return ApiSupport.Problem("ACCOUNT_SUSPENDED", 403, "Tài khoản đang bị tạm khóa.");
        return Results.Ok(await sessions.Create(account.Id, r.DeviceName, ct));
    }

    private static async Task<IResult> SendResetCode(EmailRequest r, UserManager<Account> users, IAccountMailSender mail, CancellationToken ct)
    {
        if (!ValidEmail(r.Email)) return ApiSupport.Problem("INVALID_EMAIL");
        if (!mail.IsConfigured) return ApiSupport.Problem("MAIL_UNAVAILABLE", 503, "Chưa cấu hình dịch vụ email khôi phục mật khẩu.");
        var account = await users.FindByEmailAsync(r.Email!.Trim());
        if (account is null) return Results.Accepted();
        var code = await users.GeneratePasswordResetTokenAsync(account);
        try
        {
            await mail.SendCodeAsync(account.Email!, AccountCodePurpose.ResetPassword, code, ct);
        }
        catch (SmtpException)
        {
            // Avoid disclosing account existence on delivery transient errors.
        }
        return Results.Accepted();
    }

    private static async Task<IResult> Reset(ResetRequest r, UserManager<Account> users, TMusicDb db, SessionService sessions, TimeProvider clock, CancellationToken ct)
    {
        if (!ValidEmail(r.Email) || !ValidCode(r.Code) || !ValidPassword(r.Password)) return ApiSupport.Problem("INVALID_RESET");
        var account = await users.FindByEmailAsync(r.Email!.Trim());
        if (account is null) return ApiSupport.Problem("INVALID_RESET");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!(await users.ResetPasswordAsync(account, r.Code!, r.Password!)).Succeeded) return ApiSupport.Problem("INVALID_RESET");
        if (!account.EmailConfirmed)
        {
            account.EmailConfirmed = true;
            account.UpdatedAt = clock.GetUtcNow();
            await users.UpdateAsync(account);
        }
        await sessions.Revoke(account.Id, null, ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest r, ClaimsPrincipal principal, UserManager<Account> users, TMusicDb db, SessionService sessions, CancellationToken ct)
    {
        if (!ValidPassword(r.NewPassword) || r.CurrentPassword is not { Length: > 0 and <= 128 }) return ApiSupport.Problem("INVALID_PASSWORD");
        var account = await users.FindByIdAsync(ApiSupport.UserId(principal).ToString());
        if (account is null) return Results.Unauthorized();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!(await users.ChangePasswordAsync(account, r.CurrentPassword, r.NewPassword!)).Succeeded) return ApiSupport.Problem("INVALID_PASSWORD");
        await sessions.Revoke(account.Id, null, ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Sessions(ClaimsPrincipal u, TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var family = Guid.Parse(u.FindFirstValue("sid")!);
        var now = clock.GetUtcNow();
        var items = await db.UserSessions.AsNoTracking().Where(s => s.UserId == user && s.RotatedAt == null && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.CreatedAt).Select(s => new { id = s.FamilyId, s.DeviceName, s.CreatedAt, s.ExpiresAt, isCurrent = s.FamilyId == family }).ToListAsync(ct);
        return Results.Ok(new { items, total = items.Count, page = 1, pageSize = items.Count });
    }

    private static async Task<IResult> Profile(ClaimsPrincipal principal, UserManager<Account> users, TMusicDb db, CancellationToken ct)
    {
        var account = await users.FindByIdAsync(ApiSupport.UserId(principal).ToString());
        if (account is null) return Results.NotFound();
        var providers = (await users.GetLoginsAsync(account)).Select(p => p.LoginProvider).ToList();
        if (await users.HasPasswordAsync(account)) providers.Insert(0, "password");
        var linked = await db.UserAuthProviders.AsNoTracking().Where(x => x.UserId == account.Id)
            .OrderBy(x => x.LinkedAt).Select(x => new { x.Provider, x.LinkedAt }).ToListAsync(ct);
        return Results.Ok(new { account.Id, account.Email, account.DisplayName, account.AvatarUrl, emailVerified = account.EmailConfirmed, account.CreatedAt, authProviders = providers, linkedProviders = linked });
    }

    private static async Task<IResult> UpdateProfile(ProfileRequest r, ClaimsPrincipal principal, UserManager<Account> users, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        if (r.DisplayName?.Trim() is not { Length: > 0 and <= 100 } || !ValidAvatar(r.AvatarUrl)) return ApiSupport.Problem("INVALID_DISPLAY_NAME");
        var account = await users.FindByIdAsync(ApiSupport.UserId(principal).ToString());
        if (account is null) return Results.NotFound();
        account.DisplayName = r.DisplayName.Trim();
        if (r.AvatarUrl is not null) account.AvatarUrl = r.AvatarUrl.Length == 0 ? null : r.AvatarUrl;
        account.UpdatedAt = time.GetUtcNow();
        return (await users.UpdateAsync(account)).Succeeded ? await Profile(principal, users, db, ct) : ApiSupport.Problem("CONCURRENT_MODIFICATION", 409);
    }

    private static async Task<IResult> DeleteAccount(HttpRequest request, ClaimsPrincipal principal, UserManager<Account> users, SignInManager<Account> signIn, TMusicDb db, TMusic.Api.Admin.AdminPermissionService adminPerms, AppleTokens apple, ICoverImageStorage covers, IAudioStorage audio, TimeProvider clock, CancellationToken ct)
    {
        var account = await users.FindByIdAsync(ApiSupport.UserId(principal).ToString());
        if (account is null) return Results.NoContent();
        if (await adminPerms.IsLastSuperAdminAsync(account.Id, ct))
            return ApiSupport.Problem("LAST_SUPERADMIN_PROTECTED", 409, "Không thể tự xóa tài khoản SuperAdmin cuối cùng của hệ thống.");
        DeleteRequest? body = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ? await request.ReadFromJsonAsync<DeleteRequest>(ct) : null;
        if (await users.HasPasswordAsync(account))
        {
            if (body?.Password is not { Length: > 0 and <= 128 } password || !(await signIn.CheckPasswordSignInAsync(account, password, true)).Succeeded)
                return ApiSupport.Problem("REAUTHENTICATION_REQUIRED", 401, "Xác nhận mật khẩu để xóa tài khoản.");
        }
        else if (!long.TryParse(principal.FindFirstValue("auth_time"), out var authenticated) ||
            clock.GetUtcNow().ToUnixTimeSeconds() - authenticated > 600)
            return ApiSupport.Problem("REAUTHENTICATION_REQUIRED", 401, "Hãy đăng nhập lại trước khi xóa tài khoản.");
        if ((await users.GetLoginsAsync(account)).Any(x => x.LoginProvider == "apple") && !await apple.Revoke(account, users, ct))
            return ApiSupport.Problem("APPLE_REVOCATION_UNAVAILABLE", 503, "Chưa thể thu hồi liên kết Apple. Hãy thử lại.");
        var deletedId = account.Id;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AlbumSongs.Where(x => x.UserId == account.Id).ExecuteDeleteAsync(ct);
        var result = await users.DeleteAsync(account);
        if (!result.Succeeded) return ApiSupport.Problem("CONCURRENT_MODIFICATION", 409);
        await tx.CommitAsync(ct);
        covers.DeleteUserCovers(deletedId);
        audio.DeleteUserAudio(deletedId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSettings(ClaimsPrincipal u, TMusicDb db, CancellationToken ct)
    {
        var id = ApiSupport.UserId(u);
        var value = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == id, ct) ?? new UserSettings { UserId = id };
        return Results.Ok(new { value.Theme, value.Language, value.NotificationsEnabled });
    }

    private static async Task<IResult> UpdateSettings(SettingsRequest r, ClaimsPrincipal u, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        if (r.Theme is not ("system" or "light" or "dark") || r.Language is not ("vi" or "en") || r.NotificationsEnabled)
            return ApiSupport.Problem("INVALID_SETTINGS", 400, "Thông báo đẩy chưa được hỗ trợ.");
        var id = ApiSupport.UserId(u);
        var value = await db.UserSettings.SingleOrDefaultAsync(s => s.UserId == id, ct);
        if (value is null) { value = new UserSettings { UserId = id }; db.UserSettings.Add(value); }
        value.Theme = r.Theme;
        value.Language = r.Language;
        value.NotificationsEnabled = false;
        value.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { value.Theme, value.Language, value.NotificationsEnabled });
    }

    internal static bool ValidEmail(string? email) => email?.Trim() is { Length: > 0 and <= 254 } text && MailAddress.TryCreate(text, out var address) && address.Address == text;
    internal static bool ValidPassword(string? value) => value is { Length: >= 12 and <= 128 } && value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit) && value.Any(c => !char.IsLetterOrDigit(c));
    private static bool ValidCode(string? code) => code is { Length: > 0 and <= 2048 };
    private static bool ValidAvatar(string? avatar) => avatar is null or "" or "avatar:navy" or "avatar:blue" or "avatar:green" or "avatar:terracotta" or "avatar:slate" or "avatar:amber";
    public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName, string? DeviceName = null);
    public sealed record LoginRequest(string? Email, string? Password, string? DeviceName);
    public sealed record EmailRequest(string? Email);
    public sealed record ResetRequest(string? Email, string? Code, string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
    public sealed record RefreshRequest(string? RefreshToken);
    public sealed record ProfileRequest(string? DisplayName, string? AvatarUrl = null);
    public sealed record DeleteRequest(string? Password);
    public sealed record SettingsRequest(string Theme, string Language, bool NotificationsEnabled);
}
