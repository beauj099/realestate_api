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
            "SELECT lv.Id, lv.OwnersNetPrice, lv.AgentValuation, lv.CommissionPercent, lv.LastPurchaseDate, lv.LastPurchasePriceZar, " +
            "lv.ValueLowZar, lv.ValueHighZar, lv.ListingPriceZar, lv.AdjustmentReason, lv.CommissionLatePercent, lv.CommissionEarlyMonths, lv.CommissionIncludesVat, lv.InterestRatePercent, lv.BondTermYears, lv.DepositPercent, lv.BondInstitution, lv.BondAmountZar " +
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
                "INSERT INTO ListingValuation (OwnersNetPrice, AgentValuation, CommissionPercent, LastPurchaseDate, LastPurchasePriceZar, " +
                "ValueLowZar, ValueHighZar, ListingPriceZar, AdjustmentReason, CommissionLatePercent, CommissionEarlyMonths, CommissionIncludesVat, InterestRatePercent, BondTermYears, DepositPercent, BondInstitution, BondAmountZar) " +
                "OUTPUT INSERTED.Id " +
                "VALUES (@OwnersNetPrice, @AgentValuation, @CommissionPercent, @LastPurchaseDate, @LastPurchasePriceZar, " +
                "@ValueLowZar, @ValueHighZar, @ListingPriceZar, @AdjustmentReason, @CommissionLatePercent, @CommissionEarlyMonths, @CommissionIncludesVat, @InterestRatePercent, @BondTermYears, @DepositPercent, @BondInstitution, @BondAmountZar)",
                valuation,
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
                "UPDATE ListingValuation SET OwnersNetPrice = @OwnersNetPrice, AgentValuation = @AgentValuation, CommissionPercent = @CommissionPercent, " +
                "LastPurchaseDate = @LastPurchaseDate, LastPurchasePriceZar = @LastPurchasePriceZar, " +
                "ValueLowZar = @ValueLowZar, ValueHighZar = @ValueHighZar, ListingPriceZar = @ListingPriceZar, AdjustmentReason = @AdjustmentReason, CommissionLatePercent = @CommissionLatePercent, CommissionEarlyMonths = @CommissionEarlyMonths, CommissionIncludesVat = @CommissionIncludesVat, InterestRatePercent = @InterestRatePercent, BondTermYears = @BondTermYears, DepositPercent = @DepositPercent, BondInstitution = @BondInstitution, BondAmountZar = @BondAmountZar WHERE Id = @Id",
                UpdateParameters(valuation, valuationId.Value),
                transaction: transaction, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(updateCommand);
        }

        var selectCommand = new CommandDefinition(
            "SELECT Id, OwnersNetPrice, AgentValuation, CommissionPercent, LastPurchaseDate, LastPurchasePriceZar, " +
            "ValueLowZar, ValueHighZar, ListingPriceZar, AdjustmentReason, CommissionLatePercent, CommissionEarlyMonths, CommissionIncludesVat, InterestRatePercent, BondTermYears, DepositPercent, BondInstitution, BondAmountZar FROM ListingValuation WHERE Id = @Id",
            new { Id = valuationId }, transaction: transaction, cancellationToken: cancellationToken);
        var result = await connection.QueryFirstOrDefaultAsync<ListingValuation>(selectCommand);

        transaction.Commit();
        return result!;
    }

    /// <summary>Every column of the valuation, with the row's own id.</summary>
    private static DynamicParameters UpdateParameters(ListingValuation valuation, int id)
    {
        var p = new DynamicParameters(valuation);
        p.Add("Id", id);
        return p;
    }
}
