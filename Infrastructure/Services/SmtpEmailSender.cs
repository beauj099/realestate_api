using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace RealEstateApi.Infrastructure.Services;

/// <summary>Sends plain-text mail through the SMTP server configured under "Smtp".</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var fromAddress = string.IsNullOrWhiteSpace(_options.FromAddress) ? _options.Username : _options.FromAddress;
        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException("Smtp:FromAddress is not configured.");

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, _options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(to);

        // SmtpClient is not thread-safe, so one per message.
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(_options.Username))
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);

        await client.SendMailAsync(message, ct);
    }
}
