using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingRoomRepository : DapperRepository, IListingRoomRepository
{
    public ListingRoomRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IEnumerable<ListingRoom>> GetByListingIdAsync(int listingId) =>
        QueryProcAsync<ListingRoom>("sp_ListingRooms_GetByListingId", new { ListingId = listingId });

    public Task<RoomDetails> GetRoomDetailsByListingIdAsync(int listingId) =>
        QueryMultipleProcAsync(
            "sp_ListingRooms_GetDetailsByListingId",
            new { ListingId = listingId },
            async multi =>
            {
                var rooms = (await multi.ReadAsync<ListingRoom>()).ToList();
                var conditions = (await multi.ReadAsync<Condition>()).ToList();
                var features = (await multi.ReadAsync<RoomLinkedFeature>()).ToList();
                var customFeatures = (await multi.ReadAsync<ListingRoomCustomFeature>()).ToList();
                return new RoomDetails(rooms, conditions, features, customFeatures);
            });

    public Task<ListingRoom?> GetByIdAsync(int id) =>
        QuerySingleOrDefaultSqlAsync<ListingRoom>(
            "SELECT Id, ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt FROM ListingRoom WHERE Id = @Id",
            new { Id = id });

    public Task UpdatePhotoUrlAsync(int roomId, string? photoUrl) =>
        ExecuteSqlAsync(
            "UPDATE ListingRoom SET PhotoUrl = @PhotoUrl, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = roomId, PhotoUrl = photoUrl });

    public Task<ListingRoom> CreateAsync(ListingRoom room) =>
        QuerySingleProcAsync<ListingRoom>(
            "sp_ListingRooms_Create",
            new { room.ListingId, room.Name, room.RoomTypeId, room.RoomTypeOther, room.PhotoUrl });

    public Task<ListingRoom?> UpdateAsync(ListingRoom room) =>
        QuerySingleOrDefaultProcAsync<ListingRoom>(
            "sp_ListingRooms_Update",
            new { room.Id, room.Name, room.RoomTypeId, room.RoomTypeOther, room.PhotoUrl });

    public Task DeleteAsync(int id) =>
        ExecuteProcAsync("sp_ListingRooms_Delete", new { Id = id });

    public Task<Condition?> GetConditionByRoomIdAsync(int listingRoomId) =>
        QuerySingleOrDefaultProcAsync<Condition>(
            "sp_Condition_GetByListingRoomId", new { ListingRoomId = listingRoomId });

    public Task<Condition> UpsertConditionAsync(Condition condition) =>
        QuerySingleProcAsync<Condition>(
            "sp_Condition_Upsert",
            new
            {
                condition.ListingRoomId,
                condition.ConditionRating,
                condition.Notes,
                condition.ConditionCategoryId
            });

    public Task<IEnumerable<Feature>> GetLinkedFeaturesAsync(int listingRoomId) =>
        QueryProcAsync<Feature>(
            "sp_ListingRoomFeatures_GetByListingRoomId", new { ListingRoomId = listingRoomId });

    public Task<IEnumerable<Feature>> LinkFeatureAsync(int listingRoomId, int featureId) =>
        QueryProcAsync<Feature>(
            "sp_ListingRoomFeatures_Link", new { ListingRoomId = listingRoomId, FeatureId = featureId });

    public Task<IEnumerable<Feature>> UnlinkFeatureAsync(int listingRoomId, int featureId) =>
        QueryProcAsync<Feature>(
            "sp_ListingRoomFeatures_Unlink", new { ListingRoomId = listingRoomId, FeatureId = featureId });

    public Task<IEnumerable<ListingRoomCustomFeature>> GetCustomFeaturesAsync(int listingRoomId) =>
        QueryProcAsync<ListingRoomCustomFeature>(
            "sp_ListingRoomCustomFeatures_GetByListingRoomId", new { ListingRoomId = listingRoomId });

    public Task<ListingRoomCustomFeature> AddCustomFeatureAsync(ListingRoomCustomFeature feature) =>
        QuerySingleProcAsync<ListingRoomCustomFeature>(
            "sp_ListingRoomCustomFeatures_Add",
            new { feature.ListingRoomId, feature.Description });

    public Task DeleteCustomFeatureAsync(int id) =>
        ExecuteProcAsync("sp_ListingRoomCustomFeatures_Remove", new { Id = id });
}
