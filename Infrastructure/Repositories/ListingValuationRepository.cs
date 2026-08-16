using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingValuationRepository : DapperRepository, IListingValuationRepository
{
    public ListingValuationRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<ListingValuation?> GetByListingIdAsync(int listingId) =>
        QuerySingleOrDefaultProcAsync<ListingValuation>(
            "sp_ListingValuation_GetByListingId", new { ListingId = listingId });

    public Task<ListingValuation> UpsertAsync(int listingId, ListingValuation valuation) =>
        QuerySingleProcAsync<ListingValuation>(
            "sp_ListingValuation_Upsert",
            new
            {
                ListingId = listingId,
                valuation.OwnersNetPrice,
                valuation.AgentValuation,
                valuation.CommissionPercent
            });
}
