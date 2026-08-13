using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;
using RealEstateApi.Mappings;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Infrastructure
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddSingleton(new DbConnectionFactory(connectionString!));

builder.Services.AddScoped<LookupRepository>();
builder.Services.AddScoped<ListingRepository>();
builder.Services.AddScoped<ListingAddressRepository>();
builder.Services.AddScoped<ListingBuildingInfoRepository>();
builder.Services.AddScoped<ListingValuationRepository>();
builder.Services.AddScoped<PropertyRunningCostsRepository>();
builder.Services.AddScoped<ListingRoomRepository>();
builder.Services.AddScoped<ListingParkingRepository>();
builder.Services.AddScoped<ContactRepository>();
builder.Services.AddScoped<ListingOutdoorFeatureRepository>();

// Infrastructure Services
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.AddSingleton<R2ImageService>();

// AutoMapper
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());

// Application Services
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<ListingService>();
builder.Services.AddScoped<ListingRoomService>();
builder.Services.AddScoped<ListingParkingService>();
builder.Services.AddScoped<ListingContactService>();
builder.Services.AddScoped<ListingOutdoorFeatureService>();

// Auth
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<RefreshTokenRepository>();
builder.Services.AddScoped<AuthService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
var signingKey = Encoding.UTF8.GetBytes(jwtOptions!.Secret);

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
