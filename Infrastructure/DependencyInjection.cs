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
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.AddSingleton<R2ImageService>();
        services.AddSingleton<LocalFileImageService>();

        // Temporary arrangement: local disk until the R2 bucket exists.
        // Flip "Storage:Provider" to "R2" (appsettings / env STORAGE__PROVIDER)
        // once the credentials are in place -- no other code changes needed.
        services.AddSingleton<IImageStorage>(provider =>
        {
            var configured = configuration.GetValue<string>("Storage:Provider");
            if (configured is not null && configured.Equals("R2", StringComparison.OrdinalIgnoreCase))
                return provider.GetRequiredService<R2ImageService>();
            return provider.GetRequiredService<LocalFileImageService>();
        });

        // Email: real SMTP when Smtp:Host is set, otherwise log the message so a
        // dev without SMTP can read e.g. password reset codes from the console.
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        if (!string.IsNullOrWhiteSpace(configuration.GetValue<string>("Smtp:Host")))
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<LookupRepository>();
        services.AddScoped<AgencyRepository>();
        services.AddScoped<ListingRepository>();
        services.AddScoped<ListingAddressRepository>();
        services.AddScoped<ListingBuildingInfoRepository>();
        services.AddScoped<ListingValuationRepository>();
        services.AddScoped<PropertyRunningCostsRepository>();
        services.AddScoped<ListingRoomRepository>();
        services.AddScoped<ListingRoomPhotoRepository>();
        services.AddScoped<ListingParkingRepository>();
        services.AddScoped<ContactRepository>();
        services.AddScoped<ListingPhotoRepository>();
        services.AddScoped<ListingDocumentRepository>();
        services.AddScoped<ListingOutdoorFeatureRepository>();
        services.AddScoped<UserRepository>();
        services.AddScoped<RefreshTokenRepository>();
        services.AddScoped<PasswordResetCodeRepository>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<LookupService>();
        services.AddScoped<AgencyService>();
        services.AddScoped<ListingService>();
        services.AddScoped<ListingRoomService>();
        services.AddScoped<ListingParkingService>();
        services.AddScoped<ListingContactService>();
        services.AddScoped<ListingPhotoService>();
        services.AddScoped<ListingDocumentService>();
        services.AddScoped<ListingOutdoorFeatureService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AgentProfileService>();

        return services;
    }
}