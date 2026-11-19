using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Identity;

public sealed class SessionService(TMusicDb db, IConfiguration config, TimeProvider time)
{
    public static readonly TimeSpan UserRefreshLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan AdminRefreshLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);

    public static string Issuer(IConfiguration c) => c["Auth:Jwt:Issuer"] ?? c["Auth:Issuer"] ?? "TMusic";
    public static string Audience(IConfiguration c) => c["Auth:Jwt:Audience"] ?? c["Auth:Audience"] ?? "TMusic.Mobile";

    public static SymmetricSecurityKey? SigningKey(IConfiguration c)
    {
        var secret = c["Auth:Jwt:SigningKey"] ?? c["Auth:SigningKey"];
        return secret is not null && Encoding.UTF8.GetByteCount(secret) >= 32 ? new(Encoding.UTF8.GetBytes(secret)) : null;
    }

    public bool IsConfigured => SigningKey(config) is not null;

    public async Task<SessionResponse> Create(Guid userId, string? deviceName, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var result = Issue(userId, Guid.NewGuid(), Device(deviceName), now, now.Add(UserRefreshLifetime), now, isAdmin: false, mfaVerified: false);
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, now), ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<SessionResponse> CreateAdmin(Guid userId, string? deviceName, bool mfaVerified, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var result = Issue(userId, Guid.NewGuid(), Device(deviceName ?? "TMusic Admin Web"), now, now.Add(AdminRefreshLifetime), now, isAdmin: true, mfaVerified: mfaVerified);
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, now), ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<SessionResponse?> Rotate(string token, CancellationToken ct) =>
        await RotateCore(token, requireAdminSession: false, ct);

    public async Task<SessionResponse?> RotateAdmin(string token, CancellationToken ct) =>
        await RotateCore(token, requireAdminSession: true, ct);

    private async Task<SessionResponse?> RotateCore(string token, bool requireAdminSession, CancellationToken ct)
    {
        var hash = ApiSupport.Hash(token);
        var previous = await db.UserSessions.AsNoTracking().SingleOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        var now = time.GetUtcNow();
        if (previous is null || previous.ExpiresAt <= now) return null;
        if (requireAdminSession && (!previous.IsAdminSession || !previous.MfaVerified)) return null;

        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == previous.UserId)
            .Select(u => new { u.Id, u.DeletedAt, u.IsSuspended, u.SuspendedUntil, u.LockoutEnd, u.TwoFactorEnabled })
            .FirstOrDefaultAsync(ct);
        if (account is null ||
            account.DeletedAt is not null ||
            (account.IsSuspended && (account.SuspendedUntil is null || account.SuspendedUntil > now)) ||
            (account.LockoutEnd is not null && account.LockoutEnd > now))
        {
            await Revoke(previous.UserId, previous.FamilyId, ct);
            return null;
        }

        if (requireAdminSession)
        {
            if (!account.TwoFactorEnabled)
            {
                await Revoke(previous.UserId, previous.FamilyId, ct);
                return null;
            }
            var hasAdminRole = await (
                from ur in db.UserRoles.AsNoTracking()
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where ur.UserId == previous.UserId && r.Name != null && AdminRoles.Administrative.Contains(r.Name)
                select r.Id
            ).AnyAsync(ct);
            if (!hasAdminRole)
            {
                await Revoke(previous.UserId, previous.FamilyId, ct);
                return null;
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.UserSessions.Where(s => s.Id == previous.Id && s.RotatedAt == null && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RotatedAt, now), ct);
        if (changed == 0)
        {
            await Revoke(previous.UserId, previous.FamilyId, ct);
            await tx.CommitAsync(ct);
            return null;
        }

        var response = Issue(previous.UserId, previous.FamilyId, previous.DeviceName, now, previous.ExpiresAt, previous.AuthenticatedAt, previous.IsAdminSession, previous.MfaVerified);
        await db.Users.Where(u => u.Id == previous.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, now), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }

    public Task<int> Revoke(Guid userId, Guid? family, CancellationToken ct) => db.UserSessions
        .Where(s => s.UserId == userId && (family == null || s.FamilyId == family) && s.RevokedAt == null)
        .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, time.GetUtcNow()), ct);

    private SessionResponse Issue(Guid userId, Guid family, string device, DateTimeOffset now, DateTimeOffset expires, DateTimeOffset authenticated, bool isAdmin, bool mfaVerified)
    {
        var refresh = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            FamilyId = family,
            DeviceName = device,
            IsAdminSession = isAdmin,
            MfaVerified = mfaVerified,
            RefreshTokenHash = ApiSupport.Hash(refresh),
            CreatedAt = now,
            ExpiresAt = expires,
            AuthenticatedAt = authenticated,
            LastSeenAt = now
        });
        var accessExpiry = now.Add(AccessTokenLifetime);
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("sid", family.ToString()),
            new("jti", Guid.NewGuid().ToString()),
            new("auth_time", authenticated.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        if (isAdmin)
        {
            claims.Add(new("scope", "admin"));
            if (mfaVerified) claims.Add(new("amr", "mfa"));
        }
        var token = new JwtSecurityToken(Issuer(config), Audience(config), claims,
            now.UtcDateTime, accessExpiry.UtcDateTime, new SigningCredentials(SigningKey(config)!, SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), refresh, accessExpiry, userId);
    }

    private static string Device(string? value) => string.IsNullOrWhiteSpace(value) ? "Thiết bị TMusic" : value.Trim()[..Math.Min(value.Trim().Length, 100)];
}

public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, Guid UserId);
