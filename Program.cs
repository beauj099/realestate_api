using System.Text;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RealEstateApi.Application.Interfaces;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;
using RealEstateApi.Mappings;
using RealEstateApi.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Consistent error responses: map exceptions (e.g. KeyNotFoundException -> 404) to ProblemDetails.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Infrastructure
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddSingleton(new DbConnectionFactory(connectionString!));

builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<IListingRepository, ListingRepository>();
builder.Services.AddScoped<IListingAddressRepository, ListingAddressRepository>();
builder.Services.AddScoped<IListingBuildingInfoRepository, ListingBuildingInfoRepository>();
builder.Services.AddScoped<IListingValuationRepository, ListingValuationRepository>();
builder.Services.AddScoped<IPropertyRunningCostsRepository, PropertyRunningCostsRepository>();
builder.Services.AddScoped<IListingRoomRepository, ListingRoomRepository>();
builder.Services.AddScoped<IListingParkingRepository, ListingParkingRepository>();
builder.Services.AddScoped<IContactRepository, ContactRepository>();

// Infrastructure Services
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.AddSingleton<IImageService, R2ImageService>();

// AutoMapper
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());

// Application Services
builder.Services.AddScoped<IRoomAssembler, RoomAssembler>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<IListingService, ListingService>();
builder.Services.AddScoped<IListingRoomService, ListingRoomService>();
builder.Services.AddScoped<IListingParkingService, ListingParkingService>();
builder.Services.AddScoped<IListingContactService, ListingContactService>();

// Auth
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();

// Fail fast if the JWT signing secret is missing, too weak, or left as the placeholder.
// The real secret must be supplied outside source control (user-secrets / environment
// variable / key vault), e.g. `dotnet user-secrets set "Jwt:Secret" "<64+ random chars>"`
// or the `Jwt__Secret` environment variable.
const string jwtPlaceholder = "CHANGE-ME-to-a-secret-key-at-least-32-characters-long";
if (jwtOptions is null ||
    string.IsNullOrWhiteSpace(jwtOptions.Secret) ||
    jwtOptions.Secret == jwtPlaceholder ||
    Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is not configured with a secure value. Set a random secret of at least " +
        "32 bytes via user-secrets or the 'Jwt__Secret' environment variable; it must never be committed to source control.");
}

var signingKey = Encoding.UTF8.GetBytes(jwtOptions.Secret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey)
        };
    });

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
