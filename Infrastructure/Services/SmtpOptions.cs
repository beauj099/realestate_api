namespace RealEstateApi.Infrastructure.Services;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>SMTP server. When empty, <see cref="LoggingEmailSender"/> is used instead.</summary>
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;

    /// <summary>Leave empty for an unauthenticated relay.</summary>
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = "RealWorth";
}
