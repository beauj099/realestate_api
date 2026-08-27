using System.Data;
using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ListingRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Listing?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt FROM Listings WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }

    public async Task<IEnumerable<Listing>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt FROM Listings " +
            "WHERE (@Status IS NULL OR Status = @Status) AND (@DateFrom IS NULL OR CreatedAt >= @DateFrom) AND (@DateTo IS NULL OR CreatedAt <= @DateTo) " +
            "ORDER BY CreatedAt DESC",
            new { Status = status, DateFrom = dateFrom, DateTo = dateTo },
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<Listing>(command);
    }

    public async Task<Listing> CreateAsync(int? propertyTypeId, string? p24Ref, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        // Resolve only if a value was supplied; null means user hasn't chosen yet.
        int? resolvedPropertyTypeId = null;
        if (propertyTypeId.HasValue)
        {
            resolvedPropertyTypeId = await ResolvePropertyTypeIdAsync(connection, transaction, propertyTypeId.Value, cancellationToken);
        }

        var lockCommand = new CommandDefinition(
            "EXEC sp_getapplock @Resource = 'listing-reference-generator', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;",
            transaction: transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(lockCommand);

        var yearCommand = new CommandDefinition(
            "SELECT YEAR(GETDATE())",
            transaction: transaction,
            cancellationToken: cancellationToken);
        var year = await connection.ExecuteScalarAsync<int>(yearCommand);

        var yearPattern = $"LST-{year}-%";
        var maxCommand = new CommandDefinition(
            "SELECT MAX(CAST(SUBSTRING(ReferenceNumber, 10, 5) AS INT)) " +
            "FROM Listings WHERE ReferenceNumber LIKE @Pattern",
            new { Pattern = yearPattern },
            transaction: transaction,
            cancellationToken: cancellationToken);
        var nextNum = (await connection.ExecuteScalarAsync<int?>(maxCommand)) ?? 0;
        var referenceNumber = $"LST-{year}-{nextNum + 1:00000}";

        var insertCommand = new CommandDefinition(
            "INSERT INTO Listings (ReferenceNumber, P24Ref, PropertyTypeId, Status, CreatedAt, UpdatedAt) " +
            "OUTPUT INSERTED.Id, INSERTED.ReferenceNumber, INSERTED.P24Ref, INSERTED.PropertyTypeId, INSERTED.ListingValuationId, INSERTED.ListDate, INSERTED.Status, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "VALUES (@ReferenceNumber, @P24Ref, @PropertyTypeId, @Status, GETUTCDATE(), GETUTCDATE())",
            new { ReferenceNumber = referenceNumber, P24Ref = p24Ref, PropertyTypeId = resolvedPropertyTypeId, Status = ListingStatus.Incomplete },
            transaction: transaction,
            cancellationToken: cancellationToken);
        var listing = await connection.QueryFirstOrDefaultAsync<Listing>(insertCommand);

        transaction.Commit();
        return listing!;
    }

    private static async Task<int> ResolvePropertyTypeIdAsync(IDbConnection connection, IDbTransaction transaction, int requestedId, CancellationToken cancellationToken)
    {
        var existsCommand = new CommandDefinition(
            "SELECT 1 FROM PropertyType WHERE Id = @Id AND IsActive = 1",
            new { Id = requestedId },
            transaction: transaction,
            cancellationToken: cancellationToken);
        var exists = await connection.ExecuteScalarAsync<int?>(existsCommand);
        if (exists != null) return requestedId;

        var fallbackCommand = new CommandDefinition(
            "SELECT TOP 1 Id FROM PropertyType WHERE IsActive = 1 ORDER BY SortOrder ASC, Id ASC",
            transaction: transaction,
            cancellationToken: cancellationToken);
        var fallback = await connection.ExecuteScalarAsync<int?>(fallbackCommand);
        if (fallback != null) return fallback.Value;

        throw new KeyNotFoundException(
            $"Invalid PropertyTypeId {requestedId} and no active PropertyTypes exist. Seed the PropertyType table.");
    }

    public async Task<Listing?> UpdateAsync(int id, string? status, string? p24Ref, int? propertyTypeId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE Listings SET Status = COALESCE(@Status, Status), P24Ref = COALESCE(@P24Ref, P24Ref), PropertyTypeId = COALESCE(@PropertyTypeId, PropertyTypeId), UpdatedAt = GETUTCDATE() " +
            "OUTPUT INSERTED.Id, INSERTED.ReferenceNumber, INSERTED.P24Ref, INSERTED.PropertyTypeId, INSERTED.ListingValuationId, INSERTED.ListDate, INSERTED.Status, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "WHERE Id = @Id",
            new { Id = id, Status = status, P24Ref = p24Ref, PropertyTypeId = propertyTypeId },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var listingCommand = new CommandDefinition(
            "SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt FROM Listings WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        var listing = await connection.QueryFirstOrDefaultAsync<Listing>(listingCommand);
        if (listing is null) return;

        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingOutdoorFeature WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Contact WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingParking WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM PropertyRunningCosts WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoomFeature WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id)", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoomCustomFeature WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id)", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Condition WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id)", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoom WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingAddress WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingBuildingInfo WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        if (listing.ListingValuationId is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM ListingValuation WHERE Id = @Id", new { Id = listing.ListingValuationId }, transaction: transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Listings WHERE Id = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
    }

    public async Task<Listing?> SubmitAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE Listings SET Status = @Status, ListDate = GETUTCDATE(), UpdatedAt = GETUTCDATE() " +
            "OUTPUT INSERTED.Id, INSERTED.ReferenceNumber, INSERTED.P24Ref, INSERTED.PropertyTypeId, INSERTED.ListingValuationId, INSERTED.ListDate, INSERTED.Status, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "WHERE Id = @Id AND Status = @CurrentStatus",
            new { Id = id, Status = ListingStatus.Submitted, CurrentStatus = ListingStatus.Incomplete },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }
}
