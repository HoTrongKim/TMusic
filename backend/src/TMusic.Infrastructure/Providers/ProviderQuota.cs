using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Infrastructure.Providers;

public sealed class ProviderQuota(TMusicDb db, IConfiguration configuration, TimeProvider clock)
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public async Task ReserveAsync(CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Pacific).DateTime);
        var budget = Math.Clamp(configuration.GetValue("YouTube:DailyQuotaBudget", 8000), 1, 1_000_000);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @used int;
            SELECT @used = [Used] FROM [ProviderQuotaWindows] WITH (UPDLOCK, HOLDLOCK) WHERE [Day] = @day;
            IF @used IS NULL BEGIN
                INSERT INTO [ProviderQuotaWindows] ([Day], [Used]) VALUES (@day, 1);
                SELECT 1;
            END
            ELSE IF @used < @budget BEGIN
                UPDATE [ProviderQuotaWindows] SET [Used] = [Used] + 1 WHERE [Day] = @day;
                SELECT 1;
            END
            ELSE SELECT 0;
            """;
        var dayParameter = command.CreateParameter(); dayParameter.ParameterName = "@day";
        dayParameter.DbType = DbType.Date; dayParameter.Value = day.ToDateTime(TimeOnly.MinValue);
        var budgetParameter = command.CreateParameter(); budgetParameter.ParameterName = "@budget"; budgetParameter.Value = budget;
        command.Parameters.Add(dayParameter); command.Parameters.Add(budgetParameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        await transaction.CommitAsync(ct);
        if (result == 0) throw new ProviderException("YOUTUBE_QUOTA_EXCEEDED", true);
    }
}
