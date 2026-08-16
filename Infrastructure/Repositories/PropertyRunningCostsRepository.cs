using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class PropertyRunningCostsRepository : DapperRepository, IPropertyRunningCostsRepository
{
    public PropertyRunningCostsRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<PropertyRunningCosts?> GetByListingIdAsync(int listingId) =>
        QuerySingleOrDefaultProcAsync<PropertyRunningCosts>(
            "sp_PropertyRunningCosts_GetByListingId", new { ListingId = listingId });

    public Task<PropertyRunningCosts> UpsertAsync(PropertyRunningCosts costs) =>
        QuerySingleProcAsync<PropertyRunningCosts>(
            "sp_PropertyRunningCosts_Upsert",
            new
            {
                costs.ListingId,
                costs.MonthlyLevy,
                costs.MonthlyRates,
                costs.Electricity,
                costs.Water
            });
}
