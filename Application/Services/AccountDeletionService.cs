using Dapper;
using Microsoft.Extensions.Options;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

/// <summary>
/// Deletes an agent's account, as app stores require, while their listings stay: the listings,
/// their photos and details belong to the agency's records, and the sales the agent logged stay in
/// the shared market data. So the user row is kept (listings and logged sales point at it) but
/// emptied of everything about the agent and disabled for good: name, email, phone, registration
/// numbers and password are replaced, and sign-in and token refresh already refuse an inactive
/// user. The agent's own profile (photo, signature, bio, office details) and sign-in tokens are
/// deleted. The password is asked again, so a phone left signed in cannot delete an account.
/// </summary>
public class AccountDeletionService(
    UserRepository users,
    AgentProfileRepository profiles,
    IImageStorage images,
    IOptions<R2Options> r2,
    DbConnectionFactory connections,
    ILogger<AccountDeletionService> log)
{
    public enum Result { Deleted, WrongPassword, NotFound }

    public async Task<Result> DeleteAsync(int userId, string password, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive) return Result.NotFound;
        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Result.WrongPassword;

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

        // Best effort: an orphaned image is harmless, a failed delete of the account is not.
        foreach (var key in imageUrls.Select(u => KeyOf(u, userId)).OfType<string>())
        {
            try { await images.DeleteAsync(key, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not delete {Key} of a deleted account", key); }
        }
        log.LogInformation("Account {UserId} deleted (anonymised; listings kept)", userId);
        return Result.Deleted;
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
