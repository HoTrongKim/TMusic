using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Api.Identity;
using TMusic.Application.Identity;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Admin;

public static class AdminSystemEndpoints
{
    public static void MapAdminSystemEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/audit-logs", ListAuditLogs)
            .RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AuditRead));

        admin.MapGet("/system-events", ListSystemEvents)
            .RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AuditRead));

        var settings = admin.MapGroup("/settings").AddEndpointFilter<AdminCsrfEndpointFilter>();
        settings.MapGet("", GetSettings).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.SettingsRead));
        settings.MapPut("/{key}", UpdateSetting).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.SettingsManage));
        settings.MapPost("/{key}/reset", ResetSetting).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.SettingsManage));

        var admins = admin.MapGroup("/admins").AddEndpointFilter<AdminCsrfEndpointFilter>();
        admins.MapGet("", ListAdmins).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AdminsManage));
        admins.MapPost("/invite", InviteAdmin).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AdminsManage));
        admins.MapGet("/invitations", ListInvitations).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AdminsManage));
        admins.MapPost("/invitations/{id:guid}/revoke", RevokeInvitation).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AdminsManage));
        admins.MapPut("/{id:guid}/role", UpdateAdminRole).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AdminsManage));
        admins.MapPost("/{id:guid}/deactivate", DeactivateAdmin).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AdminsManage));
    }

    private static async Task<IResult> ListAuditLogs(
        string? action,
        string? result,
        string? correlationId,
        string? from,
        string? to,
        int? page,
        int? pageSize,
        TMusicDb db,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, action))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var current = page ?? 1;
        var size = pageSize ?? 30;
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action.Contains(action.Trim()));
        if (!string.IsNullOrWhiteSpace(result))
            query = query.Where(x => x.Result == result.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(x => x.CorrelationId == correlationId.Trim());
        if (DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromDt))
            query = query.Where(x => x.CreatedAt >= fromDt.ToUniversalTime());
        if (DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toDt))
            query = query.Where(x => x.CreatedAt <= toDt.ToUniversalTime());

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return Results.Ok(new { items, total, page = current, pageSize = size, retentionPolicyDays = 365 });
    }

    private static async Task<IResult> ListSystemEvents(
        string? severity,
        string? eventType,
        string? correlationId,
        string? from,
        string? to,
        int? page,
        int? pageSize,
        TMusicDb db,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, correlationId))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var current = page ?? 1;
        var size = pageSize ?? 30;
        var query = db.SystemEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(x => x.Severity == severity.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(x => x.EventType == eventType.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(x => x.CorrelationId == correlationId.Trim());
        if (DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromDt))
            query = query.Where(x => x.CreatedAt >= fromDt.ToUniversalTime());
        if (DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toDt))
            query = query.Where(x => x.CreatedAt <= toDt.ToUniversalTime());

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return Results.Ok(new { items, total, page = current, pageSize = size });
    }

    private static async Task<IResult> GetSettings(
        string? key,
        AdminSettingsService settingsService,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (AdminSettingsService.IsForbiddenSecretKey(key))
                return ApiSupport.Problem("SECRET_KEY_FORBIDDEN", 403, "Không được phép đọc bí mật hệ thống từ Admin API.");
            var allItems = await settingsService.GetAllAsync(ct);
            var single = allItems.FirstOrDefault(x => string.Equals(x.Key, key.Trim(), StringComparison.Ordinal));
            return single is null ? Results.NotFound() : Results.Ok(single);
        }

        var items = await settingsService.GetAllAsync(ct);
        return Results.Ok(new
        {
            items,
            secretsPolicy = "JWT signing keys, database passwords, SMTP credentials và OAuth secrets được quản lý tuyệt đối qua biến môi trường hạ tầng, không bao giờ hiển thị hoặc chỉnh sửa qua dashboard."
        });
    }

    private static async Task<IResult> UpdateSetting(
        string key,
        UpdateSettingRequest req,
        ClaimsPrincipal principal,
        AdminSettingsService settingsService,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        CancellationToken ct)
    {
        var actorId = ApiSupport.UserId(principal);
        var (updated, errorCode, errorDetail) = await settingsService.UpdateAsync(key, req.Value, req.Confirmed, actorId, ct);
        if (errorCode is not null)
        {
            var status = errorCode switch
            {
                "SECRET_KEY_FORBIDDEN" => 403,
                "SETTING_NOT_FOUND" => 404,
                _ => 400
            };
            return ApiSupport.Problem(errorCode, status, errorDetail);
        }

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "SYSTEM_SETTING_UPDATED", "SYSTEM_SETTING", key, "SUCCESS",
            req.Reason?.Trim() ?? $"Cập nhật cấu hình {key}.", new { key, value = updated!.Value }, ct);

        return Results.Ok(updated);
    }

    private static async Task<IResult> ResetSetting(
        string key,
        ClaimsPrincipal principal,
        AdminSettingsService settingsService,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        CancellationToken ct)
    {
        var actorId = ApiSupport.UserId(principal);
        var (updated, errorCode) = await settingsService.ResetToDefaultAsync(key, actorId, ct);
        if (errorCode is not null)
            return ApiSupport.Problem(errorCode, errorCode == "SECRET_KEY_FORBIDDEN" ? 403 : 404);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "SYSTEM_SETTING_RESET", "SYSTEM_SETTING", key, "SUCCESS",
            $"Khôi phục cấu hình {key} về giá trị mặc định ({updated!.DefaultValue}).", new { key, value = updated.Value }, ct);

        return Results.Ok(updated);
    }

    private static async Task<IResult> ListAdmins(
        TMusicDb db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var adminRoles = await db.Roles.AsNoTracking()
            .Where(r => r.Name != null && AdminRoles.Administrative.Contains(r.Name))
            .Select(r => new { r.Id, Name = r.Name! })
            .ToListAsync(ct);
        var adminRoleIds = adminRoles.Select(r => r.Id).ToArray();

        var mappings = await db.UserRoles.AsNoTracking()
            .Where(ur => adminRoleIds.Contains(ur.RoleId))
            .ToListAsync(ct);

        var adminUserIds = mappings.Select(m => m.UserId).Distinct().ToArray();
        var accounts = await db.Users.AsNoTracking()
            .Where(u => adminUserIds.Contains(u.Id))
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var recentActions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActorId != null && adminUserIds.Contains(a.ActorId.Value))
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .Select(a => new { a.Id, a.ActorId, a.ActorEmail, a.ActorRole, a.Action, a.TargetType, a.TargetId, a.Result, a.Reason, a.CreatedAt })
            .ToListAsync(ct);

        var items = accounts.Select(a =>
        {
            var roleNames = mappings
                .Where(m => m.UserId == a.Id)
                .Join(adminRoles, m => m.RoleId, r => r.Id, (_, r) => r.Name)
                .Distinct()
                .OrderByDescending(AdminRoles.Rank)
                .ToList();
            var status = a.DeletedAt is not null ? "DEACTIVATED" : a.IsCurrentlySuspended(now) ? "SUSPENDED" : "ACTIVE";
            return new
            {
                a.Id,
                a.Email,
                a.DisplayName,
                role = roleNames.FirstOrDefault() ?? AdminRoles.User,
                roles = roleNames,
                mfaEnabled = a.TwoFactorEnabled,
                emailVerified = a.EmailConfirmed,
                status,
                a.CreatedAt,
                a.LastActiveAt
            };
        });

        return Results.Ok(new { items, recentActions });
    }

    private static async Task<IResult> InviteAdmin(
        InviteAdminRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        IAccountMailSender mail,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!AccountEndpoints.ValidEmail(req.Email))
            return ApiSupport.Problem("INVALID_EMAIL", 400, "Email người được mời không hợp lệ.");

        var roleName = (req.RoleName ?? AdminRoles.Admin).Trim();
        if (!AdminRoles.IsAdministrative(roleName))
            return ApiSupport.Problem("INVALID_ADMIN_ROLE", 400, "Vai trò mời hợp lệ: SuperAdmin, Admin hoặc Moderator.");

        var expiresHours = Math.Clamp(req.ExpiresInHours ?? 24, 1, 168);
        var now = clock.GetUtcNow();
        var email = req.Email!.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var invite = new AdminInvitation
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            RoleName = roleName,
            TokenHash = ApiSupport.Hash(rawToken),
            InvitedByUserId = ApiSupport.UserId(principal),
            Note = req.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null,
            CreatedAt = now,
            ExpiresAt = now.AddHours(expiresHours)
        };
        db.AdminInvitations.Add(invite);
        await db.SaveChangesAsync(ct);

        var emailDelivered = false;
        if (mail.IsConfigured)
        {
            try
            {
                await mail.SendCodeAsync(email, AccountCodePurpose.AdminInvitation, rawToken, ct);
                emailDelivered = true;
            }
            catch (SmtpException)
            {
                emailDelivered = false;
            }
        }

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_INVITED", "ADMIN_INVITATION", invite.Id.ToString("D"), "SUCCESS",
            $"Mời {email} với vai trò {roleName}.", new { roleName, emailDelivered, expiresAt = invite.ExpiresAt }, ct);

        return Results.Created($"/api/admin/admins/invitations", new
        {
            invite.Id,
            invite.Email,
            invite.RoleName,
            invite.ExpiresAt,
            emailDelivered,
            // Single-use invitation token returned once so SuperAdmin can deliver via secure channel if SMTP is unconfigured.
            invitationToken = rawToken,
            acceptPath = $"/accept-invite?token={rawToken}"
        });
    }

    private static async Task<IResult> ListInvitations(TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.AdminInvitations.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .Select(i => new
            {
                i.Id,
                i.Email,
                i.RoleName,
                i.Note,
                i.InvitedByUserId,
                i.CreatedAt,
                i.ExpiresAt,
                i.AcceptedAt,
                i.RevokedAt,
                status = i.RevokedAt != null ? "REVOKED"
                    : i.AcceptedAt != null ? "ACCEPTED"
                    : i.ExpiresAt <= now ? "EXPIRED" : "PENDING"
            })
            .ToListAsync(ct);

        return Results.Ok(new { items = rows });
    }

    private static async Task<IResult> RevokeInvitation(
        Guid id,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var invite = await db.AdminInvitations.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invite is null) return Results.NotFound();
        if (invite.AcceptedAt is not null)
            return ApiSupport.Problem("INVITATION_ALREADY_USED", 409);

        invite.RevokedAt = clock.GetUtcNow();
        invite.RevokedByUserId = ApiSupport.UserId(principal);
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_INVITATION_REVOKED", "ADMIN_INVITATION", id.ToString("D"), "SUCCESS",
            $"Thu hồi lời mời quản trị gửi tới {invite.Email}.", null, ct);

        return Results.Ok(new { invite.Id, status = "REVOKED" });
    }

    private static async Task<IResult> UpdateAdminRole(
        Guid id,
        AdminRoleChangeRequest req,
        ClaimsPrincipal principal,
        UserManager<Account> users,
        AdminPermissionService permissionService,
        SessionService sessions,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var requestedRole = req.Role?.Trim();
        if (!AdminRoles.IsValid(requestedRole))
            return ApiSupport.Problem("INVALID_ROLE", 400);

        var target = await users.FindByIdAsync(id.ToString());
        if (target is null || target.DeletedAt is not null) return Results.NotFound();

        var currentRoles = await permissionService.GetEffectiveRolesAsync(id, ct);
        var currentTop = currentRoles[0];
        if (string.Equals(currentTop, AdminRoles.SuperAdmin, StringComparison.Ordinal) &&
            !string.Equals(requestedRole, AdminRoles.SuperAdmin, StringComparison.Ordinal) &&
            await permissionService.IsLastSuperAdminAsync(id, ct))
        {
            return ApiSupport.Problem("LAST_SUPERADMIN_PROTECTED", 409, "Không thể hạ quyền SuperAdmin cuối cùng.");
        }

        var result = await permissionService.SetSingleRoleAsync(target, requestedRole!, ct);
        if (!result.Succeeded) return ApiSupport.Problem("ROLE_UPDATE_FAILED", 500);

        target.UpdatedAt = clock.GetUtcNow();
        await users.UpdateAsync(target);
        await sessions.Revoke(id, null, ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_ROLE_UPDATED", "ACCOUNT", id.ToString("D"), "SUCCESS",
            req.Reason?.Trim() ?? $"Đổi vai trò quản trị từ {currentTop} sang {requestedRole}.",
            new { previousRole = currentTop, newRole = requestedRole }, ct);

        return Results.Ok(new { id, previousRole = currentTop, role = requestedRole });
    }

    private static async Task<IResult> DeactivateAdmin(
        Guid id,
        AdminDeactivateRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        SessionService sessions,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return ApiSupport.Problem("REASON_REQUIRED", 400);

        var actorId = ApiSupport.UserId(principal);
        if (actorId == id)
            return ApiSupport.Problem("CANNOT_DEACTIVATE_SELF", 409);

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (target is null) return Results.NotFound();

        if (await permissionService.IsLastSuperAdminAsync(id, ct))
            return ApiSupport.Problem("LAST_SUPERADMIN_PROTECTED", 409, "Không thể vô hiệu hóa SuperAdmin cuối cùng.");

        var now = clock.GetUtcNow();
        target.IsSuspended = true;
        target.SuspendedUntil = null;
        target.SuspensionReason = req.Reason.Trim();
        target.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await sessions.Revoke(id, null, ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_DEACTIVATED", "ACCOUNT", id.ToString("D"), "SUCCESS",
            req.Reason.Trim(), null, ct);

        return Results.Ok(new { id, status = "SUSPENDED" });
    }

    public sealed record UpdateSettingRequest(string? Value, bool Confirmed = false, string? Reason = null);
    public sealed record InviteAdminRequest(string? Email, string? RoleName, int? ExpiresInHours = 24, string? Note = null);
    public sealed record AdminRoleChangeRequest(string? Role, string? Reason = null);
    public sealed record AdminDeactivateRequest(string? Reason);
}
