using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingBuildingInfoRepository
{
    private const string Columns = "Id, ListingId, ErfSize, FloorArea, ConstructionYear, FacingId, ZoningId";

    private readonly DbConnectionFactory _connectionFactory;

    public ListingBuildingInfoRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ListingBuildingInfo?> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingBuildingInfo WHERE ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingBuildingInfo>(command);
    }

    public async Task<ListingBuildingInfo> UpsertAsync(ListingBuildingInfo info, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "MERGE ListingBuildingInfo AS t " +
            "USING (SELECT @ListingId AS ListingId) AS s " +
            "ON t.ListingId = s.ListingId " +
            "WHEN MATCHED THEN UPDATE SET " +
            "ErfSize = @ErfSize, FloorArea = @FloorArea, ConstructionYear = @ConstructionYear, FacingId = @FacingId, ZoningId = @ZoningId " +
            "WHEN NOT MATCHED THEN INSERT (ListingId, ErfSize, FloorArea, ConstructionYear, FacingId, ZoningId) " +
            "VALUES (@ListingId, @ErfSize, @FloorArea, @ConstructionYear, @FacingId, @ZoningId) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")};",
            info, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingBuildingInfo>(command);
    }
}