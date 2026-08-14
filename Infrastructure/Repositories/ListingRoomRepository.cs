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

    public async Task<IEnumerable<ListingRoom>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt FROM ListingRoom WHERE ListingId = @ListingId ORDER BY CreatedAt",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingRoom>(command);
    }

    public async Task<ListingRoom?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt FROM ListingRoom WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingRoom>(command);
    }

    public async Task UpdatePhotoUrlAsync(int roomId, string? photoUrl, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE ListingRoom SET PhotoUrl = @PhotoUrl, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = roomId, PhotoUrl = photoUrl }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<ListingRoom> CreateAsync(ListingRoom room, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO ListingRoom (ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingId, INSERTED.Name, INSERTED.RoomTypeId, INSERTED.RoomTypeOther, INSERTED.PhotoUrl, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "VALUES (@ListingId, @Name, @RoomTypeId, @RoomTypeOther, @PhotoUrl, GETUTCDATE(), GETUTCDATE())",
            new { room.ListingId, room.Name, room.RoomTypeId, room.RoomTypeOther, room.PhotoUrl },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingRoom>(command);
    }

    public async Task<ListingRoom?> UpdateAsync(ListingRoom room, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE ListingRoom SET Name = COALESCE(@Name, Name), RoomTypeId = COALESCE(@RoomTypeId, RoomTypeId), " +
            "RoomTypeOther = COALESCE(@RoomTypeOther, RoomTypeOther), PhotoUrl = COALESCE(@PhotoUrl, PhotoUrl), UpdatedAt = GETUTCDATE() " +
            "OUTPUT INSERTED.Id, INSERTED.ListingId, INSERTED.Name, INSERTED.RoomTypeId, INSERTED.RoomTypeOther, INSERTED.PhotoUrl, INSERTED.CreatedAt, INSERTED.UpdatedAt " +
            "WHERE Id = @Id",
            new { room.Id, room.Name, room.RoomTypeId, room.RoomTypeOther, room.PhotoUrl },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingRoom>(command);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Condition WHERE ListingRoomId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoomFeature WHERE ListingRoomId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoomCustomFeature WHERE ListingRoomId = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ListingRoom WHERE Id = @Id", new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
    }

    public async Task<Condition?> GetConditionByRoomIdAsync(int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ListingRoomId, ConditionRating, Notes, ConditionCategoryId FROM Condition WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Condition>(command);
    }

    public async Task<IEnumerable<Condition>> GetConditionsByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT c.Id, c.ListingRoomId, c.ConditionRating, c.Notes, c.ConditionCategoryId " +
            "FROM Condition c INNER JOIN ListingRoom r ON r.Id = c.ListingRoomId WHERE r.ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<Condition>(command);
    }

    public async Task<Condition> UpsertConditionAsync(Condition condition, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "MERGE Condition AS t USING (SELECT @ListingRoomId AS ListingRoomId) AS s ON t.ListingRoomId = s.ListingRoomId " +
            "WHEN MATCHED THEN UPDATE SET ConditionRating = @ConditionRating, Notes = @Notes, ConditionCategoryId = @ConditionCategoryId " +
            "WHEN NOT MATCHED THEN INSERT (ListingRoomId, ConditionRating, Notes, ConditionCategoryId) " +
            "VALUES (@ListingRoomId, @ConditionRating, @Notes, @ConditionCategoryId) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingRoomId, INSERTED.ConditionRating, INSERTED.Notes, INSERTED.ConditionCategoryId;",
            condition, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Condition>(command);
    }

    public async Task<IEnumerable<Feature>> GetLinkedFeaturesAsync(int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT f.Id, f.Category, f.Description FROM ListingRoomFeature lrf " +
            "JOIN Feature f ON f.Id = lrf.FeatureId WHERE lrf.ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<Feature>(command);
    }

    public async Task<Dictionary<int, List<Feature>>> GetLinkedFeaturesByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT lrf.ListingRoomId AS RoomId, f.Id, f.Category, f.Description " +
            "FROM ListingRoomFeature lrf " +
            "JOIN Feature f ON f.Id = lrf.FeatureId " +
            "JOIN ListingRoom r ON r.Id = lrf.ListingRoomId WHERE r.ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<RoomFeatureRow>(command);
        return rows
            .GroupBy(r => r.RoomId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new Feature { Id = r.Id, Category = r.Category, Description = r.Description }).ToList());
    }

    private sealed class RoomFeatureRow
    {
        public int RoomId { get; set; }
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public async Task<IEnumerable<Feature>> LinkFeatureAsync(int listingRoomId, int featureId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "IF NOT EXISTS (SELECT 1 FROM ListingRoomFeature WHERE ListingRoomId = @ListingRoomId AND FeatureId = @FeatureId) " +
            "INSERT INTO ListingRoomFeature (ListingRoomId, FeatureId) VALUES (@ListingRoomId, @FeatureId)",
            new { ListingRoomId = listingRoomId, FeatureId = featureId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
        return await GetLinkedFeaturesAsync(listingRoomId, cancellationToken);
    }

    public async Task<IEnumerable<Feature>> UnlinkFeatureAsync(int listingRoomId, int featureId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM ListingRoomFeature WHERE ListingRoomId = @ListingRoomId AND FeatureId = @FeatureId",
            new { ListingRoomId = listingRoomId, FeatureId = featureId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
        return await GetLinkedFeaturesAsync(listingRoomId, cancellationToken);
    }

    public async Task<IEnumerable<ListingRoomCustomFeature>> GetCustomFeaturesAsync(int listingRoomId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, ListingRoomId, Description FROM ListingRoomCustomFeature WHERE ListingRoomId = @ListingRoomId",
            new { ListingRoomId = listingRoomId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingRoomCustomFeature>(command);
    }

    public async Task<IEnumerable<ListingRoomCustomFeature>> GetCustomFeaturesByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT cf.Id, cf.ListingRoomId, cf.Description FROM ListingRoomCustomFeature cf " +
            "JOIN ListingRoom r ON r.Id = cf.ListingRoomId WHERE r.ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingRoomCustomFeature>(command);
    }

    public async Task<ListingRoomCustomFeature> AddCustomFeatureAsync(ListingRoomCustomFeature feature, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO ListingRoomCustomFeature (ListingRoomId, Description) " +
            "OUTPUT INSERTED.Id, INSERTED.ListingRoomId, INSERTED.Description " +
            "VALUES (@ListingRoomId, @Description)",
            new { feature.ListingRoomId, feature.Description }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingRoomCustomFeature>(command);
    }

    public async Task DeleteCustomFeatureAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM ListingRoomCustomFeature WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
