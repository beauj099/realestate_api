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

    private const string Columns = "Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, UserId, CreatedAt, UpdatedAt";

    public async Task<Listing?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM Listings WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }

    /// <summary>
    /// Ownership-scoped read. Admins (isAdmin) bypass the owner check by passing
    /// userId null. Returns null on owner mismatch so callers map to 404
    /// without confirming the id exists.
    /// </summary>
    public async Task<Listing?> GetOwnedByIdAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (isAdmin || userId is null)
            return await GetByIdAsync(id, cancellationToken);

        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM Listings WHERE Id = @Id AND UserId = @UserId",
            new { Id = id, UserId = userId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }

    public async Task AssertOwnedAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await GetOwnedByIdAsync(id, userId, isAdmin, cancellationToken);
        if (listing is null)
            throw new KeyNotFoundException($"Listing {id} not found");
    }

    public async Task<IEnumerable<Listing>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(status, dateFrom, dateTo, null, true, cancellationToken);
    }

    public async Task<IEnumerable<Listing>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        // Admins pass userId null to keep the full view.
        var effectiveUserId = isAdmin ? null : userId;
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM Listings " +
            "WHERE (@Status IS NULL OR Status = @Status) AND (@DateFrom IS NULL OR CreatedAt >= @DateFrom) AND (@DateTo IS NULL OR CreatedAt <= @DateTo) " +
            "AND (@UserId IS NULL OR UserId = @UserId) " +
            "ORDER BY CreatedAt DESC",
            new { Status = status, DateFrom = dateFrom, DateTo = dateTo, UserId = effectiveUserId },
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<Listing>(command);
    }

    /// <summary>
    /// Card-ready summaries: address, first owner, primary photo and room count
    /// via LEFT JOINs so listings without them still come back.
    /// </summary>
    public async Task<IEnumerable<RealEstateApi.Application.DTOs.ListingSummaryDto>> GetSummariesAsync(string? status, DateTime? dateFrom, DateTime? dateTo, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var effectiveUserId = isAdmin ? null : userId;
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT l.Id, l.ReferenceNumber, l.P24Ref, l.PropertyTypeId, l.ListingValuationId, l.ListDate, l.Status, l.CreatedAt, l.UpdatedAt, " +
            "a.StreetNumber, a.Street, a.Suburb, a.City, " +
            "(SELECT TOP 1 FullName FROM Contact WHERE ListingId = l.Id ORDER BY FullName) AS PrimaryOwnerName, " +
            "(SELECT TOP 1 Url FROM ListingPhoto WHERE ListingId = l.Id AND IsPrimary = 1) AS PrimaryPhotoUrl, " +
            "(SELECT COUNT(*) FROM ListingRoom WHERE ListingId = l.Id) AS RoomCount " +
            "FROM Listings l LEFT JOIN ListingAddress a ON a.ListingId = l.Id " +
            "WHERE (@Status IS NULL OR l.Status = @Status) AND (@DateFrom IS NULL OR l.CreatedAt >= @DateFrom) AND (@DateTo IS NULL OR l.CreatedAt <= @DateTo) " +
            "AND (@UserId IS NULL OR l.UserId = @UserId) " +
            "ORDER BY l.CreatedAt DESC",
            new { Status = status, DateFrom = dateFrom, DateTo = dateTo, UserId = effectiveUserId },
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<RealEstateApi.Application.DTOs.ListingSummaryDto>(command);
    }

    public async Task<Listing> CreateAsync(int? propertyTypeId, string? p24Ref, CancellationToken cancellationToken = default)
    {
        return await CreateAsync(propertyTypeId, p24Ref, 0, cancellationToken);
    }

    public async Task<Listing> CreateAsync(int? propertyTypeId, string? p24Ref, int userId, CancellationToken cancellationToken = default)
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
            $"INSERT INTO Listings (ReferenceNumber, P24Ref, PropertyTypeId, Status, UserId, CreatedAt, UpdatedAt) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "VALUES (@ReferenceNumber, @P24Ref, @PropertyTypeId, @Status, @UserId, GETUTCDATE(), GETUTCDATE())",
            new { ReferenceNumber = referenceNumber, P24Ref = p24Ref, PropertyTypeId = resolvedPropertyTypeId, Status = ListingStatus.Incomplete, UserId = userId },
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
        return await UpdateAsync(id, status, p24Ref, propertyTypeId, null, true, cancellationToken);
    }

    public async Task<Listing?> UpdateAsync(int id, string? status, string? p24Ref, int? propertyTypeId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"UPDATE Listings SET Status = COALESCE(@Status, Status), P24Ref = COALESCE(@P24Ref, P24Ref), PropertyTypeId = COALESCE(@PropertyTypeId, PropertyTypeId), UpdatedAt = GETUTCDATE() " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "WHERE Id = @Id AND (@UserId IS NULL OR UserId = @UserId)",
            new { Id = id, Status = status, P24Ref = p24Ref, PropertyTypeId = propertyTypeId, UserId = isAdmin ? null : userId },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await DeleteAsync(id, null, true, cancellationToken);
    }

    public async Task DeleteAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var effectiveUserId = isAdmin ? null : userId;
        var listingCommand = new CommandDefinition(
            $"SELECT {Columns} FROM Listings WHERE Id = @Id AND (@UserId IS NULL OR UserId = @UserId)",
            new { Id = id, UserId = effectiveUserId }, cancellationToken: cancellationToken);
        var listing = await connection.QueryFirstOrDefaultAsync<Listing>(listingCommand);
        if (listing is null) return;

        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingPhoto WHERE ListingId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
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
            "DELETE FROM Listings WHERE Id = @Id AND (@UserId IS NULL OR UserId = @UserId)", new { Id = id, UserId = effectiveUserId }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
    }

    public async Task<Listing?> SubmitAsync(int id, CancellationToken cancellationToken = default)
    {
        return await SubmitAsync(id, null, true, cancellationToken);
    }

    public async Task<Listing?> SubmitAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"UPDATE Listings SET Status = @Status, ListDate = GETUTCDATE(), UpdatedAt = GETUTCDATE() " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "WHERE Id = @Id AND Status = @CurrentStatus AND (@UserId IS NULL OR UserId = @UserId)",
            new { Id = id, Status = ListingStatus.Submitted, CurrentStatus = ListingStatus.Incomplete, UserId = isAdmin ? null : userId },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Listing>(command);
    }
}
