using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;
using RealEstateApi.Mappings;

namespace RealEstateApi.IntegrationTests;

internal static class Services
{
    public static readonly IMapper Mapper = CreateMapper();

    private static IMapper CreateMapper()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
        using var provider = serviceCollection.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    public static AuthService Auth(DatabaseFixture fixture) => new(
        new UserRepository(fixture.Factory),
        new RefreshTokenRepository(fixture.Factory),
        Options.Create(new JwtOptions
        {
            Secret = "integration-test-secret-key-at-least-32-characters",
            Issuer = "RealEstateApi",
            Audience = "RealEstateApi",
            ExpiryHours = 1,
            RefreshTokenExpiryDays = 60
        }));

    public static LookupService Lookups(DatabaseFixture fixture) => new(
        new LookupRepository(fixture.Factory),
        Mapper);

    public static ListingRepository ListingRepo(DatabaseFixture fixture) => new(fixture.Factory);

    public static ListingRoomRepository RoomRepo(DatabaseFixture fixture) => new(fixture.Factory);

    public static ListingParkingRepository ParkingRepo(DatabaseFixture fixture) => new(fixture.Factory);

    public static ContactRepository ContactRepo(DatabaseFixture fixture) => new(fixture.Factory);

    public static ListingOutdoorFeatureRepository OutdoorFeatureRepo(DatabaseFixture fixture) => new(fixture.Factory);

    public static ListingService Listings(DatabaseFixture fixture) => new(
        new ListingRepository(fixture.Factory),
        new ListingAddressRepository(fixture.Factory),
        new ListingBuildingInfoRepository(fixture.Factory),
        new ListingValuationRepository(fixture.Factory),
        new PropertyRunningCostsRepository(fixture.Factory),
        new ListingRoomRepository(fixture.Factory),
        new ListingParkingRepository(fixture.Factory),
        new ContactRepository(fixture.Factory),
        new ListingOutdoorFeatureRepository(fixture.Factory),
        Mapper);

    public static ListingRoomService Rooms(DatabaseFixture fixture) => new(
        new ListingRepository(fixture.Factory),
        new ListingRoomRepository(fixture.Factory),
        fixture.R2ImageService,
        Options.Create(new R2Options
        {
            BucketName = "test-bucket",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            Endpoint = "https://test.invalid",
            PublicUrl = "https://test.invalid"
        }),
        Mapper);

    public static ListingParkingService Parking(DatabaseFixture fixture) => new(
        new ListingRepository(fixture.Factory),
        new ListingParkingRepository(fixture.Factory),
        Mapper);

    public static ListingContactService Contacts(DatabaseFixture fixture) => new(
        new ListingRepository(fixture.Factory),
        new ContactRepository(fixture.Factory),
        Mapper);

    public static ListingOutdoorFeatureService OutdoorFeatures(DatabaseFixture fixture) => new(
        new ListingRepository(fixture.Factory),
        new ListingOutdoorFeatureRepository(fixture.Factory),
        Mapper);
}
