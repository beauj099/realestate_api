using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingBuildingInfoRepository : DapperRepository, IListingBuildingInfoRepository
{
    public ListingBuildingInfoRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<ListingBuildingInfo?> GetByListingIdAsync(int listingId) =>
        QuerySingleOrDefaultProcAsync<ListingBuildingInfo>(
            "sp_ListingBuildingInfo_GetByListingId", new { ListingId = listingId });

    public Task<ListingBuildingInfo> UpsertAsync(ListingBuildingInfo info) =>
        QuerySingleProcAsync<ListingBuildingInfo>(
            "sp_ListingBuildingInfo_Upsert",
            new
            {
                info.ListingId,
                info.ErfSize,
                info.FloorArea,
                info.ConstructionYear,
                info.FacingId,
                info.ZoningId
            });
}
