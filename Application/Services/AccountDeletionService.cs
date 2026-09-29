using Dapper;
using Microsoft.Extensions.Options;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

/// <summary>
/// Deleting an account, with a grace period. "Delete account" disables it at once (no sign-in, no
/// tokens) and records when; everything is kept, and signing in again within
/// <see cref="GraceDays"/> days restores it (<see cref="UserRepository.GetPendingDeletionAsync"/>).
/// After that, <see cref="PurgeDueAsync"/> (daily) deletes what is the agent's: name, email, phone,
/// registration numbers and password are replaced, and the profile with its images is deleted.
/// The user row stays, anonymised and disabled, because the agent's listings (the agency's records)
/// and logged sales point at it; they stay too. Accounts switched off for any other reason have no
/// deletion date and are never touched.
/// </summary>
public class AccountDeletionService(
    UserRepository users,
    AgentProfileRepository profiles,
    IImageStorage images,
    IOptions<R2Options> r2,
    DbConnectionFactory connections,
    ILogger<AccountDeletionService> log)
{
    /// <summary>How long a deleted account can still be restored by signing in.</summary>
    public const int GraceDays = 90;

    public enum Result { Deleted, WrongPassword, NotFound }

    /// <summary>Disables the account and starts the grace period. The password is asked again.</summary>
    public async Task<Result> DeleteAsync(int userId, string password, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive) return Result.NotFound;
        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Result.WrongPassword;

        using var db = connections.CreateConnection();
        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM dbo.RefreshTokens WHERE UserId = @id;
            DELETE FROM dbo.PasswordResetCodes WHERE UserId = @id;
            UPDATE dbo.Users SET IsActive = 0, DeletionRequestedAt = SYSUTCDATETIME() WHERE Id = @id;
            """, new { id = userId }, cancellationToken: ct));
        log.LogInformation("Account {UserId} deleted; restorable for {Days} days", userId, GraceDays);
        return Result.Deleted;
    }

    /// <summary>Anonymises every account whose grace period is over. Returns how many.</summary>
    public async Task<int> PurgeDueAsync(CancellationToken ct)
    {
        List<int> due;
        using (var db = connections.CreateConnection())
        {
            due = (await db.QueryAsync<int>(new CommandDefinition("""
                SELECT Id FROM dbo.Users
                WHERE IsActive = 0 AND DeletionRequestedAt IS NOT NULL
                  AND DeletionRequestedAt < DATEADD(day, -@days, SYSUTCDATETIME())
                  AND Username NOT LIKE 'deleted-%'
                """, new { days = GraceDays }, cancellationToken: ct))).ToList();
        }
        foreach (var id in due) await AnonymiseAsync(id, ct);
        if (due.Count > 0) log.LogInformation("Anonymised {Count} account(s) after the grace period", due.Count);
        return due.Count;
    }

    private async Task AnonymiseAsync(int userId, CancellationToken ct)
    {
        // The profile's images, before the row that points at them goes.
        var profile = await profiles.GetAsync(userId, ct);
        var imageUrls = new[] { profile?.PhotoUrl, profile?.SignatureUrl }
            .Concat(ReadLogoUrls(profile?.OfficeLogosJson));

        using (var db = connections.CreateConnection())
        {
            db.Open();
            using var tx = db.BeginTransaction();
            await db.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.PasswordResetCodes WHERE UserId = @id;
                DELETE FROM dbo.RefreshTokens WHERE UserId = @id;
                DELETE FROM dbo.AgentProfiles WHERE UserId = @id;
                -- Kept for the listings and logged sales that point at it; nothing of the agent
                -- left. The email stays unique (it has a unique index) and is not a real address.
                UPDATE dbo.Users SET
                    Username = CONCAT('deleted-', Id),
                    Email = CONCAT('deleted-', Id, '@deleted.invalid'),
                    DisplayName = 'Former agent',
                    FullName = NULL, Mobile = NULL,
                    AgencyRegistrationNumber = NULL, LicenceNumber = NULL,
                    PasswordHash = @unusable,
                    IsActive = 0
                WHERE Id = @id;
                """,
                // A hash of a random secret nobody knows: the old password stops working too.
                new { id = userId, unusable = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")) },
                tx, cancellationToken: ct));
            tx.Commit();
        }

        // Best effort: an orphaned image is harmless, a failed purge is not.
        foreach (var key in imageUrls.Select(u => KeyOf(u, userId)).OfType<string>())
        {
            try { await images.DeleteAsync(key, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not delete {Key} of a deleted account", key); }
        }
    }

    private static IEnumerable<string?> ReadLogoUrls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject().Select(p => p.Value.GetString()).ToList();
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    /// <summary>The storage key of one of this agent's own images, or null (never anyone else's).</summary>
    private string? KeyOf(string? url, int userId)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var r2Prefix = (r2.Value.PublicUrl ?? "").TrimEnd('/') + "/";
        var key = url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ? url["/uploads/".Length..]
            : r2Prefix.Length > 1 && url.StartsWith(r2Prefix, StringComparison.Ordinal) ? url[r2Prefix.Length..]
            : null;
        key = key?.Split('?')[0];
        return key is not null && key.StartsWith($"agents/{userId}/", StringComparison.Ordinal) ? key : null;
    }
}

/// <summary>Once a day (03:20 SAST), anonymises the accounts whose grace period has ended.</summary>
public class DailyAccountPurge(IServiceScopeFactory scopes, ILogger<DailyAccountPurge> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sast = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, sast);
            var next = new DateTimeOffset(now.Year, now.Month, now.Day, 3, 20, 0, now.Offset);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AccountDeletionService>().PurgeDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "The daily account purge failed; it runs again tomorrow");
            }
        }
    }
}
