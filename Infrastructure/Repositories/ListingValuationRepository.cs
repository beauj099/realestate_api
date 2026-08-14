using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingValuationRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ListingValuationRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ListingValuation?> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT lv.Id, lv.OwnersNetPrice, lv.AgentValuation, lv.CommissionPercent " +
            "FROM ListingValuation lv INNER JOIN Listings l ON l.ListingValuationId = lv.Id WHERE l.Id = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingValuation>(command);
    }

    public async Task<ListingValuation> UpsertAsync(int listingId, ListingValuation valuation, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var lookupCommand = new CommandDefinition(
            "SELECT ListingValuationId FROM Listings WITH (UPDLOCK, HOLDLOCK) WHERE Id = @ListingId",
            new { ListingId = listingId }, transaction: transaction, cancellationToken: cancellationToken);
        var valuationId = await connection.ExecuteScalarAsync<int?>(lookupCommand);

        if (valuationId is null)
        {
            var insertCommand = new CommandDefinition(
                "INSERT INTO ListingValuation (OwnersNetPrice, AgentValuation, CommissionPercent) " +
                "OUTPUT INSERTED.Id " +
                "VALUES (@OwnersNetPrice, @AgentValuation, @CommissionPercent)",
                new { valuation.OwnersNetPrice, valuation.AgentValuation, valuation.CommissionPercent },
                transaction: transaction, cancellationToken: cancellationToken);
            valuationId = await connection.ExecuteScalarAsync<int>(insertCommand);

            var linkCommand = new CommandDefinition(
                "UPDATE Listings SET ListingValuationId = @ValuationId, UpdatedAt = GETUTCDATE() WHERE Id = @ListingId",
                new { ValuationId = valuationId, ListingId = listingId },
                transaction: transaction, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(linkCommand);
        }
        else
        {
            var updateCommand = new CommandDefinition(
                "UPDATE ListingValuation SET OwnersNetPrice = @OwnersNetPrice, AgentValuation = @AgentValuation, CommissionPercent = @CommissionPercent WHERE Id = @Id",
                new { valuation.OwnersNetPrice, valuation.AgentValuation, valuation.CommissionPercent, Id = valuationId },
                transaction: transaction, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(updateCommand);
        }

        var selectCommand = new CommandDefinition(
            "SELECT Id, OwnersNetPrice, AgentValuation, CommissionPercent FROM ListingValuation WHERE Id = @Id",
            new { Id = valuationId }, transaction: transaction, cancellationToken: cancellationToken);
        var result = await connection.QueryFirstOrDefaultAsync<ListingValuation>(selectCommand);

        transaction.Commit();
        return result!;
    }
}