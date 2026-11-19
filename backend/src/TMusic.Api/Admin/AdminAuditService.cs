using System.Security.Claims;
using System.Text.Json;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Admin;

public sealed class AdminAuditService(TMusicDb db, TimeProvider clock, IHttpContextAccessor httpAccessor)
{
    public async Task RecordAuditAsync(
        Guid? actorId,
        string? actorEmail,
        string? actorRole,
        string action,
        string targetType,
        string? targetId,
        string result,
        string? reason = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var http = httpAccessor.HttpContext;
        var metadataJson = metadata is null ? null : SerializeSanitized(metadata);
        var entry = new AuditLog
        {
            ActorId = actorId,
            ActorEmail = Truncate(actorEmail, 256),
            ActorRole = Truncate(actorRole, 64),
            Action = Truncate(action, 80) ?? "UNKNOWN",
            TargetType = Truncate(targetType, 64) ?? "SYSTEM",
            TargetId = Truncate(targetId, 128),
            Result = Truncate(result, 16) ?? "SUCCESS",
            Reason = Truncate(reason, 500),
            MetadataJson = metadataJson,
            CorrelationId = Truncate(http?.TraceIdentifier, 128),
            IpAddress = Truncate(http?.Connection.RemoteIpAddress?.ToString(), 64),
            CreatedAt = clock.GetUtcNow()
        };

        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordAuditFromPrincipalAsync(
        ClaimsPrincipal principal,
        AdminPermissionService permissions,
        string action,
        string targetType,
        string? targetId,
        string result,
        string? reason = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        Guid? actorId = Guid.TryParse(principal.FindFirstValue("sub"), out var parsed) ? parsed : null;
        string? email = null;
        string? topRole = null;
        if (actorId is not null)
        {
            email = await db.Users.Where(u => u.Id == actorId.Value).Select(u => u.Email).FirstOrDefaultAsyncSafe(ct);
            var roles = await permissions.GetEffectiveRolesAsync(actorId.Value, ct);
            topRole = roles.FirstOrDefault();
        }
        await RecordAuditAsync(actorId, email, topRole, action, targetType, targetId, result, reason, metadata, ct);
    }

    public async Task RecordSystemEventAsync(
        string severity,
        string eventType,
        string code,
        string message,
        Guid? userId = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var http = httpAccessor.HttpContext;
        db.SystemEvents.Add(new SystemEvent
        {
            Severity = Truncate(severity, 16) ?? "INFO",
            EventType = Truncate(eventType, 48) ?? "SYSTEM_HEALTH",
            Code = Truncate(code, 80) ?? "EVENT",
            Message = Truncate(ScrubSensitiveText(message), 1000) ?? "",
            CorrelationId = Truncate(http?.TraceIdentifier, 128),
            UserId = userId,
            MetadataJson = metadata is null ? null : SerializeSanitized(metadata),
            CreatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? SerializeSanitized(object metadata)
    {
        var json = JsonSerializer.Serialize(metadata);
        json = ScrubSensitiveText(json);
        return json.Length > 4000 ? json[..4000] : json;
    }

    internal static string ScrubSensitiveText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var lowered = value.ToLowerInvariant();
        if (lowered.Contains("password=") || lowered.Contains("bearer ") || lowered.Contains("signingkey"))
            return "[REDACTED_SENSITIVE_CONTENT]";
        return value;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}

internal static class EfSafeSingleExtensions
{
    public static async Task<string?> FirstOrDefaultAsyncSafe(this IQueryable<string?> query, CancellationToken ct) =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, ct);
}
