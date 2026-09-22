namespace RealEstateApi.Infrastructure.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
