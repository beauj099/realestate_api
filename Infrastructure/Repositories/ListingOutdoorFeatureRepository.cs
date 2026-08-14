using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingOutdoorFeatureRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ListingOutdoorFeatureRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ListingOutdoorFeature>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ListingId, Description FROM ListingOutdoorFeature WHERE ListingId = @ListingId ORDER BY Id",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingOutdoorFeature>(command);
    }

    public async Task<ListingOutdoorFeature?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ListingId, Description FROM ListingOutdoorFeature WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingOutdoorFeature>(command);
    }

    public async Task<ListingOutdoorFeature> AddAsync(ListingOutdoorFeature feature, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO ListingOutdoorFeature (ListingId, Description) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingId, INSERTED.Description " +
            "VALUES (@ListingId, @Description)",
            new { feature.ListingId, feature.Description }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingOutdoorFeature>(command);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM ListingOutdoorFeature WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<IEnumerable<ListingOutdoorFeature>> ReplaceAllAsync(int listingId, IEnumerable<string> descriptions, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var tx = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingOutdoorFeature WHERE ListingId = @ListingId",
            new { ListingId = listingId }, transaction: tx, cancellationToken: cancellationToken));

        foreach (var desc in descriptions)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ListingOutdoorFeature (ListingId, Description) VALUES (@ListingId, @Description)",
                new { ListingId = listingId, Description = desc }, transaction: tx, cancellationToken: cancellationToken));
        }

        tx.Commit();
        return await GetByListingIdAsync(listingId, cancellationToken);
    }
}