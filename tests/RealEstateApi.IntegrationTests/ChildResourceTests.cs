using RealEstateApi.Application.DTOs;

namespace RealEstateApi.IntegrationTests;

[Collection("Database")]
public class ChildResourceTests
{
    private readonly DatabaseFixture _fixture;

    public ChildResourceTests(DatabaseFixture fixture)
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
    public async Task Parking_AddGetUpdateDelete()
    {
        await _fixture.ResetAsync();
        var svc = Services.Parking(_fixture);
        var listingId = await CreateListingAsync();

        var added = await svc.AddParkingAsync(listingId, new AddParkingRequest(_fixture.ParkingTypeId, 2));
        Assert.Equal(2, added.Quantity);
        Assert.Equal("Garage", added.ParkingTypeDescription);

        var parking = (await svc.GetParkingAsync(listingId)).ToList();
        Assert.Single(parking);

        var updated = await svc.UpdateParkingAsync(listingId, added.Id, new UpdateParkingRequest(3));
        Assert.NotNull(updated);
        Assert.Equal(3, updated.Quantity);

        await svc.DeleteParkingAsync(listingId, added.Id);
        Assert.Empty(await svc.GetParkingAsync(listingId));
    }

    [Fact]
    public async Task Contacts_AddGetUpdateDelete()
    {
        await _fixture.ResetAsync();
        var svc = Services.Contacts(_fixture);
        var listingId = await CreateListingAsync();

        var added = await svc.AddContactAsync(listingId, new AddContactRequest(
            "John Doe", "8001011234087", null, null, "0821234567", "john@example.com", "Seller"));
        Assert.Equal("John Doe", added.FullName);

        var contacts = (await svc.GetContactsAsync(listingId)).ToList();
        Assert.Single(contacts);

        var updated = await svc.UpdateContactAsync(listingId, added.Id, new UpdateContactRequest(
            "Jane Doe", null, null, null, null, "jane@example.com", null));
        Assert.NotNull(updated);
        Assert.Equal("Jane Doe", updated.FullName);
        Assert.Equal("jane@example.com", updated.EmailAddress);
        Assert.Equal("8001011234087", updated.IdNumber);

        await svc.DeleteContactAsync(listingId, added.Id);
        Assert.Empty(await svc.GetContactsAsync(listingId));
    }

    [Fact]
    public async Task OutdoorFeatures_AddGetDeleteReplace()
    {
        await _fixture.ResetAsync();
        var svc = Services.OutdoorFeatures(_fixture);
        var listingId = await CreateListingAsync();

        var added = await svc.AddAsync(listingId, new AddOutdoorFeatureRequest("Garden"));
        Assert.Equal("Garden", added.Description);

        var features = (await svc.GetByListingIdAsync(listingId)).ToList();
        Assert.Single(features);

        await svc.DeleteAsync(listingId, added.Id);
        Assert.Empty(await svc.GetByListingIdAsync(listingId));

        var replaced = (await svc.ReplaceAllAsync(listingId, new ReplaceOutdoorFeaturesRequest(new List<string> { "Pool", "Garden" }))).ToList();
        Assert.Equal(2, replaced.Count);
        Assert.Contains(replaced, f => f.Description == "Pool");
        Assert.Contains(replaced, f => f.Description == "Garden");
    }

    [Fact]
    public async Task Parking_RejectsDeleteFromAnotherListing()
    {
        await _fixture.ResetAsync();
        var svc = Services.Parking(_fixture);
        var listingA = await CreateListingAsync();
        var listingB = await CreateListingAsync();

        var parkingInB = await svc.AddParkingAsync(listingB, new AddParkingRequest(_fixture.ParkingTypeId, 1));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteParkingAsync(listingA, parkingInB.Id));
    }

    [Fact]
    public async Task Contacts_RejectsUpdateFromAnotherListing()
    {
        await _fixture.ResetAsync();
        var svc = Services.Contacts(_fixture);
        var listingA = await CreateListingAsync();
        var listingB = await CreateListingAsync();

        var contactInB = await svc.AddContactAsync(listingB, new AddContactRequest("B's Contact", null, null, null, null, null, null));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.UpdateContactAsync(listingA, contactInB.Id, new UpdateContactRequest("hacked", null, null, null, null, null, null)));
    }

    [Fact]
    public async Task OutdoorFeatures_RejectsDeleteFromAnotherListing()
    {
        await _fixture.ResetAsync();
        var svc = Services.OutdoorFeatures(_fixture);
        var listingA = await CreateListingAsync();
        var listingB = await CreateListingAsync();

        var featureInB = await svc.AddAsync(listingB, new AddOutdoorFeatureRequest("Garden"));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteAsync(listingA, featureInB.Id));
    }
}