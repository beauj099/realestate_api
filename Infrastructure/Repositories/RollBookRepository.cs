using Dapper;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

/// <summary>A roll-book row with the import it came from (current imports only).</summary>
public class RollBookEntryRow
{
    public int Erf { get; set; }
    public int Portion { get; set; }
    public bool IsGroupHead { get; set; }
    public int? GroupHeadErf { get; set; }
    public string? ValuedUnder { get; set; }
    public string? Category { get; set; }
    public string? Address { get; set; }
    public decimal? ExtentM2 { get; set; }
    public decimal? ValueZar { get; set; }
    public string? Particulars { get; set; }
    public string RollVersion { get; set; } = string.Empty;
    public DateTime DateOfValuation { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
}

/// <summary>Valuation rolls imported from published PDF books (tools/ImportRollBooks).</summary>
public class RollBookRepository(DbConnectionFactory connectionFactory)
{
    /// <summary>Every row for these erf numbers in the area (all portions, group heads and members).</summary>
    public async Task<IReadOnlyList<RollBookEntryRow>> GetAsync(string municipality, string area, IEnumerable<int> erven,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<RollBookEntryRow>(new CommandDefinition(
            @"SELECT e.Erf, e.Portion, e.IsGroupHead, e.GroupHeadErf, e.ValuedUnder, e.Category, e.Address,
                     e.ExtentM2, e.ValueZar, e.Particulars, i.RollVersion, i.DateOfValuation, i.EffectiveFrom, i.SourceUrl
              FROM RollBookEntries e
              JOIN RollBookImports i ON i.Id = e.ImportId AND i.IsCurrent = 1
              WHERE e.Municipality = @Municipality AND e.Area = @Area AND e.Erf IN @Erven",
            new { Municipality = municipality, Area = area.Trim().ToUpperInvariant(), Erven = erven.Distinct().ToArray() },
            cancellationToken: ct))).ToList();
    }
}
