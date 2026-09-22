namespace RealEstateApi.Infrastructure.Services;

/// <summary>
/// Development fallback used when Smtp:Host is not configured: nothing is sent,
/// the full message (including any reset code) is written to the API console.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        _logger.LogWarning(
            "SMTP is not configured (Smtp:Host); email NOT sent. To: {To} | Subject: {Subject} | Body: {Body}",
            to, subject, body);
        return Task.CompletedTask;
    }
}
