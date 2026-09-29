using Dapper;
using Microsoft.Extensions.Options;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

/// <summary>
/// Deletes an agent's account and what is theirs, as app stores require: every listing they
/// captured (owners, rooms, photos, documents, through <see cref="ListingService.DeleteAsync(int, int?, bool, CancellationToken)"/>),
/// the sales they logged, their profile and its images, and their sign-in tokens. An agency they
/// added stays for the agents using it, no longer linked to them. The password is asked again,
/// so a phone left signed in cannot delete an account.
/// </summary>
public class AccountDeletionService(
    UserRepository users,
    ListingRepository listings,
    ListingService listingService,
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
        if (user is null) return Result.NotFound;
        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Result.WrongPassword;

        // Listings first, one by one, so each takes its photos and documents with it.
        foreach (var listing in await listings.GetAllAsync(null, null, null, userId, false, ct))
            await listingService.DeleteAsync(listing.Id, userId, false, ct);

        // The profile's images, before the row that points at them goes.
        var profile = await profiles.GetAsync(userId, ct);
        var imageUrls = new[] { profile?.PhotoUrl, profile?.SignatureUrl }
            .Concat(ReadLogoUrls(profile?.OfficeLogosJson));

        using (var db = connections.CreateConnection())
        {
            db.Open();
            using var tx = db.BeginTransaction();
            await db.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.AgentComparables WHERE CapturedByUserId = @id;
                DELETE FROM dbo.PasswordResetCodes WHERE UserId = @id;
                DELETE FROM dbo.RefreshTokens WHERE UserId = @id;
                DELETE FROM dbo.AgentProfiles WHERE UserId = @id;
                UPDATE dbo.Agencies SET CreatedByUserId = NULL WHERE CreatedByUserId = @id;
                DELETE FROM dbo.Users WHERE Id = @id;
                """, new { id = userId }, tx, cancellationToken: ct));
            tx.Commit();
        }

        // Best effort: an orphaned image is harmless, a failed delete of the account is not.
        foreach (var key in imageUrls.Select(u => KeyOf(u, userId)).OfType<string>())
        {
            try { await images.DeleteAsync(key, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not delete {Key} of a deleted account", key); }
        }
        log.LogInformation("Account {UserId} deleted", userId);
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
