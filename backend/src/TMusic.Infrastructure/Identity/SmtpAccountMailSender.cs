using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Identity;
namespace TMusic.Infrastructure.Identity;

public sealed class SmtpAccountMailSender(IConfiguration config) : IAccountMailSender
{
    private string? Setting(string name, string alias) => config[$"Mail:{name}"] ?? config[alias];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Setting("Host", "SMTP_HOST")) &&
        MailAddress.TryCreate(Setting("From", "SMTP_FROM_EMAIL"), out _);

    public async Task SendCodeAsync(string address, AccountCodePurpose purpose, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("SMTP is not configured.");
        using var message = new MailMessage(Setting("From", "SMTP_FROM_EMAIL")!, address)
        {
            Subject = purpose == AccountCodePurpose.AdminInvitation
                ? "Lời mời quản trị TMusic Admin"
                : "Đặt lại mật khẩu TMusic",
            Body = purpose == AccountCodePurpose.AdminInvitation
                ? $"Bạn nhận được lời mời tham gia TMusic Admin. Mã lời mời dùng một lần (hết hạn sau 24 giờ):\n\n{code}\n\nHãy mở trang TMusic Admin để xác nhận tài khoản và thiết lập MFA. Không chia sẻ mã này cho người khác."
                : $"Sao chép toàn bộ mã khôi phục mật khẩu sau và dán vào TMusic. Mã hết hạn sau 1 giờ.\n\n{code}\n\nNếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(Setting("Host", "SMTP_HOST"), int.TryParse(Setting("Port", "SMTP_PORT"), out var port) ? port : 587)
        {
            EnableSsl = !string.Equals(Setting("UseTls", "SMTP_USE_TLS"), "false", StringComparison.OrdinalIgnoreCase),
            Timeout = 15000
        };
        if (Setting("User", "SMTP_USERNAME") is { Length: > 0 } user)
            client.Credentials = new NetworkCredential(user, Setting("Password", "SMTP_PASSWORD"));
        await client.SendMailAsync(message, cancellationToken);
    }
}
