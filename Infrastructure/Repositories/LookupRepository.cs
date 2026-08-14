using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class LookupRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public LookupRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<PropertyType>> GetPropertyTypesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Name, SortOrder, IsActive FROM PropertyType WHERE IsActive = 1 ORDER BY SortOrder ASC",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<PropertyType>(command);
    }

    public async Task<IEnumerable<RoomType>> GetRoomTypesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Description FROM RoomTypes ORDER BY Description",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<RoomType>(command);
    }

    public async Task<IEnumerable<Feature>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Category, Description FROM Feature ORDER BY Category, Description",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<Feature>(command);
    }

    public async Task<IEnumerable<ConditionCategory>> GetConditionCategoriesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Description FROM ConditionCategory ORDER BY Description",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<ConditionCategory>(command);
    }

    public async Task<IEnumerable<ParkingType>> GetParkingTypesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Description FROM ParkingType ORDER BY Description",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<ParkingType>(command);
    }

    public async Task<IEnumerable<Facing>> GetFacingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Description FROM Facing ORDER BY Description",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<Facing>(command);
    }

    public async Task<IEnumerable<Zoning>> GetZoningAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Description FROM Zoning ORDER BY Description",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<Zoning>(command);
    }
}
