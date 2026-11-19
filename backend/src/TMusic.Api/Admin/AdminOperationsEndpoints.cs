using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Api.Admin;

public static class AdminOperationsEndpoints
{
    public static void MapAdminOperationsEndpoints(this RouteGroupBuilder admin)
    {
        var downloads = admin.MapGroup("/downloads").AddEndpointFilter<AdminCsrfEndpointFilter>();
        downloads.MapGet("", ListDownloads).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.DownloadsRead));
        downloads.MapGet("/{id:guid}", GetDownloadDetail).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.DownloadsRead));
        downloads.MapPost("/{id:guid}/cancel", CancelDownload).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.DownloadsManage));
        downloads.MapPost("/{id:guid}/retry", RetryDownload).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.DownloadsManage));

        var storage = admin.MapGroup("/storage").AddEndpointFilter<AdminCsrfEndpointFilter>();
        storage.MapGet("", GetStorageOverview).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.StorageRead));
        storage.MapGet("/cleanup/preview", PreviewCleanup).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.StorageManage));
        storage.MapPost("/cleanup/execute", ExecuteCleanup).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.StorageManage));
    }

    private static string MapDisplayStatus(string raw) => raw switch
    {
        ImportStatus.Queued => "Queued",
        ImportStatus.Running => "Processing",
        ImportStatus.Complete => "Completed",
        ImportStatus.Failed => "Failed",
        ImportStatus.Canceled => "Cancelled",
        _ => raw
    };

    private static async Task<IResult> ListDownloads(
        string? status,
        string? provider,
        bool? failedOnly,
        int? page,
        int? pageSize,
        TMusicDb db,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, null))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var current = page ?? 1;
        var size = pageSize ?? 20;

        var query = db.AudioDownloadJobs.AsNoTracking().AsQueryable();

        if (failedOnly == true)
        {
            query = query.Where(j => j.Status == ImportStatus.Failed);
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            var mapped = status.Trim().ToUpperInvariant() switch
            {
                "PROCESSING" or "RUNNING" => ImportStatus.Running,
                "COMPLETED" or "COMPLETE" => ImportStatus.Complete,
                "CANCELLED" or "CANCELED" => ImportStatus.Canceled,
                "QUEUED" => ImportStatus.Queued,
                "FAILED" => ImportStatus.Failed,
                _ => status.Trim().ToUpperInvariant()
            };
            query = query.Where(j => j.Status == mapped);
        }

        if (!string.IsNullOrWhiteSpace(provider))
            query = query.Where(j => j.Provider == provider.Trim().ToLowerInvariant());

        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var platformStats = await db.AudioDownloadJobs.AsNoTracking()
            .GroupBy(j => j.Provider)
            .Select(g => new
            {
                provider = g.Key,
                total = g.Count(),
                queued = g.Count(x => x.Status == ImportStatus.Queued),
                processing = g.Count(x => x.Status == ImportStatus.Running),
                completed = g.Count(x => x.Status == ImportStatus.Complete),
                failed = g.Count(x => x.Status == ImportStatus.Failed),
                cancelled = g.Count(x => x.Status == ImportStatus.Canceled)
            })
            .OrderByDescending(x => x.total)
            .ToListAsync(ct);

        var items = jobs.Select(j =>
        {
            double? durationSec = j.CompletedAt is not null && j.CompletedAt >= j.CreatedAt
                ? Math.Round((j.CompletedAt.Value - j.CreatedAt).TotalSeconds, 2)
                : null;
            var retryable = j.Status == ImportStatus.Canceled ||
                (j.Status == ImportStatus.Failed && YtDlpAudioExtractor.IsRetryableErrorCode(j.ErrorCode));
            return new
            {
                j.Id,
                userId = j.OwnerId,
                songId = j.UserLibrarySongId,
                j.Provider,
                status = MapDisplayStatus(j.Status),
                rawStatus = j.Status,
                j.ProgressPercent,
                j.BitrateKbps,
                j.OutputSizeBytes,
                errorCode = j.ErrorCode,
                retryCount = j.Failures,
                retryable,
                canCancel = j.Status is ImportStatus.Queued or ImportStatus.Running,
                durationSeconds = durationSec,
                j.CreatedAt,
                j.CompletedAt
            };
        });

        return Results.Ok(new { items, total, page = current, pageSize = size, platformStats });
    }

    private static async Task<IResult> GetDownloadDetail(Guid id, TMusicDb db, CancellationToken ct)
    {
        var job = await db.AudioDownloadJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Results.NotFound();

        var entry = await db.UserLibrarySongs.AsNoTracking()
            .Include(x => x.Song)
            .FirstOrDefaultAsync(x => x.Id == job.UserLibrarySongId, ct);

        double? durationSec = job.CompletedAt is not null && job.CompletedAt >= job.CreatedAt
            ? Math.Round((job.CompletedAt.Value - job.CreatedAt).TotalSeconds, 2)
            : null;
        var retryable = job.Status == ImportStatus.Canceled ||
            (job.Status == ImportStatus.Failed && YtDlpAudioExtractor.IsRetryableErrorCode(job.ErrorCode));

        return Results.Ok(new
        {
            job.Id,
            userId = job.OwnerId,
            songId = job.UserLibrarySongId,
            songTitle = entry?.CustomTitle ?? entry?.Song.OriginalTitle,
            job.Provider,
            canonicalUrl = job.CanonicalUrl,
            status = MapDisplayStatus(job.Status),
            rawStatus = job.Status,
            job.ProgressPercent,
            job.BitrateKbps,
            job.OutputSizeBytes,
            errorCode = job.ErrorCode,
            retryCount = job.Failures,
            retryable,
            canCancel = job.Status is ImportStatus.Queued or ImportStatus.Running,
            durationSeconds = durationSec,
            job.CreatedAt,
            job.NextAttemptAt,
            job.CompletedAt
        });
    }

    private static async Task<IResult> CancelDownload(
        Guid id,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var job = await db.AudioDownloadJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Results.NotFound();
        if (job.Status is not (ImportStatus.Queued or ImportStatus.Running))
            return ApiSupport.Problem("JOB_NOT_ACTIVE", 409, "Chỉ có thể hủy tác vụ đang chờ hoặc đang xử lý.");

        var now = clock.GetUtcNow();
        job.Status = ImportStatus.Canceled;
        job.CompletedAt = now;
        job.LeaseOwner = null;
        job.LeaseUntil = null;

        await db.UserLibrarySongs
            .Where(s => s.Id == job.UserLibrarySongId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AudioStatus, x => x.AudioFileName == null ? "NONE" : ImportStatus.Ready)
                .SetProperty(x => x.UpdatedAt, now), ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "DOWNLOAD_JOB_CANCELLED", "DOWNLOAD_JOB", id.ToString("D"), "SUCCESS",
            "Hủy tác vụ tải xuống từ trang quản trị.", new { job.Provider, ownerId = job.OwnerId }, ct);

        return Results.Ok(new { job.Id, status = "Cancelled" });
    }

    private static async Task<IResult> RetryDownload(
        Guid id,
        ClaimsPrincipal principal,
        TMusicDb db,
        IAudioExtractor extractor,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!extractor.IsAvailable)
            return ApiSupport.Problem("AUDIO_DOWNLOAD_UNAVAILABLE", 503, "Bộ xử lý yt-dlp/FFmpeg hiện chưa sẵn sàng.");

        var job = await db.AudioDownloadJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Results.NotFound();
        if (job.Status is not (ImportStatus.Failed or ImportStatus.Canceled))
            return ApiSupport.Problem("JOB_NOT_RETRYABLE", 409, "Chỉ có thể thử lại tác vụ đã thất bại hoặc bị hủy.");
        if (job.Status == ImportStatus.Failed && !YtDlpAudioExtractor.IsRetryableErrorCode(job.ErrorCode))
            return ApiSupport.Problem(job.ErrorCode ?? "DOWNLOAD_NOT_RETRYABLE", 422, "Lỗi nguồn này không thuộc diện tự động thử lại.");

        var now = clock.GetUtcNow();
        job.Status = ImportStatus.Queued;
        job.ProgressPercent = 0;
        job.ErrorCode = null;
        job.CompletedAt = null;
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        job.NextAttemptAt = now;

        await db.UserLibrarySongs
            .Where(s => s.Id == job.UserLibrarySongId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AudioStatus, ImportStatus.Queued).SetProperty(x => x.UpdatedAt, now), ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "DOWNLOAD_JOB_RETRIED", "DOWNLOAD_JOB", id.ToString("D"), "SUCCESS",
            "Kích hoạt thử lại tác vụ tải xuống từ trang quản trị.", new { job.Provider, previousFailures = job.Failures }, ct);

        return Results.Accepted($"/api/admin/downloads/{job.Id}", new { job.Id, status = "Queued" });
    }

    private static async Task<IResult> GetStorageOverview(
        TMusicDb db,
        IAudioStorage audioStorage,
        ICoverImageStorage coverStorage,
        CancellationToken ct)
    {
        var audioFiles = audioStorage.ListAllFiles();
        var coverFiles = coverStorage.ListAllFiles();

        var referencedAudio = await db.UserLibrarySongs.AsNoTracking()
            .Where(s => s.AudioFileName != null)
            .Select(s => new { s.UserId, FileName = s.AudioFileName! })
            .ToListAsync(ct);
        var referencedAudioSet = referencedAudio.Select(x => $"{x.UserId:N}/{x.FileName}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencedCovers = await db.Albums.AsNoTracking()
            .Where(a => a.CoverImageUrl != null && a.CoverImageUrl.StartsWith("/api/v1/covers/"))
            .Select(a => new { a.OwnerId, Url = a.CoverImageUrl! })
            .ToListAsync(ct);
        var referencedCoverSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in referencedCovers)
        {
            if (CoverImageStorage.ExtractOwnedFileName(c.OwnerId, c.Url) is { } fileName)
                referencedCoverSet.Add($"{c.OwnerId:N}/{fileName}");
        }

        var orphanAudio = audioFiles.Where(f => !referencedAudioSet.Contains($"{f.UserId:N}/{f.FileName}")).ToList();
        var orphanCovers = coverFiles.Where(f => !referencedCoverSet.Contains($"{f.UserId:N}/{f.FileName}")).ToList();

        var quarantineAudioBytes = MeasureDirectoryBytes(Path.Combine(audioStorage.StorageRootPath, "_quarantine"));
        var quarantineCoverBytes = MeasureDirectoryBytes(Path.Combine(coverStorage.StorageRootPath, "_quarantine"));
        var tempDownloadBytes = MeasureMatchingTempBytes();

        long? sqlDatabaseBytes = null;
        string sqlMetricStatus = "AVAILABLE";
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CAST(SUM(CAST(size AS BIGINT)) * 8192 AS BIGINT) FROM sys.database_files";
            var scalar = await cmd.ExecuteScalarAsync(ct);
            if (scalar is not null && scalar != DBNull.Value)
                sqlDatabaseBytes = Convert.ToInt64(scalar);
        }
        catch
        {
            sqlMetricStatus = "UNAVAILABLE";
        }

        return Results.Ok(new
        {
            layers = new
            {
                sqlServerDatabase = new
                {
                    label = "Cơ sở dữ liệu SQL Server",
                    bytes = sqlDatabaseBytes,
                    status = sqlMetricStatus,
                    note = sqlDatabaseBytes is null ? "Chưa có dữ liệu (không đủ quyền đọc sys.database_files)." : "Đo từ sys.database_files của database hiện tại."
                },
                serverMp3Files = new
                {
                    label = "Tệp MP3 lưu trên server",
                    bytes = audioFiles.Sum(f => f.SizeBytes),
                    fileCount = audioFiles.Count,
                    orphanBytes = orphanAudio.Sum(f => f.SizeBytes),
                    orphanCount = orphanAudio.Count,
                    referencedCount = audioFiles.Count - orphanAudio.Count
                },
                serverCoverFiles = new
                {
                    label = "Ảnh bìa lưu trên server",
                    bytes = coverFiles.Sum(f => f.SizeBytes),
                    fileCount = coverFiles.Count,
                    orphanBytes = orphanCovers.Sum(f => f.SizeBytes),
                    orphanCount = orphanCovers.Count
                },
                objectStorage = new
                {
                    label = "Object Storage (S3/R2)",
                    bytes = (long?)null,
                    status = "NOT_CONFIGURED",
                    note = "Chưa có dữ liệu — hệ thống hiện lưu trên volume đĩa của backend."
                },
                tempAndQuarantine = new
                {
                    label = "Tệp tạm & Cách ly (Quarantine)",
                    tempBytes = tempDownloadBytes,
                    quarantineBytes = quarantineAudioBytes + quarantineCoverBytes,
                    totalBytes = tempDownloadBytes + quarantineAudioBytes + quarantineCoverBytes
                },
                userDeviceFiles = new
                {
                    label = "Tệp trên điện thoại người dùng",
                    bytes = (long?)null,
                    status = "LOCAL_ONLY",
                    note = "Lưu cục bộ trên thiết bị người dùng; server không thu thập và không thể truy cập."
                }
            },
            growthHistory = new
            {
                available = false,
                note = "Chưa có dữ liệu lịch sử tăng trưởng dung lượng."
            }
        });
    }

    private static async Task<IResult> PreviewCleanup(
        int? minAgeHours,
        TMusicDb db,
        IAudioStorage audioStorage,
        ICoverImageStorage coverStorage,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ageHours = Math.Clamp(minAgeHours ?? 24, 0, 8760);
        var cutoff = clock.GetUtcNow().AddHours(-ageHours);

        var candidates = await FindUnreferencedFilesAsync(db, audioStorage, coverStorage, cutoff, ct);
        return Results.Ok(new
        {
            minAgeHours = ageHours,
            cutoffUtc = cutoff,
            candidateCount = candidates.Count,
            reclaimableBytes = candidates.Sum(c => c.SizeBytes),
            note = "Chỉ liệt kê tệp KHÔNG còn bất kỳ tham chiếu nào trong cơ sở dữ liệu. Tệp còn được người dùng tham chiếu không bao giờ bị đưa vào danh sách dọn.",
            items = candidates.Take(200).Select(c => new
            {
                c.Category,
                c.UserId,
                c.FileName,
                c.SizeBytes,
                c.LastModifiedUtc
            })
        });
    }

    private static async Task<IResult> ExecuteCleanup(
        CleanupExecuteRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        IAudioStorage audioStorage,
        ICoverImageStorage coverStorage,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!req.Confirm)
            return ApiSupport.Problem("CONFIRMATION_REQUIRED", 400, "Cần xác nhận rõ ràng trước khi cách ly/dọn tệp dư thừa.");

        var ageHours = Math.Clamp(req.MinAgeHours ?? 24, 0, 8760);
        var cutoff = clock.GetUtcNow().AddHours(-ageHours);
        var candidates = await FindUnreferencedFilesAsync(db, audioStorage, coverStorage, cutoff, ct);

        var movedCount = 0;
        long movedBytes = 0;
        foreach (var c in candidates)
        {
            var moved = c.Category == "AUDIO"
                ? audioStorage.QuarantineFile(c.UserId, c.FileName)
                : coverStorage.QuarantineFile(c.UserId, c.FileName);
            if (moved)
            {
                movedCount++;
                movedBytes += c.SizeBytes;
            }
        }

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "STORAGE_ORPHAN_QUARANTINE", "STORAGE", "SERVER_FILES", "SUCCESS",
            req.Reason?.Trim() ?? $"Cách ly {movedCount} tệp dư thừa không còn tham chiếu DB.",
            new { minAgeHours = ageHours, movedCount, movedBytes }, ct);

        return Results.Ok(new
        {
            quarantinedCount = movedCount,
            quarantinedBytes = movedBytes,
            note = "Các tệp dư thừa đã được chuyển vào thư mục cách ly (_quarantine) để an toàn khôi phục nếu cần."
        });
    }

    private static async Task<List<CleanupCandidate>> FindUnreferencedFilesAsync(
        TMusicDb db,
        IAudioStorage audioStorage,
        ICoverImageStorage coverStorage,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        var referencedAudio = (await db.UserLibrarySongs.AsNoTracking()
            .Where(s => s.AudioFileName != null)
            .Select(s => new { s.UserId, FileName = s.AudioFileName! })
            .ToListAsync(ct))
            .Select(x => $"{x.UserId:N}/{x.FileName}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencedCovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var albumCovers = await db.Albums.AsNoTracking()
            .Where(a => a.CoverImageUrl != null && a.CoverImageUrl.StartsWith("/api/v1/covers/"))
            .Select(a => new { a.OwnerId, Url = a.CoverImageUrl! })
            .ToListAsync(ct);
        foreach (var c in albumCovers)
        {
            if (CoverImageStorage.ExtractOwnedFileName(c.OwnerId, c.Url) is { } fn)
                referencedCovers.Add($"{c.OwnerId:N}/{fn}");
        }

        var list = new List<CleanupCandidate>();
        foreach (var f in audioStorage.ListAllFiles())
        {
            if (f.LastModifiedUtc <= cutoff && !referencedAudio.Contains($"{f.UserId:N}/{f.FileName}"))
                list.Add(new CleanupCandidate("AUDIO", f.UserId, f.FileName, f.SizeBytes, f.LastModifiedUtc));
        }
        foreach (var f in coverStorage.ListAllFiles())
        {
            if (f.LastModifiedUtc <= cutoff && !referencedCovers.Contains($"{f.UserId:N}/{f.FileName}"))
                list.Add(new CleanupCandidate("COVER", f.UserId, f.FileName, f.SizeBytes, f.LastModifiedUtc));
        }
        return list;
    }

    private static long MeasureDirectoryBytes(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { }
            }
        }
        catch (IOException) { }
        return total;
    }

    private static long MeasureMatchingTempBytes()
    {
        long total = 0;
        try
        {
            var tempRoot = Path.GetTempPath();
            foreach (var dir in Directory.EnumerateDirectories(tempRoot, "tmusic-audio-*"))
                total += MeasureDirectoryBytes(dir);
        }
        catch (IOException) { }
        return total;
    }

    private sealed record CleanupCandidate(string Category, Guid UserId, string FileName, long SizeBytes, DateTimeOffset LastModifiedUtc);
    public sealed record CleanupExecuteRequest(bool Confirm, int? MinAgeHours = 24, string? Reason = null);
}
