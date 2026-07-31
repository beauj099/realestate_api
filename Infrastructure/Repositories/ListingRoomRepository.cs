using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingRoomRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ListingRoomRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ListingRoom>> GetByListingIdAsync(int listingId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<ListingRoom>(
            "SELECT Id, ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt FROM ListingRoom WHERE ListingId = @ListingId ORDER BY CreatedAt",
            new { ListingId = listingId });
    }

    public async Task<ListingRoom?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ListingRoom>(
            "SELECT Id, ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt FROM ListingRoom WHERE Id = @Id",
            new { Id = id });
    }

    public async Task UpdatePhotoUrlAsync(int roomId, string? photoUrl)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE ListingRoom SET PhotoUrl = @PhotoUrl, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = roomId, PhotoUrl = photoUrl });
    }

    public async Task<ListingRoom> CreateAsync(ListingRoom room)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ListingRoom>(
            "INSERT INTO ListingRoom (ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingId, INSERTED.Name, INSERTED.RoomTypeId, INSERTED.RoomTypeOther, INSERTED.PhotoUrl, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "VALUES (@ListingId, @Name, @RoomTypeId, @RoomTypeOther, @PhotoUrl, GETUTCDATE(), GETUTCDATE())",
            new { room.ListingId, room.Name, room.RoomTypeId, room.RoomTypeOther, room.PhotoUrl });
    }

    public async Task<ListingRoom?> UpdateAsync(ListingRoom room)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ListingRoom>(
            "UPDATE ListingRoom SET Name = COALESCE(@Name, Name), RoomTypeId = COALESCE(@RoomTypeId, RoomTypeId), " +
            "RoomTypeOther = COALESCE(@RoomTypeOther, RoomTypeOther), PhotoUrl = COALESCE(@PhotoUrl, PhotoUrl), UpdatedAt = GETUTCDATE() " +
            "OUTPUT INSERTED.Id, INSERTED.ListingId, INSERTED.Name, INSERTED.RoomTypeId, INSERTED.RoomTypeOther, INSERTED.PhotoUrl, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "WHERE Id = @Id",
            new { room.Id, room.Name, room.RoomTypeId, room.RoomTypeOther, room.PhotoUrl });
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            "DELETE FROM Condition WHERE ListingRoomId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoomFeature WHERE ListingRoomId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoomCustomFeature WHERE ListingRoomId = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoom WHERE Id = @Id", new { Id = id }, transaction);

        transaction.Commit();
    }

    public async Task<Condition?> GetConditionByRoomIdAsync(int listingRoomId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Condition>(
            "SELECT Id, ListingRoomId, ConditionRating, Notes, ConditionCategoryId FROM Condition WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId });
    }

    public async Task<Condition> UpsertConditionAsync(Condition condition)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Condition>(
            "MERGE Condition AS t USING (SELECT @ListingRoomId AS ListingRoomId) AS s ON t.ListingRoomId = s.ListingRoomId " +
            "WHEN MATCHED THEN UPDATE SET ConditionRating = @ConditionRating, Notes = @Notes, ConditionCategoryId = @ConditionCategoryId " +
            "WHEN NOT MATCHED THEN INSERT (ListingRoomId, ConditionRating, Notes, ConditionCategoryId) " +
            "VALUES (@ListingRoomId, @ConditionRating, @Notes, @ConditionCategoryId) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingRoomId, INSERTED.ConditionRating, INSERTED.Notes, INSERTED.ConditionCategoryId;",
            condition);
    }

    public async Task<IEnumerable<Feature>> GetLinkedFeaturesAsync(int listingRoomId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Feature>(
            "SELECT f.Id, f.Category, f.Description FROM ListingRoomFeature lrf " +
            "JOIN Feature f ON f.Id = lrf.FeatureId WHERE lrf.ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId });
    }

    public async Task<IEnumerable<Feature>> LinkFeatureAsync(int listingRoomId, int featureId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "IF NOT EXISTS (SELECT 1 FROM ListingRoomFeature WHERE ListingRoomId = @ListingRoomId AND FeatureId = @FeatureId) " +
            "INSERT INTO ListingRoomFeature (ListingRoomId, FeatureId) VALUES (@ListingRoomId, @FeatureId)",
            new { ListingRoomId = listingRoomId, FeatureId = featureId });
        return await GetLinkedFeaturesAsync(listingRoomId);
    }

    public async Task<IEnumerable<Feature>> UnlinkFeatureAsync(int listingRoomId, int featureId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoomFeature WHERE ListingRoomId = @ListingRoomId AND FeatureId = @FeatureId",
            new { ListingRoomId = listingRoomId, FeatureId = featureId });
        return await GetLinkedFeaturesAsync(listingRoomId);
    }

    public async Task<IEnumerable<ListingRoomCustomFeature>> GetCustomFeaturesAsync(int listingRoomId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<ListingRoomCustomFeature>(
            "SELECT Id, ListingRoomId, Description FROM ListingRoomCustomFeature WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId });
    }

    public async Task<ListingRoomCustomFeature> AddCustomFeatureAsync(ListingRoomCustomFeature feature)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ListingRoomCustomFeature>(
            "INSERT INTO ListingRoomCustomFeature (ListingRoomId, Description) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingRoomId, INSERTED.Description " +
            "VALUES (@ListingRoomId, @Description)",
            new { feature.ListingRoomId, feature.Description });
    }

    public async Task DeleteCustomFeatureAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "DELETE FROM ListingRoomCustomFeature WHERE Id = @Id", new { Id = id });
    }
}
