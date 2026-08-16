using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class LookupRepository : DapperRepository, ILookupRepository
{
    public LookupRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IEnumerable<PropertyType>> GetPropertyTypesAsync() =>
        QueryProcAsync<PropertyType>("sp_PropertyTypes_GetAll");

    public Task<IEnumerable<RoomType>> GetRoomTypesAsync() =>
        QueryProcAsync<RoomType>("sp_RoomTypes_GetAll");

    public Task<IEnumerable<Feature>> GetFeaturesAsync() =>
        QueryProcAsync<Feature>("sp_Features_GetAll");

    public Task<IEnumerable<ConditionCategory>> GetConditionCategoriesAsync() =>
        QueryProcAsync<ConditionCategory>("sp_ConditionCategories_GetAll");

    public Task<IEnumerable<ParkingType>> GetParkingTypesAsync() =>
        QueryProcAsync<ParkingType>("sp_ParkingTypes_GetAll");

    public Task<IEnumerable<Facing>> GetFacingAsync() =>
        QueryProcAsync<Facing>("sp_Facing_GetAll");

    public Task<IEnumerable<Zoning>> GetZoningAsync() =>
        QueryProcAsync<Zoning>("sp_Zoning_GetAll");
}
