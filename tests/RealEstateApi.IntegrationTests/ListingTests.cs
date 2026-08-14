using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;

namespace RealEstateApi.IntegrationTests;

[Collection("Database")]
public class ListingTests
{
    private readonly DatabaseFixture _fixture;

    public ListingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_GeneratesSequentialReferenceNumbers()
    {
        await _fixture.ResetAsync();
        var repo = Services.ListingRepo(_fixture);

        var first = await repo.CreateAsync(_fixture.PropertyTypeId, null);
        var second = await repo.CreateAsync(_fixture.PropertyTypeId, null);

        var year = DateTime.UtcNow.ToString("yyyy");
        Assert.Matches($"^LST-{year}-\\d{{5}}$", first.ReferenceNumber);
        Assert.Equal($"LST-{year}-00001", first.ReferenceNumber);
        Assert.Equal($"LST-{year}-00002", second.ReferenceNumber);
        Assert.NotEqual(first.ReferenceNumber, second.ReferenceNumber);
        Assert.Equal(ListingStatus.Incomplete, first.Status);
    }

    [Fact]
    public async Task Create_ConcurrentCreatesProduceUniqueReferenceNumbers()
    {
        await _fixture.ResetAsync();
        var repo = Services.ListingRepo(_fixture);

        var listings = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => repo.CreateAsync(_fixture.PropertyTypeId, null)));

        Assert.Equal(10, listings.Length);
        Assert.Equal(10, listings.Select(l => l.ReferenceNumber).Distinct().Count());
    }

    [Fact]
    public async Task GetAll_FiltersByStatus()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);

        var first = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));
        var second = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));
        await service.SubmitAsync(second.Id);

        var submitted = (await service.GetAllAsync(ListingStatus.Submitted, null, null)).ToList();
        var incomplete = (await service.GetAllAsync(ListingStatus.Incomplete, null, null)).ToList();

        Assert.Contains(submitted, l => l.Id == second.Id);
        Assert.DoesNotContain(submitted, l => l.Id == first.Id);
        Assert.Contains(incomplete, l => l.Id == first.Id);
    }

    [Fact]
    public async Task Update_PartialUpdatePreservesUnchangedFields()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);

        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, "P24-ORIG"));
        var updated = await service.UpdateAsync(created.Id, new UpdateListingRequest("Active", null, null));

        Assert.NotNull(updated);
        Assert.Equal("Active", updated.Status);
        Assert.Equal("P24-ORIG", updated.P24Ref);
        Assert.Equal(created.ReferenceNumber, updated.ReferenceNumber);
    }

    [Fact]
    public async Task Submit_TransitionsOnlyFromIncomplete()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);

        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));

        var submitted = await service.SubmitAsync(created.Id);
        Assert.NotNull(submitted);
        Assert.Equal(ListingStatus.Submitted, submitted.Status);
        Assert.NotNull(submitted.ListDate);

        var resubmit = await service.SubmitAsync(created.Id);
        Assert.Null(resubmit);
    }

    [Fact]
    public async Task Delete_RemovesListingAndChildren()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);
        var rooms = Services.Rooms(_fixture);
        var parking = Services.Parking(_fixture);
        var contacts = Services.Contacts(_fixture);

        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));
        await service.UpsertAddressAsync(created.Id, new UpsertAddressRequest(null, null, null, null, "Main Rd", "Centurion", "Pretoria", "Gauteng", "ZA", "0157", null, null));
        var room = await rooms.CreateRoomAsync(created.Id, new CreateRoomRequest("Main bedroom", _fixture.RoomTypeId, null, null));
        await parking.AddParkingAsync(created.Id, new AddParkingRequest(_fixture.ParkingTypeId, 2));
        await contacts.AddContactAsync(created.Id, new AddContactRequest("John Doe", null, null, null, "0821234567", "john@example.com", "Seller"));

        await service.DeleteAsync(created.Id);

        Assert.Null(await service.GetByIdAsync(created.Id));
        Assert.Empty(await Services.RoomRepo(_fixture).GetByListingIdAsync(created.Id));
        Assert.Empty(await Services.ParkingRepo(_fixture).GetByListingIdAsync(created.Id));
        Assert.Empty(await Services.ContactRepo(_fixture).GetByListingIdAsync(created.Id));
    }

    [Fact]
    public async Task FullResponse_PopulatesAllSections()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);
        var rooms = Services.Rooms(_fixture);
        var parking = Services.Parking(_fixture);
        var contacts = Services.Contacts(_fixture);
        var outdoor = Services.OutdoorFeatures(_fixture);

        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, "P24-1"));

        await service.UpsertAddressAsync(created.Id, new UpsertAddressRequest(
            "1234", null, "5", null, "Main Rd", "Centurion", "Pretoria", "Gauteng", "South Africa", "0157", -25.8587m, 28.1870m));
        await service.UpsertBuildingInfoAsync(created.Id, new UpsertBuildingInfoRequest(650.00m, 320.00m, 2005, _fixture.FacingId, _fixture.ZoningId));
        await service.UpsertValuationAsync(created.Id, new UpsertValuationRequest(2500000m, 2600000m, 5.5m));
        await service.UpsertRunningCostsAsync(created.Id, new UpsertRunningCostsRequest(1200.00m, 850.00m, 600.00m, 300.00m));

        var room = await rooms.CreateRoomAsync(created.Id, new CreateRoomRequest("Main bedroom", _fixture.RoomTypeId, null, null));
        await rooms.UpsertConditionAsync(created.Id, room.Id, new UpsertRoomConditionRequest(5, "Great condition", _fixture.ConditionCategoryId));
        await rooms.LinkFeatureAsync(created.Id, room.Id, _fixture.FeatureId);
        await rooms.AddCustomFeatureAsync(created.Id, room.Id, new AddCustomFeatureRequest("Walk-in closet"));

        await parking.AddParkingAsync(created.Id, new AddParkingRequest(_fixture.ParkingTypeId, 2));
        await contacts.AddContactAsync(created.Id, new AddContactRequest("John Doe", null, null, null, "0821234567", "john@example.com", "Seller"));
        await outdoor.AddAsync(created.Id, new AddOutdoorFeatureRequest("Garden"));

        var full = await service.GetByIdAsync(created.Id);

        Assert.NotNull(full);
        Assert.Equal("0157", full.Address?.PostalCode);
        Assert.Equal(2005, full.BuildingInfo?.ConstructionYear);
        Assert.Equal(2500000m, full.Valuation?.OwnersNetPrice);
        Assert.Equal(1200.00m, full.RunningCosts?.MonthlyLevy);

        var roomDto = Assert.Single(full.Rooms);
        Assert.NotNull(roomDto.Condition);
        Assert.Single(roomDto.Features);
        Assert.Single(roomDto.CustomFeatures);

        var parkingDto = Assert.Single(full.Parking);
        Assert.Equal("Garage", parkingDto.ParkingTypeDescription);

        Assert.Single(full.Contacts);
        Assert.Single(full.OutdoorFeatures);
    }

    [Fact]
    public async Task Upsert_AddressBuildingInfoRunningCosts_UpdateInPlace()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);

        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));

        var first = await service.UpsertAddressAsync(created.Id, new UpsertAddressRequest(
            "100", null, null, null, "First St", null, "Pretoria", null, null, null, null, null));
        var second = await service.UpsertAddressAsync(created.Id, new UpsertAddressRequest(
            "200", null, null, null, "Second St", null, "Pretoria", null, null, null, null, null));

        Assert.Equal(first.ListingAddressId, second.ListingAddressId);
        Assert.Equal("Second St", second.Street);

        var buildingFirst = await service.UpsertBuildingInfoAsync(created.Id, new UpsertBuildingInfoRequest(500m, null, null, null, null));
        var buildingSecond = await service.UpsertBuildingInfoAsync(created.Id, new UpsertBuildingInfoRequest(650m, null, null, null, null));
        Assert.Equal(buildingFirst.Id, buildingSecond.Id);
        Assert.Equal(650m, buildingSecond.ErfSize);

        var costsFirst = await service.UpsertRunningCostsAsync(created.Id, new UpsertRunningCostsRequest(100m, 200m, null, null));
        var costsSecond = await service.UpsertRunningCostsAsync(created.Id, new UpsertRunningCostsRequest(150m, null, null, null));
        Assert.Equal(costsFirst.Id, costsSecond.Id);
        Assert.Equal(150m, costsSecond.MonthlyLevy);
        Assert.Null(costsSecond.MonthlyRates);
    }

    [Fact]
    public async Task Upsert_Valuation_CreatesThenUpdatesInPlace()
    {
        await _fixture.ResetAsync();
        var service = Services.Listings(_fixture);

        var created = await service.CreateAsync(new CreateListingRequest(_fixture.PropertyTypeId, null));

        var first = await service.UpsertValuationAsync(created.Id, new UpsertValuationRequest(1000000m, null, null));
        var second = await service.UpsertValuationAsync(created.Id, new UpsertValuationRequest(1200000m, 1300000m, 5m));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1200000m, second.OwnersNetPrice);
        Assert.Equal(1300000m, second.AgentValuation);

        var full = await service.GetByIdAsync(created.Id);
        Assert.NotNull(full);
        Assert.Equal(second.Id, full.Valuation?.Id);
    }
}