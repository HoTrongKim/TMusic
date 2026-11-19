using Microsoft.EntityFrameworkCore;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Admin;

public sealed record SettingDefinition(
    string Key,
    string DefaultValue,
    string ValueType,
    string Category,
    string Description,
    int? MinValue = null,
    int? MaxValue = null,
    bool RequiresConfirmation = false);

public sealed class AdminSettingsService(TMusicDb db, IConfiguration config, TimeProvider clock)
{
    private static readonly string[] ForbiddenKeyFragments =
    [
        "secret", "password", "signing", "jwt", "smtp", "connection", "token", "apikey", "privatekey", "cookie"
    ];

    public IReadOnlyList<SettingDefinition> Definitions =>
    [
        new("downloads.max_concurrency", "2", "int", "downloads",
            "Số tác vụ trích xuất MP3 xử lý đồng thời tối đa trên backend.", 1, 16, false),
        new("downloads.max_file_size_mb", "100", "int", "downloads",
            "Giới hạn dung lượng tệp MP3 tối đa cho mỗi lượt tải (MB).", 5, 200, false),
        new("downloads.max_retries", "3", "int", "downloads",
            "Số lần thử lại tối đa đối với các lỗi tạm thời khi tải MP3.", 0, 10, false),
        new("cache.metadata_ttl_hours", "24", "int", "cache",
            "Thời gian lưu bộ nhớ đệm metadata bài hát (giờ).", 1, 168, false),
        new("rate_limit.auth_per_minute",
            config.GetValue("RateLimits:AuthPermitLimit", 30).ToString(), "int", "security",
            "Số yêu cầu xác thực tối đa mỗi phút theo IP.", 5, 300, true),
        new("rate_limit.provider_per_minute",
            config.GetValue("RateLimits:ProviderPermitLimit", 30).ToString(), "int", "security",
            "Số yêu cầu phân giải nguồn/tải nhạc tối đa mỗi phút theo người dùng.", 5, 300, false),
        new("features.enable_audio_downloads",
            config.GetValue("Features:EnableAudioDownloads", true).ToString().ToLowerInvariant(), "bool", "features",
            "Bật hoặc tạm dừng tiếp nhận tác vụ tải âm thanh MP3.", null, null, true),
        new("features.enable_tiktok_import",
            config.GetValue("Features:EnableTikTokVideoImport", true).ToString().ToLowerInvariant(), "bool", "features",
            "Cho phép nhập và trích xuất âm thanh từ liên kết TikTok.", null, null, false),
        new("features.enable_soundcloud_import",
            config.GetValue("Features:EnableSoundCloudImport", true).ToString().ToLowerInvariant(), "bool", "features",
            "Cho phép nhập và trích xuất âm thanh từ liên kết SoundCloud.", null, null, false),
        new("maintenance.enabled", "false", "bool", "operations",
            "Bật chế độ bảo trì hiển thị thông báo tạm dừng dịch vụ cho ứng dụng.", null, null, true),
        new("maintenance.message", "", "string", "operations",
            "Thông điệp bảo trì hiển thị khi chế độ bảo trì được bật (tối đa 300 ký tự).", null, 300, false)
    ];

    public static bool IsForbiddenSecretKey(string key)
    {
        var lower = key.Trim().ToLowerInvariant();
        return ForbiddenKeyFragments.Any(lower.Contains);
    }

    public SettingDefinition? FindDefinition(string key) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct)
    {
        var stored = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, ct);
        var now = clock.GetUtcNow();
        var list = new List<SystemSetting>(Definitions.Count);
        foreach (var def in Definitions)
        {
            if (stored.TryGetValue(def.Key, out var row))
            {
                list.Add(row);
            }
            else
            {
                list.Add(new SystemSetting
                {
                    Key = def.Key,
                    Value = def.DefaultValue,
                    DefaultValue = def.DefaultValue,
                    ValueType = def.ValueType,
                    Category = def.Category,
                    Description = def.Description,
                    MinValue = def.MinValue,
                    MaxValue = def.MaxValue,
                    RequiresConfirmation = def.RequiresConfirmation,
                    UpdatedAt = now
                });
            }
        }
        return list;
    }

    public async Task<(SystemSetting? Setting, string? ErrorCode, string? ErrorDetail)> UpdateAsync(
        string key,
        string? rawValue,
        bool confirmed,
        Guid actorId,
        CancellationToken ct)
    {
        if (IsForbiddenSecretKey(key))
            return (null, "SECRET_KEY_FORBIDDEN", "Không được phép đọc hoặc chỉnh sửa bí mật hệ thống qua Admin API.");

        var def = FindDefinition(key);
        if (def is null)
            return (null, "SETTING_NOT_FOUND", "Khóa cấu hình không nằm trong danh sách cấu hình runtime hợp lệ.");

        if (def.RequiresConfirmation && !confirmed)
            return (null, "CONFIRMATION_REQUIRED", "Thay đổi cấu hình này có ảnh hưởng lớn và yêu cầu xác nhận.");

        var normalized = NormalizeAndValidate(def, rawValue, out var errorDetail);
        if (normalized is null)
            return (null, "INVALID_SETTING_VALUE", errorDetail);

        var now = clock.GetUtcNow();
        var row = await db.SystemSettings.SingleOrDefaultAsync(x => x.Key == def.Key, ct);
        if (row is null)
        {
            row = new SystemSetting
            {
                Key = def.Key,
                Value = normalized,
                DefaultValue = def.DefaultValue,
                ValueType = def.ValueType,
                Category = def.Category,
                Description = def.Description,
                MinValue = def.MinValue,
                MaxValue = def.MaxValue,
                RequiresConfirmation = def.RequiresConfirmation,
                UpdatedAt = now,
                UpdatedByUserId = actorId
            };
            db.SystemSettings.Add(row);
        }
        else
        {
            row.Value = normalized;
            row.DefaultValue = def.DefaultValue;
            row.Description = def.Description;
            row.MinValue = def.MinValue;
            row.MaxValue = def.MaxValue;
            row.RequiresConfirmation = def.RequiresConfirmation;
            row.UpdatedAt = now;
            row.UpdatedByUserId = actorId;
        }

        await db.SaveChangesAsync(ct);
        return (row, null, null);
    }

    public async Task<(SystemSetting? Setting, string? ErrorCode)> ResetToDefaultAsync(
        string key,
        Guid actorId,
        CancellationToken ct)
    {
        if (IsForbiddenSecretKey(key))
            return (null, "SECRET_KEY_FORBIDDEN");
        var def = FindDefinition(key);
        if (def is null)
            return (null, "SETTING_NOT_FOUND");

        var (setting, code, _) = await UpdateAsync(key, def.DefaultValue, confirmed: true, actorId, ct);
        return (setting, code);
    }

    private static string? NormalizeAndValidate(SettingDefinition def, string? rawValue, out string? error)
    {
        error = null;
        if (rawValue is null)
        {
            error = "Giá trị cấu hình không được để trống.";
            return null;
        }

        var trimmed = rawValue.Trim();
        switch (def.ValueType)
        {
            case "int":
                if (!int.TryParse(trimmed, out var intVal))
                {
                    error = "Giá trị phải là số nguyên hợp lệ.";
                    return null;
                }
                if ((def.MinValue is not null && intVal < def.MinValue) ||
                    (def.MaxValue is not null && intVal > def.MaxValue))
                {
                    error = $"Giá trị phải nằm trong khoảng {def.MinValue} đến {def.MaxValue}.";
                    return null;
                }
                return intVal.ToString();

            case "bool":
                if (!bool.TryParse(trimmed, out var boolVal))
                {
                    error = "Giá trị phải là true hoặc false.";
                    return null;
                }
                return boolVal ? "true" : "false";

            case "string":
                var maxLen = def.MaxValue ?? 300;
                if (trimmed.Length > maxLen)
                {
                    error = $"Độ dài không vượt quá {maxLen} ký tự.";
                    return null;
                }
                return trimmed;

            default:
                error = "Kiểu cấu hình không được hỗ trợ.";
                return null;
        }
    }
}
