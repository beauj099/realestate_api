using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingRepository : DapperRepository, IListingRepository
{
    public ListingRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<Listing?> GetByIdAsync(int id) =>
        QuerySingleOrDefaultProcAsync<Listing>("sp_Listings_GetById", new { Id = id });

    public Task<IEnumerable<Listing>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo) =>
        QueryProcAsync<Listing>(
            "sp_Listings_GetAll",
            new { Status = status, DateFrom = dateFrom, DateTo = dateTo });

    public Task<Listing> CreateAsync(int propertyTypeId, string? p24Ref) =>
        QuerySingleProcAsync<Listing>(
            "sp_Listings_Create",
            new { PropertyTypeId = propertyTypeId, P24Ref = p24Ref });

    public Task<Listing?> UpdateAsync(int id, string? status, string? p24Ref, int? propertyTypeId) =>
        QuerySingleOrDefaultProcAsync<Listing>(
            "sp_Listings_Update",
            new { Id = id, Status = status, P24Ref = p24Ref, PropertyTypeId = propertyTypeId });

    public Task DeleteAsync(int id) =>
        ExecuteProcAsync("sp_Listings_Delete", new { Id = id });

    public Task<Listing?> SubmitAsync(int id) =>
        QuerySingleOrDefaultProcAsync<Listing>("sp_Listings_Submit", new { Id = id });
}
