using Dapper;
using Microsoft.Data.SqlClient;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

/// <summary>
/// Photos of a single room (ListingRoomPhotos). ListingRoom.PhotoUrl is kept as the
/// room's cover for older app builds: every write here re-points it at the
/// lowest-SortOrder photo (or NULL) inside the same transaction.
/// </summary>
public class ListingRoomPhotoRepository
{
    private const string Columns = "Id, ListingRoomId, Url, StorageKey, SortOrder, CreatedAt";

    /// <summary>SQL error for "invalid object name": the room photos patch has not been applied yet.</summary>
    private const int TableMissing = 208;

    private const string RefreshCoverSql =
        "UPDATE ListingRoom SET PhotoUrl = (SELECT TOP 1 p.Url FROM ListingRoomPhotos p WHERE p.ListingRoomId = @ListingRoomId ORDER BY p.SortOrder ASC, p.Id ASC), " +
        "UpdatedAt = GETUTCDATE() WHERE Id = @ListingRoomId;";

    private readonly DbConnectionFactory _connectionFactory;

    public ListingRoomPhotoRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>Read-only; returns nothing until the ListingRoomPhotos patch is applied so room reads keep working.</summary>
    public async Task<IEnumerable<ListingRoomPhoto>> GetByRoomIdAsync(int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingRoomPhotos WHERE ListingRoomId = @ListingRoomId ORDER BY SortOrder ASC, Id ASC",
            new { ListingRoomId = listingRoomId }, cancellationToken: cancellationToken);
        try
        {
            return await connection.QueryAsync<ListingRoomPhoto>(command);
        }
        catch (SqlException ex) when (ex.Number == TableMissing)
        {
            return [];
        }
    }

    /// <summary>All photos of every room of a listing in one query (for the listing/room DTOs and deletes).</summary>
    public async Task<IEnumerable<ListingRoomPhoto>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT p.Id, p.ListingRoomId, p.Url, p.StorageKey, p.SortOrder, p.CreatedAt " +
            "FROM ListingRoomPhotos p INNER JOIN ListingRoom r ON r.Id = p.ListingRoomId " +
            "WHERE r.ListingId = @ListingId ORDER BY p.ListingRoomId ASC, p.SortOrder ASC, p.Id ASC",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        try
        {
            return await connection.QueryAsync<ListingRoomPhoto>(command);
        }
        catch (SqlException ex) when (ex.Number == TableMissing)
        {
            return [];
        }
    }

    public async Task<ListingRoomPhoto?> GetByIdAsync(int photoId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingRoomPhotos WHERE Id = @Id",
            new { Id = photoId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingRoomPhoto>(command);
    }

    public async Task<int> CountByRoomIdAsync(int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT COUNT(*) FROM ListingRoomPhotos WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId }, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<int>(command);
    }

    /// <summary>
    /// Appends a photo (SortOrder = current max + 1) unless the room already holds
    /// <paramref name="maxPhotos"/>, then refreshes the cover. The count is taken under
    /// UPDLOCK/HOLDLOCK so concurrent uploads cannot overshoot the cap.
    /// Returns null when the cap was reached and nothing was inserted.
    /// </summary>
    public async Task<ListingRoomPhoto?> TryCreateAsync(int listingRoomId, string url, string? storageKey, int maxPhotos, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var created = await connection.QueryFirstOrDefaultAsync<ListingRoomPhoto>(new CommandDefinition(
            "DECLARE @Count INT, @NextSort INT; " +
            "SELECT @Count = COUNT(*), @NextSort = ISNULL(MAX(SortOrder), -1) + 1 " +
            "FROM ListingRoomPhotos WITH (UPDLOCK, HOLDLOCK) WHERE ListingRoomId = @ListingRoomId; " +
            "INSERT INTO ListingRoomPhotos (ListingRoomId, Url, StorageKey, SortOrder, CreatedAt) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "SELECT @ListingRoomId, @Url, @StorageKey, @NextSort, GETUTCDATE() WHERE @Count < @MaxPhotos;",
            new { ListingRoomId = listingRoomId, Url = url, StorageKey = storageKey, MaxPhotos = maxPhotos },
            transaction: transaction, cancellationToken: cancellationToken));

        if (created is null)
        {
            transaction.Rollback();
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            RefreshCoverSql, new { ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return created;
    }

    /// <summary>
    /// Applies an agent-chosen order: SortOrder follows <paramref name="photoIds"/>, so the
    /// first is the cover, and the cover is refreshed. The list must name every photo of
    /// the room exactly once; returns false (nothing changed) otherwise.
    /// </summary>
    public async Task<bool> ReorderAsync(int listingRoomId, IReadOnlyList<int> photoIds, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var existing = (await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT Id FROM ListingRoomPhotos WITH (UPDLOCK) WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken))).ToHashSet();
        if (photoIds.Count != existing.Count || photoIds.Distinct().Count() != photoIds.Count || !photoIds.All(existing.Contains))
        {
            transaction.Rollback();
            return false;
        }

        for (var i = 0; i < photoIds.Count; i++)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE ListingRoomPhotos SET SortOrder = @SortOrder WHERE Id = @Id AND ListingRoomId = @ListingRoomId",
                new { Id = photoIds[i], ListingRoomId = listingRoomId, SortOrder = i },
                transaction: transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition(
            RefreshCoverSql, new { ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return true;
    }

    /// <summary>Deletes one photo of the room and refreshes the cover. False when no such photo exists under the room.</summary>
    public async Task<bool> DeleteAsync(int photoId, int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoomPhotos WHERE Id = @Id AND ListingRoomId = @ListingRoomId",
            new { Id = photoId, ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken));

        if (affected == 0)
        {
            transaction.Rollback();
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            RefreshCoverSql, new { ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return true;
    }

    /// <summary>Deletes every photo of the room and clears the cover. Returns the removed rows.</summary>
    public async Task<List<ListingRoomPhoto>> DeleteAllForRoomAsync(int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var deleted = (await connection.QueryAsync<ListingRoomPhoto>(new CommandDefinition(
            $"DELETE FROM ListingRoomPhotos OUTPUT DELETED.{Columns.Replace(", ", ", DELETED.")} WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken))).ToList();

        await connection.ExecuteAsync(new CommandDefinition(
            RefreshCoverSql, new { ListingRoomId = listingRoomId }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return deleted;
    }
}
