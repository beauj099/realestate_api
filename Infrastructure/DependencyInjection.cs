using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddSingleton(new DbConnectionFactory(connectionString!));

        services.Configure<R2Options>(configuration.GetSection(R2Options.SectionName));
        services.AddSingleton<R2ImageService>();

        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<LookupRepository>();
        services.AddScoped<ListingRepository>();
        services.AddScoped<ListingAddressRepository>();
        services.AddScoped<ListingBuildingInfoRepository>();
        services.AddScoped<ListingValuationRepository>();
        services.AddScoped<PropertyRunningCostsRepository>();
        services.AddScoped<ListingRoomRepository>();
        services.AddScoped<ListingParkingRepository>();
        services.AddScoped<ContactRepository>();
        services.AddScoped<ListingPhotoRepository>();
        services.AddScoped<ListingOutdoorFeatureRepository>();
        services.AddScoped<UserRepository>();
        services.AddScoped<RefreshTokenRepository>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<LookupService>();
        services.AddScoped<ListingService>();
        services.AddScoped<ListingRoomService>();
        services.AddScoped<ListingParkingService>();
        services.AddScoped<ListingContactService>();
        services.AddScoped<ListingPhotoService>();
        services.AddScoped<ListingOutdoorFeatureService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AgentProfileService>();

        return services;
    }
}