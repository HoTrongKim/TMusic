using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Api.Identity;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Api.Admin;

public static class AdminUsersEndpoints
{
    public static void MapAdminUsersEndpoints(this RouteGroupBuilder admin)
    {
        var users = admin.MapGroup("/users").AddEndpointFilter<AdminCsrfEndpointFilter>();
        users.MapGet("", ListUsers).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.UsersRead));
        users.MapGet("/{id:guid}", GetUserDetail).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.UsersRead));
        users.MapPost("/{id:guid}/suspend", SuspendUser).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.UsersSuspend));
        users.MapPost("/{id:guid}/reactivate", ReactivateUser).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.UsersSuspend));
        users.MapPut("/{id:guid}/role", UpdateUserRole).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.RolesManage));
        users.MapPost("/{id:guid}/deactivate", DeactivateUser).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.UsersDelete));

        var roles = admin.MapGroup("/roles").AddEndpointFilter<AdminCsrfEndpointFilter>();
        roles.MapGet("", ListRoles).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.RolesRead));
        roles.MapPut("/{roleName}/permissions", UpdateRolePermissions).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.RolesManage));
    }

    private static async Task<IResult> ListUsers(
        string? q,
        string? status,
        string? role,
        string? registeredFrom,
        string? registeredTo,
        string? sort,
        int? page,
        int? pageSize,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, q))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var actorId = ApiSupport.UserId(principal);
        var actorPerms = await permissionService.GetEffectivePermissionsAsync(actorId, ct);
        var canSeeFullEmail = actorPerms.Contains(AdminPermissions.UsersUpdate) || actorPerms.Contains(AdminPermissions.AdminsManage);

        var now = clock.GetUtcNow();
        var current = page ?? 1;
        var size = pageSize ?? 20;

        var query = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (Guid.TryParse(term, out var idSearch))
                query = query.Where(u => u.Id == idSearch);
            else
                query = query.Where(u =>
                    u.DisplayName.Contains(term) ||
                    (u.Email != null && u.Email.Contains(term)));
        }

        var statusFilter = (status ?? "").Trim().ToLowerInvariant();
        query = statusFilter switch
        {
            "active" => query.Where(u => u.DeletedAt == null && (!u.IsSuspended || (u.SuspendedUntil != null && u.SuspendedUntil <= now))),
            "suspended" => query.Where(u => u.DeletedAt == null && u.IsSuspended && (u.SuspendedUntil == null || u.SuspendedUntil > now)),
            "deactivated" => query.Where(u => u.DeletedAt != null),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(role) && AdminRoles.IsValid(role.Trim()))
        {
            var roleUpper = role.Trim().ToUpperInvariant();
            if (roleUpper == "USER")
            {
                var adminRoleIds = db.Roles.Where(r => r.Name != null && AdminRoles.Administrative.Contains(r.Name)).Select(r => r.Id);
                query = query.Where(u => !db.UserRoles.Any(ur => ur.UserId == u.Id && adminRoleIds.Contains(ur.RoleId)));
            }
            else
            {
                var roleId = await db.Roles.AsNoTracking().Where(r => r.NormalizedName == roleUpper).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
                if (roleId is null) return Results.Ok(new { items = Array.Empty<object>(), total = 0, page = current, pageSize = size });
                query = query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId.Value));
            }
        }

        if (DateTimeOffset.TryParse(registeredFrom, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromDt))
            query = query.Where(u => u.CreatedAt >= fromDt.ToUniversalTime());
        if (DateTimeOffset.TryParse(registeredTo, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toDt))
            query = query.Where(u => u.CreatedAt <= toDt.ToUniversalTime());

        var total = await query.CountAsync(ct);

        query = (sort ?? "created_desc").Trim().ToLowerInvariant() switch
        {
            "created_asc" => query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id),
            "name_asc" => query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id),
            "active_desc" => query.OrderByDescending(u => u.LastActiveAt).ThenByDescending(u => u.CreatedAt),
            _ => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
        };

        var rows = await query
            .Skip((current - 1) * size)
            .Take(size)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.EmailConfirmed,
                HasPassword = u.PasswordHash != null,
                u.TwoFactorEnabled,
                u.IsSuspended,
                u.SuspendedUntil,
                u.SuspensionReason,
                u.CreatedAt,
                u.LastActiveAt,
                u.DeletedAt
            })
            .ToListAsync(ct);

        var ids = rows.Select(x => x.Id).ToArray();
        var roleMap = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId) && r.Name != null
            select new { ur.UserId, Role = r.Name! }
        ).ToListAsync(ct);

        var providerMap = await db.UserAuthProviders.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Provider })
            .ToListAsync(ct);

        var albumCounts = await db.Albums.AsNoTracking()
            .Where(a => ids.Contains(a.OwnerId) && a.DeletedAt == null)
            .GroupBy(a => a.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Count, ct);

        var songCounts = await db.UserLibrarySongs.AsNoTracking()
            .Where(s => ids.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var items = rows.Select(u =>
        {
            var userRoles = roleMap.Where(r => r.UserId == u.Id).Select(r => r.Role).Distinct().OrderByDescending(AdminRoles.Rank).ToList();
            if (userRoles.Count == 0) userRoles.Add(AdminRoles.User);

            var methods = new List<string>();
            if (u.HasPassword) methods.Add("password");
            methods.AddRange(providerMap.Where(p => p.UserId == u.Id).Select(p => p.Provider).Distinct());

            var computedStatus = u.DeletedAt is not null
                ? "DEACTIVATED"
                : (u.IsSuspended && (u.SuspendedUntil is null || u.SuspendedUntil > now)) ? "SUSPENDED" : "ACTIVE";

            return new
            {
                u.Id,
                u.DisplayName,
                email = canSeeFullEmail ? u.Email : MaskEmail(u.Email),
                emailVerified = u.EmailConfirmed,
                mfaEnabled = u.TwoFactorEnabled,
                authMethods = methods,
                role = userRoles[0],
                roles = userRoles,
                status = computedStatus,
                u.SuspendedUntil,
                u.SuspensionReason,
                u.CreatedAt,
                u.LastActiveAt,
                albumCount = albumCounts.GetValueOrDefault(u.Id, 0),
                songCount = songCounts.GetValueOrDefault(u.Id, 0)
            };
        });

        return Results.Ok(new { items, total, page = current, pageSize = size });
    }

    private static async Task<IResult> GetUserDetail(
        Guid id,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        TimeProvider clock,
        CancellationToken ct)
    {
        var actorId = ApiSupport.UserId(principal);
        var actorPerms = await permissionService.GetEffectivePermissionsAsync(actorId, ct);
        var canSeeFullEmail = actorPerms.Contains(AdminPermissions.UsersUpdate) || actorPerms.Contains(AdminPermissions.AdminsManage);

        var now = clock.GetUtcNow();
        var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (account is null) return Results.NotFound();

        var roles = await permissionService.GetEffectiveRolesAsync(id, ct);
        var methods = new List<string>();
        if (account.PasswordHash is not null) methods.Add("password");
        methods.AddRange(await db.UserAuthProviders.AsNoTracking().Where(p => p.UserId == id).Select(p => p.Provider).Distinct().ToListAsync(ct));

        var albumCount = await db.Albums.AsNoTracking().CountAsync(a => a.OwnerId == id && a.DeletedAt == null, ct);
        var songCount = await db.UserLibrarySongs.AsNoTracking().CountAsync(s => s.UserId == id, ct);
        var serverAudioCount = await db.UserLibrarySongs.AsNoTracking().CountAsync(s => s.UserId == id && s.AudioStatus == ImportStatus.Ready, ct);
        var downloadCount = await db.AudioDownloadJobs.AsNoTracking().CountAsync(j => j.OwnerId == id, ct);
        var activeSessions = await db.UserSessions.AsNoTracking().CountAsync(s => s.UserId == id && s.RevokedAt == null && s.RotatedAt == null && s.ExpiresAt > now, ct);

        var history = await db.AccountSuspensions.AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(25)
            .Select(s => new { s.Id, s.Action, s.Reason, s.ExpiresAt, s.ActorId, s.CorrelationId, s.CreatedAt })
            .ToListAsync(ct);

        var computedStatus = account.DeletedAt is not null
            ? "DEACTIVATED"
            : account.IsCurrentlySuspended(now) ? "SUSPENDED" : "ACTIVE";

        return Results.Ok(new
        {
            account.Id,
            account.DisplayName,
            email = canSeeFullEmail ? account.Email : MaskEmail(account.Email),
            emailVerified = account.EmailConfirmed,
            mfaEnabled = account.TwoFactorEnabled,
            role = roles[0],
            roles,
            status = computedStatus,
            account.IsSuspended,
            account.SuspendedUntil,
            account.SuspensionReason,
            account.CreatedAt,
            account.UpdatedAt,
            account.LastActiveAt,
            account.DeletedAt,
            authMethods = methods,
            stats = new { albumCount, songCount, serverAudioCount, downloadCount, activeSessions },
            suspensionHistory = history
        });
    }

    private static async Task<IResult> SuspendUser(
        Guid id,
        SuspendRequest req,
        ClaimsPrincipal principal,
        HttpContext http,
        TMusicDb db,
        AdminPermissionService permissionService,
        SessionService sessions,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > 500)
            return ApiSupport.Problem("REASON_REQUIRED", 400, "Vui lòng nhập lý do tạm khóa (1–500 ký tự).");
        if (req.DurationHours is <= 0 or > 87600)
            return ApiSupport.Problem("INVALID_DURATION", 400, "Thời hạn tạm khóa phải trong khoảng 1 giờ đến 10 năm.");

        var actorId = ApiSupport.UserId(principal);
        if (actorId == id)
            return ApiSupport.Problem("CANNOT_SUSPEND_SELF", 409, "Không thể tự tạm khóa tài khoản của chính mình.");

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (target is null) return Results.NotFound();

        var actorRoles = await permissionService.GetEffectiveRolesAsync(actorId, ct);
        var targetRoles = await permissionService.GetEffectiveRolesAsync(id, ct);
        if (targetRoles.Contains(AdminRoles.SuperAdmin, StringComparer.Ordinal) &&
            !actorRoles.Contains(AdminRoles.SuperAdmin, StringComparer.Ordinal))
            return ApiSupport.Problem("PRIVILEGE_ESCALATION_BLOCKED", 403, "Không có quyền tạm khóa tài khoản SuperAdmin.");

        if (await permissionService.IsLastSuperAdminAsync(id, ct))
            return ApiSupport.Problem("LAST_SUPERADMIN_PROTECTED", 409, "Không thể tạm khóa SuperAdmin cuối cùng của hệ thống.");

        var now = clock.GetUtcNow();
        DateTimeOffset? expiresAt = req.DurationHours is { } h ? now.AddHours(h) : null;
        var reason = req.Reason.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        target.IsSuspended = true;
        target.SuspendedUntil = expiresAt;
        target.SuspensionReason = reason;
        target.UpdatedAt = now;
        db.AccountSuspensions.Add(new AccountSuspension
        {
            UserId = id,
            ActorId = actorId,
            Action = "SUSPEND",
            Reason = reason,
            ExpiresAt = expiresAt,
            CorrelationId = http.TraceIdentifier,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        var revokedSessions = await sessions.Revoke(id, null, ct);
        await tx.CommitAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "USER_SUSPENDED", "ACCOUNT", id.ToString("D"), "SUCCESS", reason,
            new { expiresAt, revokedSessions }, ct);

        return Results.Ok(new { id, status = "SUSPENDED", suspendedUntil = expiresAt, reason, revokedSessions });
    }

    private static async Task<IResult> ReactivateUser(
        Guid id,
        ReasonRequest req,
        ClaimsPrincipal principal,
        HttpContext http,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > 500)
            return ApiSupport.Problem("REASON_REQUIRED", 400, "Vui lòng nhập lý do mở khóa.");

        var actorId = ApiSupport.UserId(principal);
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (target is null) return Results.NotFound();
        if (target.DeletedAt is not null)
            return ApiSupport.Problem("ACCOUNT_DEACTIVATED", 409, "Tài khoản đã bị vô hiệu hóa vĩnh viễn.");

        var now = clock.GetUtcNow();
        var reason = req.Reason.Trim();

        target.IsSuspended = false;
        target.SuspendedUntil = null;
        target.SuspensionReason = null;
        target.LockoutEnd = null;
        target.UpdatedAt = now;
        db.AccountSuspensions.Add(new AccountSuspension
        {
            UserId = id,
            ActorId = actorId,
            Action = "REACTIVATE",
            Reason = reason,
            CorrelationId = http.TraceIdentifier,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "USER_REACTIVATED", "ACCOUNT", id.ToString("D"), "SUCCESS", reason, null, ct);
        return Results.Ok(new { id, status = "ACTIVE", reason });
    }

    private static async Task<IResult> UpdateUserRole(
        Guid id,
        ChangeRoleRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        UserManager<Account> users,
        AdminPermissionService permissionService,
        SessionService sessions,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var requestedRole = req.Role?.Trim();
        if (!AdminRoles.IsValid(requestedRole))
            return ApiSupport.Problem("INVALID_ROLE", 400, $"Vai trò hợp lệ: {string.Join(", ", AdminRoles.All)}.");

        var actorId = ApiSupport.UserId(principal);
        var target = await users.FindByIdAsync(id.ToString());
        if (target is null || target.DeletedAt is not null) return Results.NotFound();

        var actorRoles = await permissionService.GetEffectiveRolesAsync(actorId, ct);
        var targetRoles = await permissionService.GetEffectiveRolesAsync(id, ct);
        var currentTopRole = targetRoles[0];

        if (!AdminPermissionService.CanChangeRole(actorRoles, currentTopRole, requestedRole!, actorId, id))
        {
            await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "USER_ROLE_CHANGE", "ACCOUNT", id.ToString("D"), "DENIED",
                $"Từ chối thay đổi vai trò từ {currentTopRole} sang {requestedRole}.", null, ct);
            return ApiSupport.Problem("PRIVILEGE_ESCALATION_BLOCKED", 403, "Không được phép tự nâng quyền hoặc thay đổi vai trò vượt thẩm quyền.");
        }

        if (string.Equals(currentTopRole, AdminRoles.SuperAdmin, StringComparison.Ordinal) &&
            !string.Equals(requestedRole, AdminRoles.SuperAdmin, StringComparison.Ordinal) &&
            await permissionService.IsLastSuperAdminAsync(id, ct))
        {
            return ApiSupport.Problem("LAST_SUPERADMIN_PROTECTED", 409, "Không thể hạ quyền SuperAdmin cuối cùng của hệ thống.");
        }

        var result = await permissionService.SetSingleRoleAsync(target, requestedRole!, ct);
        if (!result.Succeeded)
            return ApiSupport.Problem("ROLE_UPDATE_FAILED", 500);

        target.UpdatedAt = clock.GetUtcNow();
        await users.UpdateAsync(target);
        // Revoke active sessions so role change takes effect cleanly on next auth.
        await sessions.Revoke(id, null, ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "USER_ROLE_CHANGED", "ACCOUNT", id.ToString("D"), "SUCCESS",
            req.Reason?.Trim() ?? $"Đổi vai trò từ {currentTopRole} sang {requestedRole}.",
            new { previousRole = currentTopRole, newRole = requestedRole }, ct);

        return Results.Ok(new { id, previousRole = currentTopRole, role = requestedRole });
    }

    private static async Task<IResult> DeactivateUser(
        Guid id,
        DeactivateRequest req,
        ClaimsPrincipal principal,
        HttpContext http,
        TMusicDb db,
        AdminPermissionService permissionService,
        SessionService sessions,
        ICoverImageStorage covers,
        IAudioStorage audio,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!req.Confirm || string.IsNullOrWhiteSpace(req.Reason))
            return ApiSupport.Problem("CONFIRMATION_REQUIRED", 400, "Cần xác nhận và nhập lý do vô hiệu hóa tài khoản.");

        var actorId = ApiSupport.UserId(principal);
        if (actorId == id)
            return ApiSupport.Problem("CANNOT_DEACTIVATE_SELF", 409, "Không thể tự vô hiệu hóa tài khoản của chính mình.");

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (target is null) return Results.NotFound();

        var actorRoles = await permissionService.GetEffectiveRolesAsync(actorId, ct);
        var targetRoles = await permissionService.GetEffectiveRolesAsync(id, ct);
        if (targetRoles.Contains(AdminRoles.SuperAdmin, StringComparer.Ordinal) &&
            !actorRoles.Contains(AdminRoles.SuperAdmin, StringComparer.Ordinal))
            return ApiSupport.Problem("PRIVILEGE_ESCALATION_BLOCKED", 403);

        if (await permissionService.IsLastSuperAdminAsync(id, ct))
            return ApiSupport.Problem("LAST_SUPERADMIN_PROTECTED", 409, "Không thể xóa hoặc vô hiệu hóa SuperAdmin cuối cùng.");

        var now = clock.GetUtcNow();
        var reason = req.Reason.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Safe deactivation: keep shared Song catalog untouched so other users' data is never harmed.
        target.DeletedAt = now;
        target.IsSuspended = true;
        target.SuspensionReason = reason;
        target.UpdatedAt = now;

        // Cancel user's active jobs and revoke share links.
        await db.AudioDownloadJobs
            .Where(j => j.OwnerId == id && (j.Status == ImportStatus.Queued || j.Status == ImportStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ImportStatus.Canceled).SetProperty(j => j.CompletedAt, now), ct);
        await db.AlbumShares
            .Where(s => s.OwnerId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        db.AccountSuspensions.Add(new AccountSuspension
        {
            UserId = id,
            ActorId = actorId,
            Action = "DEACTIVATE",
            Reason = reason,
            CorrelationId = http.TraceIdentifier,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        await sessions.Revoke(id, null, ct);
        await tx.CommitAsync(ct);

        if (req.PurgeOwnedFiles)
        {
            covers.DeleteUserCovers(id);
            audio.DeleteUserAudio(id);
        }

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "USER_DEACTIVATED", "ACCOUNT", id.ToString("D"), "SUCCESS", reason,
            new { purgeOwnedFiles = req.PurgeOwnedFiles }, ct);

        return Results.Ok(new { id, status = "DEACTIVATED", deletedAt = now });
    }

    private static async Task<IResult> ListRoles(
        TMusicDb db,
        AdminPermissionService permissionService,
        CancellationToken ct)
    {
        await permissionService.EnsureRolesSeededAsync(ct);
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        var claims = await db.RoleClaims.AsNoTracking()
            .Where(c => c.ClaimType == "permission" && c.ClaimValue != null)
            .ToListAsync(ct);
        var userRoleCounts = await db.UserRoles.AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

        var items = roles
            .Where(r => r.Name != null && AdminRoles.IsValid(r.Name))
            .OrderByDescending(r => AdminRoles.Rank(r.Name))
            .Select(r =>
            {
                var perms = string.Equals(r.Name, AdminRoles.SuperAdmin, StringComparison.Ordinal)
                    ? AdminPermissions.All
                    : claims.Where(c => c.RoleId == r.Id).Select(c => c.ClaimValue!).Distinct().OrderBy(x => x).ToArray();
                return new
                {
                    name = r.Name,
                    rank = AdminRoles.Rank(r.Name),
                    memberCount = userRoleCounts.GetValueOrDefault(r.Id, 0),
                    permissions = perms,
                    editable = r.Name is AdminRoles.Admin or AdminRoles.Moderator
                };
            });

        return Results.Ok(new { items, allPermissions = AdminPermissions.All });
    }

    private static async Task<IResult> UpdateRolePermissions(
        string roleName,
        UpdateRolePermissionsRequest req,
        ClaimsPrincipal principal,
        RoleManager<IdentityRole<Guid>> roleManager,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        CancellationToken ct)
    {
        var normalized = roleName.Trim();
        if (normalized is not (AdminRoles.Admin or AdminRoles.Moderator))
            return ApiSupport.Problem("ROLE_IMMUTABLE", 400, "Chỉ có thể tùy chỉnh quyền cho vai trò Admin và Moderator.");

        var desired = (req.Permissions ?? [])
            .Select(p => p.Trim())
            .Where(AdminPermissions.IsValid)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Guardrail: neither Admin nor Moderator can be granted admins.manage or roles.manage unless explicitly SuperAdmin
        if (normalized == AdminRoles.Moderator && desired.Any(p => p is AdminPermissions.SettingsManage or AdminPermissions.AdminsManage or AdminPermissions.RolesManage))
            return ApiSupport.Problem("MODERATOR_POLICY_VIOLATION", 400, "Moderator không được cấp quyền cấu hình hệ thống hoặc quản trị vai trò.");

        await permissionService.EnsureRolesSeededAsync(ct);
        var role = await roleManager.FindByNameAsync(normalized);
        if (role is null) return Results.NotFound();

        var existing = (await roleManager.GetClaimsAsync(role)).Where(c => c.Type == "permission").ToList();
        foreach (var c in existing)
            await roleManager.RemoveClaimAsync(role, c);
        foreach (var p in desired)
            await roleManager.AddClaimAsync(role, new Claim("permission", p));

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ROLE_PERMISSIONS_UPDATED", "ROLE", normalized, "SUCCESS",
            $"Cập nhật {desired.Count} quyền cho vai trò {normalized}.", new { role = normalized, permissions = desired }, ct);

        return Results.Ok(new { role = normalized, permissions = desired });
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;
        var at = email.IndexOf('@');
        if (at <= 1) return "***";
        return $"{email[0]}***{email[at - 1]}{email[at..]}";
    }

    public sealed record SuspendRequest(string? Reason, int? DurationHours = null);
    public sealed record ReasonRequest(string? Reason);
    public sealed record ChangeRoleRequest(string? Role, string? Reason = null);
    public sealed record DeactivateRequest(bool Confirm, string? Reason, bool PurgeOwnedFiles = false);
    public sealed record UpdateRolePermissionsRequest(string[]? Permissions);
}
