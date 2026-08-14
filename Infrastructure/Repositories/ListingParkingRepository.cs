using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingParkingRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ListingParkingRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ListingParking>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT lp.Id, lp.ListingId, lp.ParkingTypeId, lp.Quantity, pt.Description AS ParkingTypeDescription " +
            "FROM ListingParking lp INNER JOIN ParkingType pt ON pt.Id = lp.ParkingTypeId " +
            "WHERE lp.ListingId = @ListingId ORDER BY pt.Description",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingParking>(command);
    }

    public async Task<ListingParking?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT lp.Id, lp.ListingId, lp.ParkingTypeId, lp.Quantity, pt.Description AS ParkingTypeDescription " +
            "FROM ListingParking lp INNER JOIN ParkingType pt ON pt.Id = lp.ParkingTypeId WHERE lp.Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingParking>(command);
    }

    public async Task<ListingParking> CreateAsync(ListingParking parking, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DECLARE @Id INT; " +
            "INSERT INTO ListingParking (ListingId, ParkingTypeId, Quantity) VALUES (@ListingId, @ParkingTypeId, @Quantity); " +
            "SET @Id = SCOPE_IDENTITY(); " +
            "SELECT lp.Id, lp.ListingId, lp.ParkingTypeId, lp.Quantity, pt.Description AS ParkingTypeDescription " +
            "FROM ListingParking lp INNER JOIN ParkingType pt ON pt.Id = lp.ParkingTypeId WHERE lp.Id = @Id;",
            new { parking.ListingId, parking.ParkingTypeId, parking.Quantity },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingParking>(command);
    }

    public async Task<ListingParking?> UpdateAsync(int id, int quantity, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE ListingParking SET Quantity = @Quantity WHERE Id = @Id; " +
            "SELECT lp.Id, lp.ListingId, lp.ParkingTypeId, lp.Quantity, pt.Description AS ParkingTypeDescription " +
            "FROM ListingParking lp INNER JOIN ParkingType pt ON pt.Id = lp.ParkingTypeId WHERE lp.Id = @Id;",
            new { Id = id, Quantity = quantity }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingParking>(command);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM ListingParking WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}