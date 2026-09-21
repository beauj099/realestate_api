using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class PropertyRunningCostsRepository
{
    private const string Columns = "Id, ListingId, MonthlyLevy, MonthlyRates, Electricity, Water, MunicipalAccount";

    private readonly DbConnectionFactory _connectionFactory;

    public PropertyRunningCostsRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PropertyRunningCosts?> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM PropertyRunningCosts WHERE ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<PropertyRunningCosts>(command);
    }

    public async Task<PropertyRunningCosts> UpsertAsync(PropertyRunningCosts costs, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "MERGE PropertyRunningCosts AS t " +
            "USING (SELECT @ListingId AS ListingId) AS s " +
            "ON t.ListingId = s.ListingId " +
            "WHEN MATCHED THEN UPDATE SET MonthlyLevy = @MonthlyLevy, MonthlyRates = @MonthlyRates, Electricity = @Electricity, Water = @Water, MunicipalAccount = @MunicipalAccount " +
            "WHEN NOT MATCHED THEN INSERT (ListingId, MonthlyLevy, MonthlyRates, Electricity, Water, MunicipalAccount) " +
            "VALUES (@ListingId, @MonthlyLevy, @MonthlyRates, @Electricity, @Water, @MunicipalAccount) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")};",
            costs, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<PropertyRunningCosts>(command);
    }
}