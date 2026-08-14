using RealEstateApi.Application.DTOs;

namespace RealEstateApi.IntegrationTests;

[Collection("Database")]
public class RoomTests
{
    private readonly DatabaseFixture _fixture;

    public RoomTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<int> CreateListingAsync()
    {
        var service = Services.Listings(_fixture);
        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));
        return created.Id;
    }

    [Fact]
    public async Task CreateAndUpdate_Room()
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingId = await CreateListingAsync();

        var created = await svc.CreateRoomAsync(listingId, new CreateRoomRequest("Bedroom 1", _fixture.RoomTypeId, null, null));
        Assert.Equal("Bedroom 1", created.Name);
        Assert.Equal(listingId, created.ListingId);
        Assert.Null(created.Condition);

        var updated = await svc.UpdateRoomAsync(listingId, created.Id, new UpdateRoomRequest("Main bedroom", _fixture.RoomTypeId, null, null));
        Assert.NotNull(updated);
        Assert.Equal("Main bedroom", updated.Name);
    }

    [Fact]
    public async Task GetRooms_ReturnsRoomsWithChildren()
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingId = await CreateListingAsync();

        var room = await svc.CreateRoomAsync(listingId, new CreateRoomRequest("Bedroom", _fixture.RoomTypeId, null, null));
        await svc.UpsertConditionAsync(listingId, room.Id, new UpsertRoomConditionRequest(4.5m, "Good", _fixture.ConditionCategoryId));
        await svc.LinkFeatureAsync(listingId, room.Id, _fixture.FeatureId);
        await svc.AddCustomFeatureAsync(listingId, room.Id, new AddCustomFeatureRequest("Walk-in closet"));

        var rooms = (await svc.GetRoomsAsync(listingId)).ToList();

        var dto = Assert.Single(rooms);
        Assert.Equal(4.5m, dto.Condition?.ConditionRating);
        Assert.Single(dto.Features);
        Assert.Single(dto.CustomFeatures);
    }

    [Fact]
    public async Task UpsertCondition_CreatesThenUpdates()
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingId = await CreateListingAsync();
        var room = await svc.CreateRoomAsync(listingId, new CreateRoomRequest("Bedroom", _fixture.RoomTypeId, null, null));

        var first = await svc.UpsertConditionAsync(listingId, room.Id, new UpsertRoomConditionRequest(3.5m, "Fair", _fixture.ConditionCategoryId));
        var second = await svc.UpsertConditionAsync(listingId, room.Id, new UpsertRoomConditionRequest(5.5m, "Excellent", _fixture.ConditionCategoryId));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(5.5m, second.ConditionRating);
    }

    [Fact]
    public async Task LinkAndUnlinkFeature()
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingId = await CreateListingAsync();
        var room = await svc.CreateRoomAsync(listingId, new CreateRoomRequest("Bedroom", _fixture.RoomTypeId, null, null));

        var linked = (await svc.LinkFeatureAsync(listingId, room.Id, _fixture.FeatureId)).ToList();
        Assert.Single(linked);

        // linking the same feature again should not duplicate
        var linkedAgain = (await svc.LinkFeatureAsync(listingId, room.Id, _fixture.FeatureId)).ToList();
        Assert.Single(linkedAgain);

        var unlinked = (await svc.UnlinkFeatureAsync(listingId, room.Id, _fixture.FeatureId)).ToList();
        Assert.Empty(unlinked);
    }

    [Fact]
    public async Task AddAndDeleteCustomFeature()
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingId = await CreateListingAsync();
        var room = await svc.CreateRoomAsync(listingId, new CreateRoomRequest("Bedroom", _fixture.RoomTypeId, null, null));

        var added = await svc.AddCustomFeatureAsync(listingId, room.Id, new AddCustomFeatureRequest("Walk-in closet"));
        Assert.Equal("Walk-in closet", added.Description);

        var customFeatures = (await svc.GetRoomCustomFeaturesAsync(listingId, room.Id)).ToList();
        Assert.Single(customFeatures);

        await svc.DeleteCustomFeatureAsync(listingId, room.Id, added.Id);

        customFeatures = (await svc.GetRoomCustomFeaturesAsync(listingId, room.Id)).ToList();
        Assert.Empty(customFeatures);
    }

    [Fact]
    public async Task DeleteRoom_CascadesChildren()
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingId = await CreateListingAsync();
        var room = await svc.CreateRoomAsync(listingId, new CreateRoomRequest("Bedroom", _fixture.RoomTypeId, null, null));
        await svc.UpsertConditionAsync(listingId, room.Id, new UpsertRoomConditionRequest(4, "Good", _fixture.ConditionCategoryId));
        await svc.LinkFeatureAsync(listingId, room.Id, _fixture.FeatureId);

        await svc.DeleteRoomAsync(listingId, room.Id);

        var rooms = (await svc.GetRoomsAsync(listingId)).ToList();
        Assert.Empty(rooms);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoomOperations_RejectRoomFromAnotherListing(bool mutate)
    {
        await _fixture.ResetAsync();
        var svc = Services.Rooms(_fixture);
        var listingA = await CreateListingAsync();
        var listingB = await CreateListingAsync();

        var roomInB = await svc.CreateRoomAsync(listingB, new CreateRoomRequest("B's room", _fixture.RoomTypeId, null, null));

        Func<Task> action = () =>
        {
            if (mutate)
                return svc.UpdateRoomAsync(listingA, roomInB.Id, new UpdateRoomRequest("hacked", _fixture.RoomTypeId, null, null));
            return svc.LinkFeatureAsync(listingA, roomInB.Id, _fixture.FeatureId);
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(action);
    }
}