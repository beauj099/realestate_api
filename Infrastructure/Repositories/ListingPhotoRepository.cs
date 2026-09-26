using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingPhotoRepository
{
    private const string Columns = "Id, ListingId, Url, IsPrimary, SortOrder, CreatedAt";

    private readonly DbConnectionFactory _connectionFactory;

    public ListingPhotoRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ListingPhoto>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            // The primary photo first, so older data (primary set without reordering) still
            // shows its main photo first.
            $"SELECT {Columns} FROM ListingPhoto WHERE ListingId = @ListingId ORDER BY IsPrimary DESC, SortOrder ASC, Id ASC",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingPhoto>(command);
    }

    public async Task<ListingPhoto?> GetByIdAsync(int photoId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingPhoto WHERE Id = @Id",
            new { Id = photoId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingPhoto>(command);
    }

    public async Task<int> CountByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT COUNT(*) FROM ListingPhoto WHERE ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<int>(command);
    }

    public async Task<ListingPhoto> CreateAsync(int listingId, string url, bool isPrimary, int sortOrder, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"INSERT INTO ListingPhoto (ListingId, Url, IsPrimary, SortOrder, CreatedAt) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "VALUES (@ListingId, @Url, @IsPrimary, @SortOrder, GETUTCDATE())",
            new { ListingId = listingId, Url = url, IsPrimary = isPrimary, SortOrder = sortOrder },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<ListingPhoto>(command);
    }

    /// <summary>
    /// Clears the old primary and sets the new one in a single transaction,
    /// as required by the filtered unique index UX_ListingPhoto_Primary.
    /// </summary>
    public async Task SetPrimaryAsync(int listingId, int photoId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ListingPhoto SET IsPrimary = 0 WHERE ListingId = @ListingId AND IsPrimary = 1",
            new { ListingId = listingId }, transaction: transaction, cancellationToken: cancellationToken));

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ListingPhoto SET IsPrimary = 1 WHERE Id = @Id AND ListingId = @ListingId",
            new { Id = photoId, ListingId = listingId }, transaction: transaction, cancellationToken: cancellationToken));

        if (affected == 0)
            throw new KeyNotFoundException($"Photo {photoId} not found under listing {listingId}");

        transaction.Commit();
    }

    /// <summary>
    /// Applies an agent-chosen order: SortOrder follows <paramref name="photoIds"/> and the
    /// first becomes the primary photo. The list must name every photo of the listing
    /// exactly once; returns false (nothing changed) otherwise.
    /// </summary>
    public async Task<bool> ReorderAsync(int listingId, IReadOnlyList<int> photoIds, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var existing = (await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT Id FROM ListingPhoto WITH (UPDLOCK) WHERE ListingId = @ListingId",
            new { ListingId = listingId }, transaction: transaction, cancellationToken: cancellationToken))).ToHashSet();
        if (photoIds.Count != existing.Count || photoIds.Distinct().Count() != photoIds.Count || !photoIds.All(existing.Contains))
        {
            transaction.Rollback();
            return false;
        }

        for (var i = 0; i < photoIds.Count; i++)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE ListingPhoto SET SortOrder = @SortOrder, IsPrimary = @IsPrimary WHERE Id = @Id AND ListingId = @ListingId",
                new { Id = photoIds[i], ListingId = listingId, SortOrder = i, IsPrimary = i == 0 },
                transaction: transaction, cancellationToken: cancellationToken));
        }

        transaction.Commit();
        return true;
    }

    public async Task DeleteAsync(int photoId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM ListingPhoto WHERE Id = @Id", new { Id = photoId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
