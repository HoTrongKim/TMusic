using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace TMusic.Api.Common;

internal static class ApiSupport
{
    public static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
    public static IResult Problem(string code, int status = 400, string? detail = null) =>
        Results.Problem(statusCode: status, title: code, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    public static bool IsUnique(DbUpdateException error) => error.InnerException is SqlException { Number: 2601 or 2627 };
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool PageValid(int? page, int? pageSize, string? q = null) =>
        page is null or >= 1 and <= 10000 && pageSize is null or >= 1 and <= 100 && q?.Length is null or <= 200;
    public static IResult? Match(HttpContext context, byte[] version)
    {
        var header = context.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(header)) return Problem("VERSION_REQUIRED", 428, "Hãy tải lại dữ liệu trước khi thay đổi.");
        return header == $"\"{Convert.ToBase64String(version)}\"" ? null :
            Problem("CONCURRENT_MODIFICATION", 409, "Dữ liệu đã thay đổi trên thiết bị khác. Hãy tải lại.");
    }
}

