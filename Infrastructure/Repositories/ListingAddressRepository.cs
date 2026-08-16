using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingAddressRepository : DapperRepository, IListingAddressRepository
{
    public ListingAddressRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<ListingAddress?> GetByListingIdAsync(int listingId) =>
        QuerySingleOrDefaultProcAsync<ListingAddress>(
            "sp_ListingAddress_GetByListingId", new { ListingId = listingId });

    public Task<ListingAddress> UpsertAsync(ListingAddress address) =>
        QuerySingleProcAsync<ListingAddress>(
            "sp_ListingAddress_Upsert",
            new
            {
                address.ListingId,
                address.ErfNumber,
                address.EstateName,
                address.StreetNumber,
                address.UnitNumber,
                address.Street,
                address.Suburb,
                address.City,
                address.Province,
                address.Country,
                address.PostalCode,
                address.Latitude,
                address.Longitude
            });
}
