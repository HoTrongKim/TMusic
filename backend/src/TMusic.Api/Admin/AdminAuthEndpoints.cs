using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Api.Identity;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Admin;

public static class AdminAuthEndpoints
{
    public static void MapAdminAuthEndpoints(this RouteGroupBuilder admin)
    {
        var auth = admin.MapGroup("/auth").RequireRateLimiting("admin-auth");
        auth.MapPost("/login", Login);
        auth.MapPost("/mfa/setup", SetupMfa);
        auth.MapPost("/mfa/verify", VerifyMfa);
        auth.MapPost("/refresh", Refresh);
        auth.MapGet("/invitations/inspect", InspectInvitation);
        auth.MapPost("/invitations/accept", AcceptInvitation);
        auth.MapPost("/invitations/complete-mfa", CompleteInvitationMfa);

        var protectedAuth = admin.MapGroup("/auth")
            .RequireAuthorization(AdminAuthConstants.AdminAccessPolicy)
            .AddEndpointFilter<AdminCsrfEndpointFilter>();
        protectedAuth.MapGet("/me", Me);
        protectedAuth.MapPost("/logout", Logout);
        protectedAuth.MapPost("/logout-all", LogoutAll);
        protectedAuth.MapPost("/mfa/recovery-codes", RegenerateRecoveryCodes);
    }

    private static async Task<IResult> Login(
        AdminLoginRequest req,
        UserManager<Account> users,
        SignInManager<Account> signIn,
        IPasswordHasher<Account> hasher,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        SessionService sessions,
        TMusicDb db,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!AccountEndpoints.ValidEmail(req.Email) || req.Password is not { Length: > 0 and <= 128 })
            return ApiSupport.Problem("INVALID_CREDENTIALS", 401, "Email hoặc mật khẩu chưa đúng.");
        if (!sessions.IsConfigured)
            return ApiSupport.Problem("AUTH_UNAVAILABLE", 503);

        var now = clock.GetUtcNow();
        var account = await users.FindByEmailAsync(req.Email!.Trim());
        if (account is null || account.DeletedAt is not null)
        {
            hasher.HashPassword(new Account(), req.Password);
            await audit.RecordSystemEventAsync("WARNING", "AUTH_FAILURE", "ADMIN_LOGIN_UNKNOWN", "Đăng nhập quản trị thất bại (tài khoản không tồn tại).", null, null, ct);
            return ApiSupport.Problem("INVALID_CREDENTIALS", 401, "Email hoặc mật khẩu chưa đúng.");
        }

        if (account.IsCurrentlySuspended(now))
        {
            await audit.RecordAuditAsync(account.Id, account.Email, null, "ADMIN_LOGIN", "ACCOUNT", account.Id.ToString("D"), "DENIED", "Tài khoản đang bị tạm khóa.", null, ct);
            return ApiSupport.Problem("ACCOUNT_SUSPENDED", 403, "Tài khoản quản trị đang bị tạm khóa.");
        }

        var check = await signIn.CheckPasswordSignInAsync(account, req.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            await audit.RecordSystemEventAsync("WARNING", "AUTH_FAILURE", check.IsLockedOut ? "ADMIN_LOCKED_OUT" : "ADMIN_INVALID_PASSWORD",
                "Đăng nhập quản trị thất bại do sai mật khẩu hoặc tài khoản đang bị khóa tạm.", account.Id, null, ct);
            return ApiSupport.Problem("INVALID_CREDENTIALS", 401, "Email hoặc mật khẩu chưa đúng, hoặc tài khoản đang tạm khóa.");
        }

        var roles = await permissionService.GetEffectiveRolesAsync(account.Id, ct);
        if (!roles.Any(AdminRoles.IsAdministrative))
        {
            await audit.RecordAuditAsync(account.Id, account.Email, roles.FirstOrDefault(), "ADMIN_LOGIN", "ACCOUNT", account.Id.ToString("D"), "DENIED", "Tài khoản không có vai trò quản trị.", null, ct);
            return ApiSupport.Problem("ADMIN_ACCESS_REQUIRED", 403, "Tài khoản này không có quyền truy cập TMusic Admin.");
        }

        var rawChallenge = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var purpose = account.TwoFactorEnabled ? "MFA_VERIFY" : "MFA_SETUP";
        db.AdminMfaChallenges.Add(new AdminMfaChallenge
        {
            Id = ApiSupport.Hash(rawChallenge),
            UserId = account.Id,
            Purpose = purpose,
            DeviceName = string.IsNullOrWhiteSpace(req.DeviceName) ? "TMusic Admin Web" : req.DeviceName.Trim()[..Math.Min(req.DeviceName.Trim().Length, 100)],
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(purpose == "MFA_SETUP" ? 15 : 5)
        });
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            status = purpose == "MFA_SETUP" ? "MFA_SETUP_REQUIRED" : "MFA_REQUIRED",
            challengeToken = rawChallenge,
            expiresAt = now.AddMinutes(purpose == "MFA_SETUP" ? 15 : 5)
        });
    }

    private static async Task<IResult> SetupMfa(
        MfaSetupRequest req,
        TMusicDb db,
        UserManager<Account> users,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ChallengeToken))
            return ApiSupport.Problem("INVALID_CHALLENGE", 400);

        var now = clock.GetUtcNow();
        var hash = ApiSupport.Hash(req.ChallengeToken.Trim());
        var challenge = await db.AdminMfaChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == hash && c.ConsumedAt == null && c.ExpiresAt > now, ct);
        if (challenge is null || challenge.Purpose != "MFA_SETUP")
            return ApiSupport.Problem("INVALID_CHALLENGE", 401, "Phiên thiết lập MFA đã hết hạn hoặc không hợp lệ.");

        var account = await users.FindByIdAsync(challenge.UserId.ToString());
        if (account is null || account.DeletedAt is not null || account.IsCurrentlySuspended(now))
            return ApiSupport.Problem("ACCOUNT_UNAVAILABLE", 403);

        var key = await users.GetAuthenticatorKeyAsync(account);
        if (string.IsNullOrWhiteSpace(key))
        {
            await users.ResetAuthenticatorKeyAsync(account);
            key = await users.GetAuthenticatorKeyAsync(account);
        }

        var email = account.Email ?? account.Id.ToString("D");
        var otpAuthUri = $"otpauth://totp/TMusic%20Admin:{UrlEncoder.Default.Encode(email)}?secret={key}&issuer=TMusic%20Admin&digits=6";
        return Results.Ok(new
        {
            sharedKey = FormatKey(key!),
            rawKey = key,
            otpAuthUri
        });
    }

    private static async Task<IResult> VerifyMfa(
        MfaVerifyRequest req,
        HttpContext http,
        TMusicDb db,
        UserManager<Account> users,
        AdminPermissionService permissionService,
        SessionService sessions,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ChallengeToken) || string.IsNullOrWhiteSpace(req.Code))
            return ApiSupport.Problem("INVALID_MFA_CODE", 400, "Vui lòng nhập mã xác thực MFA.");

        var now = clock.GetUtcNow();
        var hash = ApiSupport.Hash(req.ChallengeToken.Trim());
        var challenge = await db.AdminMfaChallenges
            .FirstOrDefaultAsync(c => c.Id == hash && c.ConsumedAt == null && c.ExpiresAt > now, ct);
        if (challenge is null)
            return ApiSupport.Problem("INVALID_CHALLENGE", 401, "Phiên xác thực MFA đã hết hạn. Vui lòng đăng nhập lại.");

        var account = await users.FindByIdAsync(challenge.UserId.ToString());
        if (account is null || account.DeletedAt is not null || account.IsCurrentlySuspended(now))
            return ApiSupport.Problem("ACCOUNT_UNAVAILABLE", 403);

        var roles = await permissionService.GetEffectiveRolesAsync(account.Id, ct);
        if (!roles.Any(AdminRoles.IsAdministrative))
            return ApiSupport.Problem("ADMIN_ACCESS_REQUIRED", 403);

        var cleanedCode = req.Code.Replace(" ", "").Replace("-", "").Trim();
        var verified = await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, cleanedCode);
        var usedRecovery = false;

        if (!verified && challenge.Purpose == "MFA_VERIFY")
        {
            var recoveryResult = await users.RedeemTwoFactorRecoveryCodeAsync(account, req.Code.Trim());
            verified = recoveryResult.Succeeded;
            usedRecovery = verified;
        }

        if (!verified)
        {
            await audit.RecordAuditAsync(account.Id, account.Email, roles.FirstOrDefault(), "ADMIN_MFA_VERIFY", "ACCOUNT", account.Id.ToString("D"), "FAILURE", "Mã MFA không hợp lệ.", null, ct);
            return ApiSupport.Problem("INVALID_MFA_CODE", 401, "Mã xác thực 6 chữ số hoặc mã khôi phục không đúng.");
        }

        IReadOnlyList<string>? recoveryCodes = null;
        if (challenge.Purpose == "MFA_SETUP" || !account.TwoFactorEnabled)
        {
            await users.SetTwoFactorEnabledAsync(account, true);
            recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))?.ToList() ?? [];
            await audit.RecordAuditAsync(account.Id, account.Email, roles.FirstOrDefault(), "ADMIN_MFA_ENABLED", "ACCOUNT", account.Id.ToString("D"), "SUCCESS", "Thiết lập TOTP MFA thành công.", null, ct);
        }

        challenge.ConsumedAt = now;
        var session = await sessions.CreateAdmin(account.Id, challenge.DeviceName, mfaVerified: true, ct);
        var csrf = SetAdminAuthCookies(http, session);
        var permissions = await permissionService.GetEffectivePermissionsAsync(account.Id, ct);

        await audit.RecordAuditAsync(account.Id, account.Email, roles.FirstOrDefault(), "ADMIN_LOGIN", "ACCOUNT", account.Id.ToString("D"), "SUCCESS",
            usedRecovery ? "Đăng nhập quản trị bằng mã khôi phục MFA." : "Đăng nhập quản trị thành công qua MFA.", null, ct);

        return Results.Ok(new
        {
            accessToken = session.AccessToken,
            refreshToken = session.RefreshToken,
            expiresAt = session.ExpiresAt,
            csrfToken = csrf,
            recoveryCodes,
            admin = new
            {
                account.Id,
                account.Email,
                account.DisplayName,
                mfaEnabled = true,
                roles,
                permissions = permissions.OrderBy(x => x).ToArray()
            }
        });
    }

    private static async Task<IResult> Refresh(
        HttpContext http,
        SessionService sessions,
        AdminPermissionService permissionService,
        UserManager<Account> users,
        CancellationToken ct)
    {
        if (!sessions.IsConfigured)
            return ApiSupport.Problem("AUTH_UNAVAILABLE", 503);

        string? refreshToken = http.Request.Cookies[AdminAuthConstants.RefreshCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken) && http.Request.ContentLength is > 0)
        {
            var body = await http.Request.ReadFromJsonAsync<RefreshBody>(ct);
            refreshToken = body?.RefreshToken;
        }
        if (refreshToken is not { Length: 64 })
        {
            ClearAdminAuthCookies(http);
            return ApiSupport.Problem("SESSION_EXPIRED", 401);
        }

        var rotated = await sessions.RotateAdmin(refreshToken, ct);
        if (rotated is null)
        {
            ClearAdminAuthCookies(http);
            return ApiSupport.Problem("SESSION_EXPIRED", 401);
        }

        var csrf = SetAdminAuthCookies(http, rotated);
        var account = await users.FindByIdAsync(rotated.UserId.ToString());
        var roles = await permissionService.GetEffectiveRolesAsync(rotated.UserId, ct);
        var permissions = await permissionService.GetEffectivePermissionsAsync(rotated.UserId, ct);

        return Results.Ok(new
        {
            accessToken = rotated.AccessToken,
            refreshToken = rotated.RefreshToken,
            expiresAt = rotated.ExpiresAt,
            csrfToken = csrf,
            admin = account is null ? null : new
            {
                account.Id,
                account.Email,
                account.DisplayName,
                mfaEnabled = account.TwoFactorEnabled,
                roles,
                permissions = permissions.OrderBy(x => x).ToArray()
            }
        });
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        HttpContext http,
        UserManager<Account> users,
        AdminPermissionService permissionService,
        CancellationToken ct)
    {
        var userId = ApiSupport.UserId(principal);
        var account = await users.FindByIdAsync(userId.ToString());
        if (account is null) return Results.Unauthorized();

        var roles = await permissionService.GetEffectiveRolesAsync(userId, ct);
        var permissions = await permissionService.GetEffectivePermissionsAsync(userId, ct);
        var csrf = http.Request.Cookies[AdminAuthConstants.CsrfCookieName];
        if (string.IsNullOrWhiteSpace(csrf))
        {
            csrf = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
            http.Response.Cookies.Append(AdminAuthConstants.CsrfCookieName, csrf, BuildCookieOptions(http, httpOnly: false, TimeSpan.FromHours(8)));
        }

        return Results.Ok(new
        {
            account.Id,
            account.Email,
            account.DisplayName,
            emailVerified = account.EmailConfirmed,
            mfaEnabled = account.TwoFactorEnabled,
            roles,
            permissions = permissions.OrderBy(x => x).ToArray(),
            csrfToken = csrf,
            account.CreatedAt,
            account.LastActiveAt
        });
    }

    private static async Task<IResult> Logout(
        ClaimsPrincipal principal,
        HttpContext http,
        SessionService sessions,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        CancellationToken ct)
    {
        var userId = ApiSupport.UserId(principal);
        var family = Guid.Parse(principal.FindFirstValue("sid")!);
        await sessions.Revoke(userId, family, ct);
        ClearAdminAuthCookies(http);
        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_LOGOUT", "SESSION", family.ToString("D"), "SUCCESS", null, null, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAll(
        ClaimsPrincipal principal,
        HttpContext http,
        SessionService sessions,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        CancellationToken ct)
    {
        var userId = ApiSupport.UserId(principal);
        await sessions.Revoke(userId, null, ct);
        ClearAdminAuthCookies(http);
        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_LOGOUT_ALL", "ACCOUNT", userId.ToString("D"), "SUCCESS", null, null, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RegenerateRecoveryCodes(
        MfaCodeOnlyRequest req,
        ClaimsPrincipal principal,
        UserManager<Account> users,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return ApiSupport.Problem("INVALID_MFA_CODE", 400);

        var userId = ApiSupport.UserId(principal);
        var account = await users.FindByIdAsync(userId.ToString());
        if (account is null) return Results.Unauthorized();

        var cleaned = req.Code.Replace(" ", "").Replace("-", "").Trim();
        var ok = await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, cleaned);
        if (!ok)
            return ApiSupport.Problem("INVALID_MFA_CODE", 401, "Mã TOTP hiện tại không đúng.");

        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))?.ToList() ?? [];
        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_MFA_RECOVERY_REGENERATED", "ACCOUNT", account.Id.ToString("D"), "SUCCESS", null, null, ct);
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static async Task<IResult> InspectInvitation(
        string? token,
        TMusicDb db,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ApiSupport.Problem("INVALID_INVITATION", 400);

        var hash = ApiSupport.Hash(token.Trim());
        var invite = await db.AdminInvitations.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (invite is null) return ApiSupport.Problem("INVITATION_NOT_FOUND", 404);
        if (invite.RevokedAt is not null) return ApiSupport.Problem("INVITATION_REVOKED", 410);
        if (invite.AcceptedAt is not null) return ApiSupport.Problem("INVITATION_ALREADY_USED", 409);
        if (invite.ExpiresAt <= now) return ApiSupport.Problem("INVITATION_EXPIRED", 410);

        var existing = await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == invite.NormalizedEmail && u.DeletedAt == null, ct);
        return Results.Ok(new
        {
            email = invite.Email,
            roleName = invite.RoleName,
            expiresAt = invite.ExpiresAt,
            existingAccount = existing
        });
    }

    private static async Task<IResult> AcceptInvitation(
        AcceptInvitationRequest req,
        TMusicDb db,
        UserManager<Account> users,
        SignInManager<Account> signIn,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return ApiSupport.Problem("INVALID_INVITATION", 400);

        var now = clock.GetUtcNow();
        var hash = ApiSupport.Hash(req.Token.Trim());
        var invite = await db.AdminInvitations.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (invite is null) return ApiSupport.Problem("INVITATION_NOT_FOUND", 404);
        if (invite.RevokedAt is not null) return ApiSupport.Problem("INVITATION_REVOKED", 410);
        if (invite.AcceptedAt is not null) return ApiSupport.Problem("INVITATION_ALREADY_USED", 409);
        if (invite.ExpiresAt <= now) return ApiSupport.Problem("INVITATION_EXPIRED", 410);

        var account = await users.FindByEmailAsync(invite.Email);
        if (account is null)
        {
            if (!AccountEndpoints.ValidPassword(req.Password))
                return ApiSupport.Problem("INVALID_PASSWORD", 400, "Mật khẩu phải từ 12-128 ký tự gồm chữ hoa, chữ thường, số và ký tự đặc biệt.");
            if (req.DisplayName?.Trim().Length is > 100)
                return ApiSupport.Problem("INVALID_DISPLAY_NAME", 400);

            account = new Account
            {
                Id = Guid.NewGuid(),
                Email = invite.Email,
                UserName = invite.Email,
                DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? invite.Email.Split('@')[0] : req.DisplayName.Trim(),
                EmailConfirmed = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            var created = await users.CreateAsync(account, req.Password!);
            if (!created.Succeeded)
                return ApiSupport.Problem("ACCOUNT_CREATE_FAILED", 400);
        }
        else
        {
            if (account.DeletedAt is not null || account.IsCurrentlySuspended(now))
                return ApiSupport.Problem("ACCOUNT_UNAVAILABLE", 403);
            if (string.IsNullOrWhiteSpace(req.Password) ||
                !(await signIn.CheckPasswordSignInAsync(account, req.Password, lockoutOnFailure: true)).Succeeded)
                return ApiSupport.Problem("INVALID_CREDENTIALS", 401, "Vui lòng xác nhận mật khẩu tài khoản hiện có.");
        }

        var key = await users.GetAuthenticatorKeyAsync(account);
        if (string.IsNullOrWhiteSpace(key))
        {
            await users.ResetAuthenticatorKeyAsync(account);
            key = await users.GetAuthenticatorKeyAsync(account);
        }

        var otpAuthUri = $"otpauth://totp/TMusic%20Admin:{UrlEncoder.Default.Encode(invite.Email)}?secret={key}&issuer=TMusic%20Admin&digits=6";
        return Results.Ok(new
        {
            status = "MFA_CONFIRMATION_REQUIRED",
            sharedKey = FormatKey(key!),
            rawKey = key,
            otpAuthUri,
            roleName = invite.RoleName
        });
    }

    private static async Task<IResult> CompleteInvitationMfa(
        CompleteInvitationMfaRequest req,
        HttpContext http,
        TMusicDb db,
        UserManager<Account> users,
        AdminPermissionService permissionService,
        SessionService sessions,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.Code))
            return ApiSupport.Problem("INVALID_REQUEST", 400);

        var now = clock.GetUtcNow();
        var hash = ApiSupport.Hash(req.Token.Trim());
        var invite = await db.AdminInvitations.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (invite is null) return ApiSupport.Problem("INVITATION_NOT_FOUND", 404);
        if (invite.RevokedAt is not null) return ApiSupport.Problem("INVITATION_REVOKED", 410);
        if (invite.AcceptedAt is not null) return ApiSupport.Problem("INVITATION_ALREADY_USED", 409);
        if (invite.ExpiresAt <= now) return ApiSupport.Problem("INVITATION_EXPIRED", 410);

        var account = await users.FindByEmailAsync(invite.Email);
        if (account is null || account.DeletedAt is not null || account.IsCurrentlySuspended(now))
            return ApiSupport.Problem("ACCOUNT_UNAVAILABLE", 403);

        var cleanedCode = req.Code.Replace(" ", "").Replace("-", "").Trim();
        var ok = await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, cleanedCode);
        if (!ok)
            return ApiSupport.Problem("INVALID_MFA_CODE", 401, "Mã TOTP không hợp lệ.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.AdminInvitations
            .Where(x => x.Id == invite.Id && x.AcceptedAt == null && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AcceptedAt, now)
                .SetProperty(x => x.AcceptedUserId, account.Id), ct);
        if (updated == 0)
            return ApiSupport.Problem("INVITATION_ALREADY_USED", 409);

        await users.SetTwoFactorEnabledAsync(account, true);
        var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))?.ToList() ?? [];
        var roleResult = await permissionService.SetSingleRoleAsync(account, invite.RoleName, ct);
        if (!roleResult.Succeeded)
            return ApiSupport.Problem("ROLE_ASSIGNMENT_FAILED", 500);

        var session = await sessions.CreateAdmin(account.Id, "TMusic Admin Web", mfaVerified: true, ct);
        await tx.CommitAsync(ct);

        var csrf = SetAdminAuthCookies(http, session);
        var roles = await permissionService.GetEffectiveRolesAsync(account.Id, ct);
        var permissions = await permissionService.GetEffectivePermissionsAsync(account.Id, ct);

        await audit.RecordAuditAsync(account.Id, account.Email, invite.RoleName, "ADMIN_INVITATION_ACCEPTED", "ADMIN_INVITATION", invite.Id.ToString("D"), "SUCCESS",
            $"Hoàn tất lời mời quản trị với vai trò {invite.RoleName} và kích hoạt MFA.", null, ct);

        return Results.Ok(new
        {
            accessToken = session.AccessToken,
            refreshToken = session.RefreshToken,
            expiresAt = session.ExpiresAt,
            csrfToken = csrf,
            recoveryCodes,
            admin = new
            {
                account.Id,
                account.Email,
                account.DisplayName,
                mfaEnabled = true,
                roles,
                permissions = permissions.OrderBy(x => x).ToArray()
            }
        });
    }

    private static string SetAdminAuthCookies(HttpContext http, SessionResponse session)
    {
        var csrf = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        http.Response.Cookies.Append(AdminAuthConstants.AccessCookieName, session.AccessToken,
            BuildCookieOptions(http, httpOnly: true, SessionService.AccessTokenLifetime));
        http.Response.Cookies.Append(AdminAuthConstants.RefreshCookieName, session.RefreshToken,
            BuildCookieOptions(http, httpOnly: true, SessionService.AdminRefreshLifetime));
        http.Response.Cookies.Append(AdminAuthConstants.CsrfCookieName, csrf,
            BuildCookieOptions(http, httpOnly: false, SessionService.AdminRefreshLifetime));
        return csrf;
    }

    private static void ClearAdminAuthCookies(HttpContext http)
    {
        var expired = BuildCookieOptions(http, httpOnly: true, TimeSpan.FromSeconds(-1));
        http.Response.Cookies.Delete(AdminAuthConstants.AccessCookieName, expired);
        http.Response.Cookies.Delete(AdminAuthConstants.RefreshCookieName, expired);
        http.Response.Cookies.Delete(AdminAuthConstants.CsrfCookieName, BuildCookieOptions(http, httpOnly: false, TimeSpan.FromSeconds(-1)));
    }

    private static CookieOptions BuildCookieOptions(HttpContext http, bool httpOnly, TimeSpan maxAge)
    {
        var isHttps = http.Request.IsHttps ||
            string.Equals(http.Request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase);
        return new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/api/admin",
            MaxAge = maxAge,
            IsEssential = true
        };
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        for (var i = 0; i < unformattedKey.Length; i += 4)
        {
            if (result.Length > 0) result.Append(' ');
            var len = Math.Min(4, unformattedKey.Length - i);
            result.Append(unformattedKey.AsSpan(i, len));
        }
        return result.ToString().ToLowerInvariant();
    }

    public sealed record AdminLoginRequest(string? Email, string? Password, string? DeviceName = null);
    public sealed record MfaSetupRequest(string? ChallengeToken);
    public sealed record MfaVerifyRequest(string? ChallengeToken, string? Code);
    public sealed record MfaCodeOnlyRequest(string? Code);
    public sealed record RefreshBody(string? RefreshToken);
    public sealed record AcceptInvitationRequest(string? Token, string? Password, string? DisplayName = null);
    public sealed record CompleteInvitationMfaRequest(string? Token, string? Code);
}
