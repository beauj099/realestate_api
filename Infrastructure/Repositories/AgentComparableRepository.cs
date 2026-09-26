using System.Data;
using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class AgentComparableRepository
{
    private const string Columns =
        "Id, CapturedByUserId, CapturedAt, Municipality, Suburb, Address, Erf, Latitude, Longitude, ErfM2, FloorM2, " +
        "Bedrooms, Bathrooms, Garages, PropertyTypeId, Condition, SaleDate, SalePriceZar, EvidenceLevel, Notes, " +
        "CorroborationCount, Verification, VerifiedAgainst, VerifiedAt, PricePerFloorM2, Weight";

    private readonly DbConnectionFactory _connectionFactory;

    public AgentComparableRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Adds a sale, or — when the same sale is already logged (same area and property, within 45
    /// days and 2% of the price) — corroborates it: a second agent confirming hearsay promotes it.
    /// One agent re-entering their own sale is just a duplicate. Returns the entry and whether it
    /// already existed.
    /// </summary>
    public async Task<(Guid Id, bool WasDuplicate)> AddOrCorroborateAsync(AgentComparable c, Func<ExistingSale, bool> isSameProperty,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        // Sales in the area at about that date and price; which of them is the same property
        // (address spelling, erf) is the caller's call.
        var nearby = await connection.QueryAsync<ExistingSale>(new CommandDefinition(
            @"SELECT Id, CapturedByUserId, Address, Erf FROM AgentComparables WITH (UPDLOCK, HOLDLOCK)
              WHERE Municipality = @Municipality AND Suburb = @Suburb
                AND ABS(DATEDIFF(DAY, SaleDate, @SaleDate)) <= 45
                AND ABS(SalePriceZar - @SalePriceZar) <= @SalePriceZar * 0.02
              ORDER BY CapturedAt",
            new { c.Municipality, c.Suburb, c.SaleDate, c.SalePriceZar },
            transaction: transaction, cancellationToken: cancellationToken));
        var existing = nearby.FirstOrDefault(isSameProperty);

        if (existing is not null)
        {
            var hit = existing;
            if (hit.CapturedByUserId != c.CapturedByUserId)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    @"UPDATE AgentComparables
                      SET CorroborationCount = CorroborationCount + 1,
                          EvidenceLevel = CASE WHEN EvidenceLevel = 'Hearsay' THEN 'ColleagueConfirmed' ELSE EvidenceLevel END
                      WHERE Id = @Id",
                    new { hit.Id }, transaction: transaction, cancellationToken: cancellationToken));
            }
            transaction.Commit();
            return (hit.Id, true);
        }

        var id = await connection.QuerySingleAsync<Guid>(new CommandDefinition(
            @"INSERT INTO AgentComparables
                (CapturedByUserId, Municipality, Suburb, Address, Erf, Latitude, Longitude, ErfM2, FloorM2, Bedrooms,
                 Bathrooms, Garages, PropertyTypeId, Condition, SaleDate, SalePriceZar, EvidenceLevel, Notes)
              OUTPUT INSERTED.Id
              VALUES
                (@CapturedByUserId, @Municipality, @Suburb, @Address, @Erf, @Latitude, @Longitude, @ErfM2, @FloorM2, @Bedrooms,
                 @Bathrooms, @Garages, @PropertyTypeId, @Condition, @SaleDate, @SalePriceZar, @EvidenceLevel, @Notes)",
            c, transaction: transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return (id, false);
    }

    /// <summary>Sales in a suburb since a date, newest first.</summary>
    public async Task<IEnumerable<AgentComparable>> GetInAreaAsync(string municipality, string suburb, DateTime since,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<AgentComparable>(new CommandDefinition(
            $"SELECT {Columns} FROM AgentComparables WHERE Municipality = @Municipality AND Suburb = @Suburb " +
            "AND SaleDate >= @Since AND Verification <> 'Rejected' ORDER BY SaleDate DESC",
            new { Municipality = municipality, Suburb = suburb, Since = since }, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<AgentComparable>> GetByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<AgentComparable>(new CommandDefinition(
            $"SELECT {Columns} FROM AgentComparables WHERE CapturedByUserId = @UserId ORDER BY CapturedAt DESC",
            new { UserId = userId }, cancellationToken: cancellationToken));
    }

    /// <summary>Deletes an entry the agent captured. False when it is not theirs (or not there).</summary>
    public async Task<bool> DeleteOwnAsync(Guid id, int userId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM AgentComparables WHERE Id = @Id AND CapturedByUserId = @UserId",
            new { Id = id, UserId = userId }, cancellationToken: cancellationToken)) > 0;
    }

    /// <summary>Records the outcome of checking an entry against a municipal sales record.</summary>
    public async Task SetVerificationAsync(Guid id, string verification, string? evidenceLevel, string against,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE AgentComparables
              SET Verification = @Verification, EvidenceLevel = COALESCE(@EvidenceLevel, EvidenceLevel),
                  VerifiedAgainst = @Against, VerifiedAt = GETUTCDATE()
              WHERE Id = @Id AND Verification = 'Unverified'",
            new { Id = id, Verification = verification, EvidenceLevel = evidenceLevel, Against = against },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The agency's own listings in a suburb, for "on the market nearby": listings by any agent
    /// whose AgencyName matches the requesting agent's (or the agent's own when they have none).
    /// </summary>
    public async Task<IEnumerable<MarketListingRow>> GetAgencyListingsInSuburbAsync(int userId, string suburb,
        int? excludeListingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<MarketListingRow>(new CommandDefinition(
            @"SELECT l.Id AS ListingId, a.StreetNumber, a.Street, a.Suburb, l.PropertyTypeId,
                     v.AgentValuation, b.ErfSize, b.FloorArea, l.Status, l.ListDate, l.CreatedAt, l.ArchivedAt
              FROM Listings l
              JOIN ListingAddress a ON a.ListingId = l.Id
              JOIN Users u ON u.Id = l.UserId
              LEFT JOIN ListingValuation v ON v.Id = l.ListingValuationId
              LEFT JOIN ListingBuildingInfo b ON b.ListingId = l.Id
              WHERE UPPER(LTRIM(RTRIM(a.Suburb))) = UPPER(@Suburb)
                AND (@ExcludeListingId IS NULL OR l.Id <> @ExcludeListingId)
                AND (l.UserId = @UserId OR (
                      NULLIF(LTRIM(RTRIM(u.AgencyName)), '') IS NOT NULL
                      AND UPPER(LTRIM(RTRIM(u.AgencyName))) =
                          (SELECT UPPER(LTRIM(RTRIM(me.AgencyName))) FROM Users me WHERE me.Id = @UserId)))
              ORDER BY l.CreatedAt DESC",
            new { UserId = userId, Suburb = suburb.Trim(), ExcludeListingId = excludeListingId },
            cancellationToken: cancellationToken));
    }
}

public class ExistingSale
{
    public Guid Id { get; set; }
    public int CapturedByUserId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? Erf { get; set; }
}

public class MarketListingRow
{
    public int ListingId { get; set; }
    public string? StreetNumber { get; set; }
    public string? Street { get; set; }
    public string? Suburb { get; set; }
    public int? PropertyTypeId { get; set; }
    public decimal? AgentValuation { get; set; }
    public decimal? ErfSize { get; set; }
    public decimal? FloorArea { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ListDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
