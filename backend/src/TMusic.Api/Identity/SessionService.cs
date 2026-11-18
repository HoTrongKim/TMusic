using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TMusic.Infrastructure.Persistence;
using TMusic.Api.Common;
namespace TMusic.Api.Identity;

public sealed class SessionService(TMusicDb db, IConfiguration config, TimeProvider time)
{
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
        var result = Issue(userId, Guid.NewGuid(), Device(deviceName), now, now.AddDays(30), now);
        await db.SaveChangesAsync(ct);
        return result;
    }
    public async Task<SessionResponse?> Rotate(string token, CancellationToken ct)
    {
        var hash = ApiSupport.Hash(token);
        var previous = await db.UserSessions.AsNoTracking().SingleOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        var now = time.GetUtcNow();
        if (previous is null || previous.ExpiresAt <= now) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.UserSessions.Where(s => s.Id == previous.Id && s.RotatedAt == null && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RotatedAt, now), ct);
        if (changed == 0)
        {
            await Revoke(previous.UserId, previous.FamilyId, ct);
            await tx.CommitAsync(ct);
            return null;
        }
        var response = Issue(previous.UserId, previous.FamilyId, previous.DeviceName, now, previous.ExpiresAt, previous.AuthenticatedAt);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }
    public Task<int> Revoke(Guid userId, Guid? family, CancellationToken ct) => db.UserSessions
        .Where(s => s.UserId == userId && (family == null || s.FamilyId == family) && s.RevokedAt == null)
        .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, time.GetUtcNow()), ct);
    private SessionResponse Issue(Guid userId, Guid family, string device, DateTimeOffset now, DateTimeOffset expires, DateTimeOffset authenticated)
    {
        var refresh = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        db.UserSessions.Add(new UserSession { UserId = userId, FamilyId = family, DeviceName = device,
            RefreshTokenHash = ApiSupport.Hash(refresh), CreatedAt = now, ExpiresAt = expires, AuthenticatedAt = authenticated });
        var accessExpiry = now.AddMinutes(10);
        var token = new JwtSecurityToken(Issuer(config), Audience(config),
            [new("sub", userId.ToString()), new("sid", family.ToString()), new("jti", Guid.NewGuid().ToString()),
             new("auth_time", authenticated.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
            now.UtcDateTime, accessExpiry.UtcDateTime, new SigningCredentials(SigningKey(config)!, SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), refresh, accessExpiry, userId);
    }
    private static string Device(string? value) => string.IsNullOrWhiteSpace(value) ? "Thiết bị TMusic" : value.Trim()[..Math.Min(value.Trim().Length,100)];
}
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, Guid UserId);

