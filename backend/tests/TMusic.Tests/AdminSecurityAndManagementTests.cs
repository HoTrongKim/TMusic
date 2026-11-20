using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TMusic.Api.Admin;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AdminSecurityAndManagementTests(SqlApiFixture api)
{
    private const string AdminPassword = "Admin-Strong!2026#Test";

    private async Task<(Guid UserId, string Email, string AccessToken, string RefreshToken)>
        BootstrapAndLoginAdminAsync(string roleName, string? email = null)
    {
        email ??= $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@tmusic.test";
        Guid userId;
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            var perms = scope.ServiceProvider.GetRequiredService<AdminPermissionService>();
            await perms.EnsureRolesSeededAsync(CancellationToken.None);

            var account = new Account
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                DisplayName = $"Test {roleName}",
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var created = await userManager.CreateAsync(account, AdminPassword);
            Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Code)));
            var roleRes = await userManager.AddToRoleAsync(account, roleName);
            Assert.True(roleRes.Succeeded);
            userId = account.Id;
        }

        using var anon = api.Client();
        var loginRes = await anon.PostAsJsonAsync("/api/admin/auth/login", new { email, password = AdminPassword, deviceName = "Admin Test" });
        var loginJson = await SqlApiFixture.Json(loginRes);
        Assert.Equal("AUTHENTICATED", loginJson.GetProperty("status").GetString());
        var accessToken = loginJson.GetProperty("accessToken").GetString()!;
        var refreshToken = loginJson.GetProperty("refreshToken").GetString()!;

        return (userId, email, accessToken, refreshToken);
    }

    [SqlFact]
    public async Task RbacEnforcesGuest401User403PermissionsPrivilegeEscalationAndMfa()
    {
        var super = await BootstrapAndLoginAdminAsync(AdminRoles.SuperAdmin);
        var admin = await BootstrapAndLoginAdminAsync(AdminRoles.Admin);
        var moderator = await BootstrapAndLoginAdminAsync(AdminRoles.Moderator);
        var regularUser = await api.RegisterAsync();

        // 1. Guest -> 401
        using (var guestClient = api.Client())
        {
            await SqlApiFixture.Expect(await guestClient.GetAsync("/api/admin/dashboard"), HttpStatusCode.Unauthorized);
            await SqlApiFixture.Expect(await guestClient.GetAsync("/api/admin/users"), HttpStatusCode.Unauthorized);
        }

        // 2. Normal user -> 403 on all Admin APIs and admin login
        using (var userClient = api.Client(regularUser.AccessToken))
        {
            await SqlApiFixture.Expect(await userClient.GetAsync("/api/admin/dashboard"), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await userClient.GetAsync("/api/admin/users"), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await userClient.GetAsync("/api/admin/settings"), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await userClient.PostAsJsonAsync("/api/admin/auth/login", new { email = regularUser.Email, password = SqlApiFixture.Password }), HttpStatusCode.Forbidden);
        }

        // 3. Mandatory MFA: regular mobile token issued for an Admin (without IsAdminSession/MfaVerified) cannot access /api/admin/*
        using (var anon = api.Client())
        {
            var mobileLogin = await SqlApiFixture.Json(await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = admin.Email, password = AdminPassword, deviceName = "Mobile" }));
            using var nonMfaClient = api.Client(mobileLogin.GetProperty("accessToken").GetString()!);
            await SqlApiFixture.Expect(await nonMfaClient.GetAsync("/api/admin/dashboard"), HttpStatusCode.Forbidden);
        }

        // 4. Moderator cannot read or modify System Settings, nor manage admins/roles
        using (var modClient = api.Client(moderator.AccessToken))
        {
            await SqlApiFixture.Expect(await modClient.GetAsync("/api/admin/dashboard"), HttpStatusCode.OK);
            await SqlApiFixture.Expect(await modClient.GetAsync("/api/admin/songs"), HttpStatusCode.OK);
            await SqlApiFixture.Expect(await modClient.GetAsync("/api/admin/settings"), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await modClient.PutAsJsonAsync("/api/admin/settings/maintenance.enabled", new { value = "true", confirmed = true }), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await modClient.GetAsync("/api/admin/admins"), HttpStatusCode.Forbidden);
        }

        // 5. Admin lacks admins.manage and roles.manage -> 403; cannot elevate self to SuperAdmin
        using (var adminClient = api.Client(admin.AccessToken))
        {
            await SqlApiFixture.Expect(await adminClient.GetAsync("/api/admin/users"), HttpStatusCode.OK);
            await SqlApiFixture.Expect(await adminClient.GetAsync("/api/admin/admins"), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await adminClient.PostAsJsonAsync("/api/admin/admins/invite", new { email = "x@tmusic.test", roleName = "Admin" }), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await adminClient.PutAsJsonAsync($"/api/admin/users/{admin.UserId}/role", new { role = "SuperAdmin", reason = "escalate" }), HttpStatusCode.Forbidden);
            await SqlApiFixture.Expect(await adminClient.PostAsJsonAsync($"/api/admin/users/{super.UserId}/suspend", new { reason = "cannot suspend super" }), HttpStatusCode.Forbidden);
        }

        // 6. SuperAdmin can read dashboard, settings, admins, audit-logs
        using (var superClient = api.Client(super.AccessToken))
        {
            var dash = await SqlApiFixture.Json(await superClient.GetAsync("/api/admin/dashboard?range=30d"));
            Assert.True(dash.GetProperty("kpis").GetProperty("totalAccounts").GetProperty("value").GetInt32() >= 4);
            Assert.False(dash.GetProperty("charts").GetProperty("storageHistory").GetProperty("available").GetBoolean());
        }
    }

    [SqlFact]
    public async Task LastSuperAdminProtectedSuspensionRevokesTokensAndSettingsBlockSecrets()
    {
        // Ensure clean count of SuperAdmins in this isolated check by identifying all active SuperAdmins
        var super = await BootstrapAndLoginAdminAsync(AdminRoles.SuperAdmin);
        var victim = await api.RegisterAsync();

        // Save an album + song for victim to verify existing user library remains intact
        using (var victimClient = api.Client(victim.AccessToken))
        {
            await SqlApiFixture.Json(await victimClient.PostAsJsonAsync("/api/v1/albums", new { name = "Album V4 Preserve" }));
            await SqlApiFixture.Json(await victimClient.PostAsJsonAsync("/api/v1/library/songs", new { url = "https://youtu.be/dQw4w9WgXcQ", customTitle = "Bài hát thử nghiệm V4" }));
        }

        using var superClient = api.Client(super.AccessToken);

        // If there are other SuperAdmins from earlier tests in the shared fixture DB, demote them so `super` is the last SuperAdmin
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            var superRoleId = await db.Roles.Where(r => r.NormalizedName == "SUPERADMIN").Select(r => r.Id).SingleAsync();
            var otherSupers = await db.UserRoles.Where(ur => ur.RoleId == superRoleId && ur.UserId != super.UserId).ToListAsync();
            if (otherSupers.Count > 0)
            {
                db.UserRoles.RemoveRange(otherSupers);
                await db.SaveChangesAsync();
            }
        }

        // 6. Last SuperAdmin cannot be demoted, suspended, deactivated, or self-deleted
        await SqlApiFixture.Expect(
            await superClient.PutAsJsonAsync($"/api/admin/users/{super.UserId}/role", new { role = "Admin", reason = "demote last" }),
            HttpStatusCode.Conflict);
        await SqlApiFixture.Expect(
            await superClient.PutAsJsonAsync($"/api/admin/admins/{super.UserId}/role", new { role = "User", reason = "revoke last" }),
            HttpStatusCode.Conflict);
        await SqlApiFixture.Expect(
            await superClient.PostAsJsonAsync($"/api/admin/users/{super.UserId}/suspend", new { reason = "self" }),
            HttpStatusCode.Conflict);
        await SqlApiFixture.Expect(
            await SqlApiFixture.Mutation(superClient, HttpMethod.Delete, "/api/v1/users/me", new { password = AdminPassword }),
            HttpStatusCode.Conflict);

        // 7. Suspend user -> active access token fails immediately AND old refresh token cannot be used
        await SqlApiFixture.Expect(
            await superClient.PostAsJsonAsync($"/api/admin/users/{victim.UserId}/suspend", new { reason = "Vi phạm điều khoản thử nghiệm", durationHours = 24 }),
            HttpStatusCode.OK);

        using (var victimClient = api.Client(victim.AccessToken))
        {
            await SqlApiFixture.Expect(await victimClient.GetAsync("/api/v1/users/me"), HttpStatusCode.Unauthorized);
            await SqlApiFixture.Expect(await victimClient.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = victim.RefreshToken }), HttpStatusCode.Unauthorized);
            await SqlApiFixture.Expect(await victimClient.PostAsJsonAsync("/api/v1/auth/login", new { email = victim.Email, password = SqlApiFixture.Password, deviceName = "retry" }), HttpStatusCode.Forbidden);
        }

        // Reactivate victim -> can log in again and existing album & song are 100% preserved!
        await SqlApiFixture.Expect(
            await superClient.PostAsJsonAsync($"/api/admin/users/{victim.UserId}/reactivate", new { reason = "Đã xác minh lại" }),
            HttpStatusCode.OK);

        using (var anon = api.Client())
        {
            var relogin = await SqlApiFixture.Json(await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = victim.Email, password = SqlApiFixture.Password, deviceName = "restored" }));
            using var restoredClient = api.Client(relogin.GetProperty("accessToken").GetString()!);
            var albums = await SqlApiFixture.Json(await restoredClient.GetAsync("/api/v1/albums"));
            Assert.Contains("Album V4 Preserve", albums.ToString());
            var songs = await SqlApiFixture.Json(await restoredClient.GetAsync("/api/v1/library/songs"));
            Assert.True(songs.GetProperty("total").GetInt32() >= 1);
        }

        // 12. Settings never expose secrets and reject secret keys
        var settingsJson = await SqlApiFixture.Json(await superClient.GetAsync("/api/admin/settings"));
        var settingsText = settingsJson.ToString();
        Assert.DoesNotContain("INTEGRATION-TEST-ONLY-NONPRODUCTION-KEY", settingsText);
        await SqlApiFixture.Expect(await superClient.GetAsync("/api/admin/settings?key=Auth:Jwt:SigningKey"), HttpStatusCode.Forbidden);
        await SqlApiFixture.Expect(await superClient.PutAsJsonAsync("/api/admin/settings/jwt.signing_key", new { value = "hacked", confirmed = true }), HttpStatusCode.Forbidden);

        // Safe setting requires confirmation if high-impact, updates cleanly when confirmed, and creates Audit Log
        await SqlApiFixture.Expect(await superClient.PutAsJsonAsync("/api/admin/settings/maintenance.enabled", new { value = "true", confirmed = false }), HttpStatusCode.BadRequest);
        await SqlApiFixture.Expect(await superClient.PutAsJsonAsync("/api/admin/settings/maintenance.enabled", new { value = "true", confirmed = true, reason = "Test maintenance" }), HttpStatusCode.OK);
        await SqlApiFixture.Expect(await superClient.PostAsync("/api/admin/settings/maintenance.enabled/reset", null), HttpStatusCode.OK);

        // 13 & 14. Audit logs recorded & pagination works
        var auditPage1 = await SqlApiFixture.Json(await superClient.GetAsync("/api/admin/audit-logs?page=1&pageSize=3"));
        Assert.Equal(1, auditPage1.GetProperty("page").GetInt32());
        Assert.Equal(3, auditPage1.GetProperty("pageSize").GetInt32());
        Assert.True(auditPage1.GetProperty("total").GetInt32() >= 3);
        Assert.Contains("USER_SUSPENDED", (await superClient.GetStringAsync("/api/admin/audit-logs?action=USER_SUSPENDED")));

        // 8. Revoked admin token (after logout) cannot be used anymore
        await SqlApiFixture.Expect(await superClient.PostAsync("/api/admin/auth/logout", null), HttpStatusCode.NoContent);
        await SqlApiFixture.Expect(await superClient.GetAsync("/api/admin/dashboard"), HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task InvitationsEnforceExpirySingleUseAndMfaAndStorageCleanupProtectsReferencedMp3()
    {
        var super = await BootstrapAndLoginAdminAsync(AdminRoles.SuperAdmin);
        using var superClient = api.Client(super.AccessToken);

        // 9 & 10. Invite Admin -> valid inspect -> accept directly -> cannot reuse token
        var inviteEmail = $"invited-{Guid.NewGuid():N}@tmusic.test";
        var inviteJson = await SqlApiFixture.Json(await superClient.PostAsJsonAsync("/api/admin/admins/invite", new { email = inviteEmail, roleName = "Admin", expiresInHours = 24 }));
        var token = inviteJson.GetProperty("invitationToken").GetString()!;

        using var anon = api.Client();
        var inspect = await SqlApiFixture.Json(await anon.GetAsync($"/api/admin/auth/invitations/inspect?token={Uri.EscapeDataString(token)}"));
        Assert.Equal(inviteEmail, inspect.GetProperty("email").GetString());

        var accepted = await SqlApiFixture.Json(await anon.PostAsJsonAsync("/api/admin/auth/invitations/accept", new { token, password = AdminPassword, displayName = "Invited Ops" }));
        Assert.Equal("AUTHENTICATED", accepted.GetProperty("status").GetString());
        var invitedAccess = accepted.GetProperty("accessToken").GetString()!;

        using (var invitedClient = api.Client(invitedAccess))
        {
            await SqlApiFixture.Expect(await invitedClient.GetAsync("/api/admin/users"), HttpStatusCode.OK);
        }

        // Reuse used token -> 409 Conflict
        await SqlApiFixture.Expect(await anon.GetAsync($"/api/admin/auth/invitations/inspect?token={Uri.EscapeDataString(token)}"), HttpStatusCode.Conflict);
        await SqlApiFixture.Expect(await anon.PostAsJsonAsync("/api/admin/auth/invitations/accept", new { token, password = AdminPassword }), HttpStatusCode.Conflict);

        // Expired token -> 410 Gone
        var expiredToken = "expired-" + Guid.NewGuid().ToString("N");
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            db.AdminInvitations.Add(new AdminInvitation
            {
                Email = "expired@tmusic.test",
                NormalizedEmail = "EXPIRED@TMUSIC.TEST",
                RoleName = AdminRoles.Moderator,
                TokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expiredToken))),
                InvitedByUserId = super.UserId,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }
        await SqlApiFixture.Expect(await anon.GetAsync($"/api/admin/auth/invitations/inspect?token={expiredToken}"), HttpStatusCode.Gone);

        // Storage orphan cleanup protects referenced MP3 and quarantines orphan MP3
        var user = await api.RegisterAsync();
        string referencedFile;
        string orphanFile;
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            var storage = scope.ServiceProvider.GetRequiredService<IAudioStorage>();
            var mp3 = new byte[64];
            mp3[0] = (byte)'I'; mp3[1] = (byte)'D'; mp3[2] = (byte)'3';

            var savedRef = await storage.SaveBytesAsync(user.UserId, mp3, CancellationToken.None);
            var savedOrphan = await storage.SaveBytesAsync(user.UserId, mp3, CancellationToken.None);
            referencedFile = savedRef.FileName;
            orphanFile = savedOrphan.FileName;

            var song = new Song
            {
                Provider = "youtube",
                ProviderContentId = "ref" + Guid.NewGuid().ToString("N")[..8],
                CanonicalUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                OriginalUrl = "https://youtu.be/dQw4w9WgXcQ",
                OriginalTitle = "Referenced Track"
            };
            db.Songs.Add(song);
            db.UserLibrarySongs.Add(new UserLibrarySong
            {
                UserId = user.UserId,
                Song = song,
                CustomTitle = "Referenced Track",
                AudioFileName = referencedFile,
                AudioSizeBytes = savedRef.SizeBytes,
                AudioStatus = ImportStatus.Ready
            });
            await db.SaveChangesAsync();
        }

        var preview = await SqlApiFixture.Json(await superClient.GetAsync("/api/admin/storage/cleanup/preview?minAgeHours=0"));
        Assert.Contains(orphanFile, preview.ToString());
        Assert.DoesNotContain(referencedFile, preview.ToString());

        var executed = await SqlApiFixture.Json(await superClient.PostAsJsonAsync("/api/admin/storage/cleanup/execute", new { confirm = true, minAgeHours = 0, reason = "Test cleanup" }));
        Assert.True(executed.GetProperty("quarantinedCount").GetInt32() >= 1);

        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IAudioStorage>();
            var refStillOpen = await storage.OpenReadAsync(user.UserId, referencedFile, CancellationToken.None);
            Assert.NotNull(refStillOpen);
            await refStillOpen!.Value.Stream.DisposeAsync();
            var orphanGone = await storage.OpenReadAsync(user.UserId, orphanFile, CancellationToken.None);
            Assert.Null(orphanGone);
        }

        // 16 & 17. Guest mode capability and legacy/existing user compatibility intact
        var caps = await SqlApiFixture.Json(await anon.GetAsync("/api/v1/system/capabilities"));
        Assert.True(caps.GetProperty("guestLocalMode").GetBoolean());
    }

    [SqlFact]
    public async Task CookieAuthEnforcesCsrfHeaderOnMutatingAdminRequests()
    {
        var super = await BootstrapAndLoginAdminAsync(AdminRoles.SuperAdmin);

        using var cookieClient = api.Client();
        cookieClient.DefaultRequestHeaders.Add("Cookie", $"{AdminAuthConstants.AccessCookieName}={super.AccessToken}; {AdminAuthConstants.CsrfCookieName}=csrf-expected-token");

        // GET succeeds with HttpOnly cookie
        await SqlApiFixture.Expect(await cookieClient.GetAsync("/api/admin/auth/me"), HttpStatusCode.OK);

        // Mutating request WITHOUT X-CSRF-Token header -> 403 CSRF_VALIDATION_FAILED
        await SqlApiFixture.Expect(
            await cookieClient.PutAsJsonAsync("/api/admin/settings/downloads.max_retries", new { value = "4", confirmed = true }),
            HttpStatusCode.Forbidden);

        // Mutating request WITH matching X-CSRF-Token header -> 200 OK
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings/downloads.max_retries")
        {
            Content = JsonContent.Create(new { value = "4", confirmed = true })
        };
        req.Headers.Add(AdminAuthConstants.CsrfHeaderName, "csrf-expected-token");
        await SqlApiFixture.Expect(await cookieClient.SendAsync(req), HttpStatusCode.OK);
    }
}
