namespace TMusic.Application.Identity;

public enum AccountCodePurpose
{
    ResetPassword,
    AdminInvitation
}

public interface IAccountMailSender
{
    bool IsConfigured { get; }
    Task SendCodeAsync(string address, AccountCodePurpose purpose, string code, CancellationToken cancellationToken);
}
