namespace TMusic.Application.Identity;

public enum AccountCodePurpose
{
    ResetPassword
}

public interface IAccountMailSender
{
    bool IsConfigured { get; }
    Task SendCodeAsync(string address, AccountCodePurpose purpose, string code, CancellationToken cancellationToken);
}
