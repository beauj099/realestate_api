using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingParkingRepository : DapperRepository, IListingParkingRepository
{
    public ListingParkingRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IEnumerable<ListingParking>> GetByListingIdAsync(int listingId) =>
        QueryProcAsync<ListingParking>("sp_ListingParking_GetByListingId", new { ListingId = listingId });

    public Task<ListingParking> CreateAsync(ListingParking parking) =>
        QuerySingleProcAsync<ListingParking>(
            "sp_ListingParking_Create",
            new { parking.ListingId, parking.ParkingTypeId, parking.Quantity });

    public Task<ListingParking?> UpdateAsync(int id, int quantity) =>
        QuerySingleOrDefaultProcAsync<ListingParking>(
            "sp_ListingParking_Update", new { Id = id, Quantity = quantity });

    public Task DeleteAsync(int id) =>
        ExecuteProcAsync("sp_ListingParking_Delete", new { Id = id });
}
