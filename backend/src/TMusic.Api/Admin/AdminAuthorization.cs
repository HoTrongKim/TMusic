using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Admin;

public static class AdminAuthConstants
{
    public const string AccessCookieName = "tmusic_admin_access";
    public const string RefreshCookieName = "tmusic_admin_refresh";
    public const string CsrfCookieName = "tmusic_admin_csrf";
    public const string CsrfHeaderName = "X-CSRF-Token";
    public const string AdminAccessPolicy = "Admin.Access";
    public const string AuthViaCookieItemKey = "TMusic.AdminAuthViaCookie";

    public static string PolicyFor(string permission) => $"Admin.Perm.{permission}";
}

public sealed record AdminPermissionRequirement(string? Permission) : IAuthorizationRequirement;

public sealed class AdminPermissionService(
    TMusicDb db,
    UserManager<Account> users,
    RoleManager<IdentityRole<Guid>> roles)
{
    public async Task EnsureRolesSeededAsync(CancellationToken ct)
    {
        foreach (var roleName in AdminRoles.All)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName);
                await roles.CreateAsync(role);
            }

            if (AdminPermissions.DefaultRolePermissions.TryGetValue(roleName, out var defaults) && defaults.Length > 0)
            {
                var existing = await db.RoleClaims.AsNoTracking()
                    .Where(c => c.RoleId == role.Id && c.ClaimType == "permission")
                    .Select(c => c.ClaimValue)
                    .ToListAsync(ct);
                if (existing.Count == 0)
                {
                    foreach (var p in defaults)
                        await roles.AddClaimAsync(role, new Claim("permission", p));
                }
            }
        }
    }

    public async Task<IReadOnlyList<string>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct)
    {
        var roleNames = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name != null
            select r.Name!
        ).ToListAsync(ct);

        if (roleNames.Count == 0)
            return [AdminRoles.User];

        return roleNames
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(AdminRoles.Rank)
            .ToList();
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct)
    {
        var effectiveRoles = await GetEffectiveRolesAsync(userId, ct);
        if (effectiveRoles.Contains(AdminRoles.SuperAdmin, StringComparer.Ordinal))
            return AdminPermissions.All.ToHashSet(StringComparer.Ordinal);

        var adminRoles = effectiveRoles.Where(AdminRoles.IsAdministrative).ToArray();
        if (adminRoles.Length == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var roleRows = await db.Roles.AsNoTracking()
            .Where(r => r.Name != null && adminRoles.Contains(r.Name))
            .Select(r => new { r.Id, Name = r.Name! })
            .ToListAsync(ct);

        var roleIds = roleRows.Select(r => r.Id).ToArray();
        var claimRows = await db.RoleClaims.AsNoTracking()
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == "permission" && c.ClaimValue != null)
            .Select(c => new { c.RoleId, Permission = c.ClaimValue! })
            .ToListAsync(ct);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in roleRows)
        {
            var custom = claimRows.Where(c => c.RoleId == row.Id).Select(c => c.Permission).ToList();
            IEnumerable<string> source = custom.Count > 0
                ? custom
                : AdminPermissions.DefaultRolePermissions.TryGetValue(row.Name, out var defaults)
                    ? defaults
                    : Array.Empty<string>();
            foreach (var p in source)
            {
                if (AdminPermissions.IsValid(p))
                    result.Add(p);
            }
        }
        return result;
    }

    public async Task<bool> IsLastSuperAdminAsync(Guid userId, CancellationToken ct)
    {
        var superRoleId = await db.Roles.AsNoTracking()
            .Where(r => r.NormalizedName == "SUPERADMIN")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (superRoleId is null) return false;

        var now = DateTimeOffset.UtcNow;
        var activeSuperIds = await (
            from ur in db.UserRoles.AsNoTracking()
            join a in db.Users.AsNoTracking() on ur.UserId equals a.Id
            where ur.RoleId == superRoleId.Value
                  && a.DeletedAt == null
                  && (!a.IsSuspended || (a.SuspendedUntil != null && a.SuspendedUntil <= now))
            select a.Id
        ).Distinct().ToListAsync(ct);

        return activeSuperIds.Count <= 1 && activeSuperIds.Contains(userId);
    }

    public static bool CanChangeRole(
        IReadOnlyCollection<string> actorRoles,
        string targetCurrentTopRole,
        string requestedRole,
        Guid actorId,
        Guid targetUserId)
    {
        if (!AdminRoles.IsValid(requestedRole)) return false;

        var actorIsSuper = actorRoles.Contains(AdminRoles.SuperAdmin, StringComparer.Ordinal);
        if (actorIsSuper) return true;

        // Non-SuperAdmin can NEVER assign SuperAdmin, modify an existing SuperAdmin, or self-elevate.
        if (string.Equals(requestedRole, AdminRoles.SuperAdmin, StringComparison.Ordinal)) return false;
        if (string.Equals(targetCurrentTopRole, AdminRoles.SuperAdmin, StringComparison.Ordinal)) return false;
        if (actorId == targetUserId) return false;

        var actorRank = actorRoles.Select(AdminRoles.Rank).DefaultIfEmpty(0).Max();
        return AdminRoles.Rank(requestedRole) < actorRank && AdminRoles.Rank(targetCurrentTopRole) < actorRank;
    }

    public async Task<IdentityResult> SetSingleRoleAsync(Account target, string roleName, CancellationToken ct)
    {
        await EnsureRolesSeededAsync(ct);
        var current = await users.GetRolesAsync(target);
        if (current.Count > 0)
        {
            var removed = await users.RemoveFromRolesAsync(target, current);
            if (!removed.Succeeded) return removed;
        }
        return await users.AddToRoleAsync(target, roleName);
    }
}

public sealed class AdminPermissionHandler(
    IServiceScopeFactory scopes,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<AdminPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminPermissionRequirement requirement)
    {
        var principal = context.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return;

        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("sid"), out var familyId))
            return;

        var httpContext = httpContextAccessor.HttpContext;
        var ct = httpContext?.RequestAborted ?? CancellationToken.None;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var permissionService = scope.ServiceProvider.GetRequiredService<AdminPermissionService>();
        var now = clock.GetUtcNow();

        // 1. Verify active admin session in SQL Server (never trust stale JWT claims or regular mobile sessions).
        var sessionValid = await db.UserSessions.AsNoTracking().AnyAsync(s =>
            s.UserId == userId &&
            s.FamilyId == familyId &&
            s.IsAdminSession &&
            s.RevokedAt == null &&
            s.RotatedAt == null &&
            s.ExpiresAt > now, ct);
        if (!sessionValid)
            return;

        // 2. Verify account status is active (not deleted, suspended, or locked out).
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.DeletedAt,
                u.IsSuspended,
                u.SuspendedUntil,
                u.LockoutEnd
            })
            .FirstOrDefaultAsync(ct);

        if (account is null ||
            account.DeletedAt is not null ||
            (account.IsSuspended && (account.SuspendedUntil is null || account.SuspendedUntil > now)) ||
            (account.LockoutEnd is not null && account.LockoutEnd > now))
            return;

        // 3. Verify live roles & permissions from SQL Server.
        var roles = await permissionService.GetEffectiveRolesAsync(userId, ct);
        if (!roles.Any(AdminRoles.IsAdministrative))
            return;

        if (requirement.Permission is null)
        {
            context.Succeed(requirement);
            return;
        }

        var permissions = await permissionService.GetEffectivePermissionsAsync(userId, ct);
        if (permissions.Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}

public sealed class AdminCsrfEndpointFilter : IEndpointFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Options
    };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!SafeMethods.Contains(http.Request.Method) &&
            http.Items.TryGetValue(AdminAuthConstants.AuthViaCookieItemKey, out var viaCookie) &&
            viaCookie is true)
        {
            var cookieToken = http.Request.Cookies[AdminAuthConstants.CsrfCookieName];
            var headerToken = http.Request.Headers[AdminAuthConstants.CsrfHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(cookieToken) ||
                string.IsNullOrWhiteSpace(headerToken) ||
                !string.Equals(cookieToken, headerToken, StringComparison.Ordinal))
            {
                return ApiSupport.Problem("CSRF_VALIDATION_FAILED", 403, "Mã xác thực CSRF không hợp lệ hoặc bị thiếu.");
            }
        }
        return await next(context);
    }
}

public static class AdminPolicyExtensions
{
    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<AdminPermissionService>();
        services.AddScoped<AdminAuditService>();
        services.AddScoped<AdminSettingsService>();
        services.AddSingleton<IAuthorizationHandler, AdminPermissionHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminAuthConstants.AdminAccessPolicy, p =>
            {
                p.RequireAuthenticatedUser();
                p.AddRequirements(new AdminPermissionRequirement(null));
            });

            foreach (var permission in AdminPermissions.All)
            {
                options.AddPolicy(AdminAuthConstants.PolicyFor(permission), p =>
                {
                    p.RequireAuthenticatedUser();
                    p.AddRequirements(new AdminPermissionRequirement(permission));
                });
            }
        });

        return services;
    }
}
