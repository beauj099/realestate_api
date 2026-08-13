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

    public async Task<Listing?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Listing>(
            "SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt FROM Listings WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<IEnumerable<Listing>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Listing>(
            "SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt FROM Listings " +
            "WHERE (@Status IS NULL OR Status = @Status) AND (@DateFrom IS NULL OR CreatedAt >= @DateFrom) AND (@DateTo IS NULL OR CreatedAt <= @DateTo) " +
            "ORDER BY CreatedAt DESC",
            new { Status = status, DateFrom = dateFrom, DateTo = dateTo });
    }

    public async Task<Listing> CreateAsync(int propertyTypeId, string? p24Ref)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Listing>(
            "DECLARE @Year CHAR(4) = CAST(YEAR(GETDATE()) AS CHAR(4)); " +
            "DECLARE @NextNum INT = ISNULL((SELECT MAX(CAST(SUBSTRING(ReferenceNumber, 10, 5) AS INT)) FROM Listings WHERE ReferenceNumber LIKE 'LST-' + @Year + '-%'), 0) + 1; " +
            "DECLARE @RefNum NVARCHAR(20) = 'LST-' + @Year + '-' + RIGHT('00000' + CAST(@NextNum AS NVARCHAR(5)), 5); " +
            "INSERT INTO Listings (ReferenceNumber, P24Ref, PropertyTypeId, Status, CreatedAt, UpdatedAt) " +
            "OUTPUT INSERTED.Id, INSERTED.ReferenceNumber, INSERTED.P24Ref, INSERTED.PropertyTypeId, INSERTED.ListingValuationId, INSERTED.ListDate, INSERTED.Status, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "VALUES (@RefNum, @P24Ref, @PropertyTypeId, 'incomplete', GETUTCDATE(), GETUTCDATE())",
            new { PropertyTypeId = propertyTypeId, P24Ref = p24Ref });
    }

    public async Task<Listing?> UpdateAsync(int id, string? status, string? p24Ref, int? propertyTypeId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Listing>(
            "UPDATE Listings SET Status = COALESCE(@Status, Status), P24Ref = COALESCE(@P24Ref, P24Ref), PropertyTypeId = COALESCE(@PropertyTypeId, PropertyTypeId), UpdatedAt = GETUTCDATE() " +
            "OUTPUT INSERTED.Id, INSERTED.ReferenceNumber, INSERTED.P24Ref, INSERTED.PropertyTypeId, INSERTED.ListingValuationId, INSERTED.ListDate, INSERTED.Status, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "WHERE Id = @Id",
            new { Id = id, Status = status, P24Ref = p24Ref, PropertyTypeId = propertyTypeId });
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var listing = await connection.QueryFirstOrDefaultAsync<Listing>(
            "SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt FROM Listings WHERE Id = @Id",
            new { Id = id });
        if (listing is null) return;

        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            "DELETE FROM ListingOutdoorFeature WHERE ListingId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM Contact WHERE ListingId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingParking WHERE ListingId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM PropertyRunningCosts WHERE ListingId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoomFeature WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id)", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoomCustomFeature WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id)", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM Condition WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id)", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoom WHERE ListingId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingAddress WHERE ListingId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingBuildingInfo WHERE ListingId = @Id", new { Id = id }, transaction);
        if (listing.ListingValuationId is not null)
        {
            await connection.ExecuteAsync(
                "DELETE FROM ListingValuation WHERE Id = @Id", new { Id = listing.ListingValuationId }, transaction);
        }
        await connection.ExecuteAsync(
            "DELETE FROM Listings WHERE Id = @Id", new { Id = id }, transaction);

        transaction.Commit();
    }

    public async Task<Listing?> SubmitAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Listing>(
            "UPDATE Listings SET Status = 'submitted', ListDate = GETUTCDATE(), UpdatedAt = GETUTCDATE() " +
            "OUTPUT INSERTED.Id, INSERTED.ReferenceNumber, INSERTED.P24Ref, INSERTED.PropertyTypeId, INSERTED.ListingValuationId, INSERTED.ListDate, INSERTED.Status, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "WHERE Id = @Id AND Status = 'incomplete'",
            new { Id = id });
    }
}
