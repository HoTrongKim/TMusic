using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Api.Admin;

public static class AdminDashboardEndpoints
{
    public static void MapAdminDashboardEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/dashboard", GetDashboard)
            .RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AnalyticsRead));

        admin.MapGet("/analytics", GetAnalytics)
            .RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AnalyticsRead));

        admin.MapGet("/analytics/export", ExportAnalyticsCsv)
            .RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AnalyticsRead));
    }

    private static (DateTimeOffset Start, DateTimeOffset End, string RangeKey) ResolveRange(
        string? range,
        string? from,
        string? to,
        DateTimeOffset now)
    {
        var key = (range ?? "30d").Trim().ToLowerInvariant();
        if (key == "custom" &&
            DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var customFrom) &&
            DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var customTo) &&
            customTo > customFrom &&
            (customTo - customFrom).TotalDays <= 366)
        {
            return (customFrom.ToUniversalTime(), customTo.ToUniversalTime(), "custom");
        }

        return key switch
        {
            "24h" => (now.AddHours(-24), now, "24h"),
            "7d" => (now.AddDays(-7), now, "7d"),
            "90d" => (now.AddDays(-90), now, "90d"),
            _ => (now.AddDays(-30), now, "30d")
        };
    }

    private static async Task<IResult> GetDashboard(
        string? range,
        string? from,
        string? to,
        TMusicDb db,
        IAudioStorage audioStorage,
        ICoverImageStorage coverStorage,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var (start, end, rangeKey) = ResolveRange(range, from, to, now);

        var totalAccounts = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null, ct);
        var newUsersToday = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.CreatedAt >= todayStart, ct);
        var activeUsers7d = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.LastActiveAt >= now.AddDays(-7), ct);
        var activeUsers30d = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.LastActiveAt >= now.AddDays(-30), ct);
        var totalAlbums = await db.Albums.AsNoTracking().CountAsync(a => a.DeletedAt == null, ct);
        var totalSavedSongs = await db.UserLibrarySongs.AsNoTracking().CountAsync(ct);
        var serverStoredSongs = await db.UserLibrarySongs.AsNoTracking().CountAsync(s => s.AudioStatus == ImportStatus.Ready && s.AudioFileName != null, ct);

        var totalDownloadJobs = await db.AudioDownloadJobs.AsNoTracking().CountAsync(ct);
        var completedDownloads = await db.AudioDownloadJobs.AsNoTracking().CountAsync(j => j.Status == ImportStatus.Complete, ct);
        var failedDownloads = await db.AudioDownloadJobs.AsNoTracking().CountAsync(j => j.Status == ImportStatus.Failed, ct);
        var activeDownloads = await db.AudioDownloadJobs.AsNoTracking().CountAsync(j => j.Status == ImportStatus.Queued || j.Status == ImportStatus.Running, ct);

        var audioFiles = audioStorage.ListAllFiles();
        var coverFiles = coverStorage.ListAllFiles();
        var serverAudioBytes = audioFiles.Sum(f => f.SizeBytes);
        var serverCoverBytes = coverFiles.Sum(f => f.SizeBytes);
        var totalServerFileBytes = serverAudioBytes + serverCoverBytes;

        // SQL aggregated time-series (no full table scan in memory)
        var signupsByDay = await db.Users.AsNoTracking()
            .Where(u => u.DeletedAt == null && u.CreatedAt >= start && u.CreatedAt <= end)
            .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month, u.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);

        var downloadsByDay = await db.AudioDownloadJobs.AsNoTracking()
            .Where(j => j.CreatedAt >= start && j.CreatedAt <= end)
            .GroupBy(j => new { j.CreatedAt.Year, j.CreatedAt.Month, j.CreatedAt.Day })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                Total = g.Count(),
                Succeeded = g.Count(x => x.Status == ImportStatus.Complete),
                Failed = g.Count(x => x.Status == ImportStatus.Failed),
                Canceled = g.Count(x => x.Status == ImportStatus.Canceled)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);

        var activeByDay = await db.Users.AsNoTracking()
            .Where(u => u.DeletedAt == null && u.LastActiveAt != null && u.LastActiveAt >= start && u.LastActiveAt <= end)
            .GroupBy(u => new { u.LastActiveAt!.Value.Year, u.LastActiveAt!.Value.Month, u.LastActiveAt!.Value.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);

        var providerBreakdown = await db.Songs.AsNoTracking()
            .Where(s => s.CreatedAt >= start && s.CreatedAt <= end)
            .GroupBy(s => s.Provider)
            .Select(g => new { Provider = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        var rangeDownloadCounts = await db.AudioDownloadJobs.AsNoTracking()
            .Where(j => j.CreatedAt >= start && j.CreatedAt <= end)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var openModerationFlags = await db.ContentModerationFlags.AsNoTracking().CountAsync(f => f.Status == "OPEN", ct);
        var recentErrors24h = await db.SystemEvents.AsNoTracking().CountAsync(e => (e.Severity == "ERROR" || e.Severity == "CRITICAL") && e.CreatedAt >= now.AddHours(-24), ct);

        return Results.Ok(new
        {
            range = rangeKey,
            from = start,
            to = end,
            generatedAt = now,
            kpis = new
            {
                totalAccounts = new { value = totalAccounts, source = "AspNetUsers (DeletedAt IS NULL)" },
                newUsersToday = new { value = newUsersToday, source = "AspNetUsers.CreatedAt >= UTC 00:00" },
                activeUsers7d = new { value = activeUsers7d, source = "AspNetUsers.LastActiveAt >= now - 7d" },
                activeUsers30d = new { value = activeUsers30d, source = "AspNetUsers.LastActiveAt >= now - 30d" },
                totalAlbums = new { value = totalAlbums, source = "Albums (DeletedAt IS NULL)" },
                totalSavedSongs = new { value = totalSavedSongs, source = "UserLibrarySongs (tất cả bản ghi thư viện đồng bộ)" },
                serverStoredMp3Count = new { value = serverStoredSongs, source = "UserLibrarySongs (AudioStatus = READY trên server; không bao gồm tệp chỉ lưu trên máy người dùng)" },
                totalDownloadJobs = new { value = totalDownloadJobs, source = "AudioDownloadJobs (tổng số tác vụ server đã tiếp nhận)" },
                completedDownloads = new { value = completedDownloads, source = "AudioDownloadJobs (Status = COMPLETE)" },
                failedDownloads = new { value = failedDownloads, source = "AudioDownloadJobs (Status = FAILED)" },
                activeDownloads = new { value = activeDownloads, source = "AudioDownloadJobs (Status IN QUEUED, RUNNING)" },
                serverStorageBytes = new { value = totalServerFileBytes, audioBytes = serverAudioBytes, coverBytes = serverCoverBytes, audioFileCount = audioFiles.Count, coverFileCount = coverFiles.Count, source = "Đo trực tiếp thư mục Storage:AudioPath + Storage:CoversPath trên server (không gồm file local trên điện thoại)" }
            },
            alerts = new
            {
                openModerationFlags,
                recentErrors24h
            },
            charts = new
            {
                registrationsByDay = signupsByDay.Select(x => new { date = $"{x.Year:D4}-{x.Month:D2}-{x.Day:D2}", count = x.Count }),
                downloadsByDay = downloadsByDay.Select(x => new { date = $"{x.Year:D4}-{x.Month:D2}-{x.Day:D2}", total = x.Total, succeeded = x.Succeeded, failed = x.Failed, canceled = x.Canceled }),
                downloadStatusRatio = rangeDownloadCounts.Select(x => new { status = x.Status, count = x.Count }),
                sourceDistribution = providerBreakdown.Select(x => new { provider = x.Provider, count = x.Count }),
                activeUsersByDay = activeByDay.Select(x => new { date = $"{x.Year:D4}-{x.Month:D2}-{x.Day:D2}", count = x.Count }),
                storageHistory = new
                {
                    available = false,
                    reason = "Chưa có dữ liệu chuỗi thời gian dung lượng lịch sử (chỉ đo dung lượng thời điểm hiện tại)."
                }
            }
        });
    }

    private static async Task<IResult> GetAnalytics(
        string? range,
        string? from,
        string? to,
        TMusicDb db,
        IAudioStorage audioStorage,
        ICoverImageStorage coverStorage,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (start, end, rangeKey) = ResolveRange(range, from, to, now);

        var newUsers = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.CreatedAt >= start && u.CreatedAt <= end, ct);
        var activeUsers = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.LastActiveAt >= start && u.LastActiveAt <= end, ct);
        var returningUsers = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.CreatedAt < start && u.LastActiveAt >= start && u.LastActiveAt <= end, ct);

        var passwordUsers = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.PasswordHash != null, ct);
        var externalMethods = await db.UserAuthProviders.AsNoTracking()
            .GroupBy(p => p.Provider)
            .Select(g => new { provider = g.Key, count = g.Select(x => x.UserId).Distinct().Count() })
            .ToListAsync(ct);

        var songsSavedInRange = await db.UserLibrarySongs.AsNoTracking().CountAsync(s => s.CreatedAt >= start && s.CreatedAt <= end, ct);
        var albumsCreatedInRange = await db.Albums.AsNoTracking().CountAsync(a => a.DeletedAt == null && a.CreatedAt >= start && a.CreatedAt <= end, ct);
        var jobsInRange = await db.AudioDownloadJobs.AsNoTracking()
            .Where(j => j.CreatedAt >= start && j.CreatedAt <= end)
            .Select(j => new { j.Status, j.Provider, j.CreatedAt, j.CompletedAt })
            .ToListAsync(ct);

        var completedCount = jobsInRange.Count(j => j.Status == ImportStatus.Complete);
        var failedCount = jobsInRange.Count(j => j.Status == ImportStatus.Failed);
        var finishedCount = completedCount + failedCount;
        double? downloadFailureRate = finishedCount > 0 ? Math.Round(failedCount * 100.0 / finishedCount, 2) : null;

        var durations = jobsInRange
            .Where(j => j.Status == ImportStatus.Complete && j.CompletedAt is not null && j.CompletedAt >= j.CreatedAt)
            .Select(j => (j.CompletedAt!.Value - j.CreatedAt).TotalSeconds)
            .ToList();
        double? avgProcessingSeconds = durations.Count > 0 ? Math.Round(durations.Average(), 2) : null;

        var sourceDist = jobsInRange
            .GroupBy(j => j.Provider)
            .Select(g => new { provider = g.Key, total = g.Count(), completed = g.Count(x => x.Status == ImportStatus.Complete), failed = g.Count(x => x.Status == ImportStatus.Failed) })
            .OrderByDescending(x => x.total)
            .ToList();

        var apiErrorEvents = await db.SystemEvents.AsNoTracking()
            .CountAsync(e => e.EventType == "API_ERROR" && e.CreatedAt >= start && e.CreatedAt <= end, ct);
        var totalEvents = await db.SystemEvents.AsNoTracking()
            .CountAsync(e => e.CreatedAt >= start && e.CreatedAt <= end, ct);

        var serverAudioBytes = audioStorage.ListAllFiles().Sum(f => f.SizeBytes);
        var serverCoverBytes = coverStorage.ListAllFiles().Sum(f => f.SizeBytes);

        return Results.Ok(new
        {
            range = rangeKey,
            from = start,
            to = end,
            userAnalytics = new
            {
                newUsers,
                activeUsers,
                returningUsers,
                registrationMethods = new object[]
                {
                    new { method = "email_password", count = passwordUsers }
                }.Concat(externalMethods.Select(x => (object)new { method = x.provider, count = x.count }))
            },
            musicAnalytics = new
            {
                songsSaved = songsSavedInRange,
                albumsCreated = albumsCreatedInRange,
                downloadJobs = jobsInRange.Count,
                completedDownloads = completedCount,
                failedDownloads = failedCount,
                sourceDistribution = sourceDist
            },
            infrastructureAnalytics = new
            {
                apiErrorEventsCount = apiErrorEvents,
                apiErrorRateNote = "Chưa có bộ đếm tổng HTTP requests dài hạn; hiển thị số sự kiện API_ERROR thực tế đã ghi nhận.",
                totalSystemEvents = totalEvents,
                downloadFailureRatePercent = downloadFailureRate,
                avgJobProcessingSeconds = avgProcessingSeconds,
                serverStorageBytes = serverAudioBytes + serverCoverBytes,
                serverAudioBytes,
                serverCoverBytes
            }
        });
    }

    private static async Task<IResult> ExportAnalyticsCsv(
        string? dataset,
        string? range,
        string? from,
        string? to,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (start, end, rangeKey) = ResolveRange(range, from, to, now);
        var type = (dataset ?? "downloads").Trim().ToLowerInvariant();
        const int maxRows = 2000;

        var sb = new StringBuilder();
        var rowCount = 0;

        switch (type)
        {
            case "downloads":
                sb.AppendLine("JobId,Provider,Status,BitrateKbps,OutputSizeBytes,ErrorCode,Failures,CreatedAtUtc,CompletedAtUtc");
                var jobs = await db.AudioDownloadJobs.AsNoTracking()
                    .Where(j => j.CreatedAt >= start && j.CreatedAt <= end)
                    .OrderByDescending(j => j.CreatedAt)
                    .Take(maxRows)
                    .ToListAsync(ct);
                foreach (var j in jobs)
                {
                    sb.AppendLine(string.Join(',',
                        Csv(j.Id.ToString("D")), Csv(j.Provider), Csv(j.Status),
                        Csv(j.BitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? ""),
                        Csv(j.OutputSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? ""),
                        Csv(j.ErrorCode ?? ""), Csv(j.Failures.ToString(CultureInfo.InvariantCulture)),
                        Csv(j.CreatedAt.ToString("O")), Csv(j.CompletedAt?.ToString("O") ?? "")));
                    rowCount++;
                }
                break;

            case "registrations":
                sb.AppendLine("DateUtc,NewUsers");
                var days = await db.Users.AsNoTracking()
                    .Where(u => u.DeletedAt == null && u.CreatedAt >= start && u.CreatedAt <= end)
                    .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month, u.CreatedAt.Day })
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
                    .Take(maxRows)
                    .ToListAsync(ct);
                foreach (var d in days)
                {
                    sb.AppendLine($"{d.Year:D4}-{d.Month:D2}-{d.Day:D2},{d.Count}");
                    rowCount++;
                }
                break;

            case "sources":
                sb.AppendLine("Provider,TotalSongs");
                var providers = await db.Songs.AsNoTracking()
                    .Where(s => s.CreatedAt >= start && s.CreatedAt <= end)
                    .GroupBy(s => s.Provider)
                    .Select(g => new { Provider = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToListAsync(ct);
                foreach (var p in providers)
                {
                    sb.AppendLine($"{Csv(p.Provider)},{p.Count}");
                    rowCount++;
                }
                break;

            default:
                return ApiSupport.Problem("INVALID_EXPORT_DATASET", 400, "Dataset hỗ trợ: downloads, registrations, sources.");
        }

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ADMIN_EXPORT_CSV", "ANALYTICS", type, "SUCCESS",
            $"Xuất báo cáo CSV ({type}, phạm vi {rangeKey}, {rowCount} dòng).", new { dataset = type, range = rangeKey, rowCount }, ct);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", $"tmusic-{type}-{rangeKey}-{now:yyyyMMdd}.csv");
    }

    private static string Csv(string value)
    {
        // Guard against CSV formula injection in Excel/Sheets
        var safe = value;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}
