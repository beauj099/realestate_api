namespace RealEstateApi.IntegrationTests;

[Collection("Database")]
public class LookupTests
{
    private readonly DatabaseFixture _fixture;

    public LookupTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lookups_ReturnSeededData()
    {
        await _fixture.ResetAsync();
        var svc = Services.Lookups(_fixture);

        var propertyTypes = (await svc.GetPropertyTypesAsync()).ToList();
        Assert.Contains(propertyTypes, p => p.Name == "House");

        var roomTypes = (await svc.GetRoomTypesAsync()).ToList();
        Assert.Contains(roomTypes, r => r.Description == "Bedroom");

        var features = (await svc.GetFeaturesAsync()).ToList();
        Assert.Contains(features, f => f.Description == "Built-in cupboards");

        var conditionCategories = (await svc.GetConditionCategoriesAsync()).ToList();
        Assert.Contains(conditionCategories, c => c.Description == "Excellent");

        var parkingTypes = (await svc.GetParkingTypesAsync()).ToList();
        Assert.Contains(parkingTypes, p => p.Description == "Garage");

        var facing = (await svc.GetFacingAsync()).ToList();
        Assert.Contains(facing, f => f.Description == "North");

        var zoning = (await svc.GetZoningAsync()).ToList();
        Assert.Contains(zoning, z => z.Description == "Residential");
    }
}