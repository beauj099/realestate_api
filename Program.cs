 using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.IdentityModel.Tokens;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure;
using RealEstateApi.Infrastructure.Middleware;
using RealEstateApi.Mappings;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Machine-local overrides for secrets (R2 keys, connection strings). The file
// is git-ignored; on a server use environment variables (e.g. R2__SecretAccessKey).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

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
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRepositories();

// AutoMapper
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());

// Application Services
builder.Services.AddApplicationServices();

// Auth
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();

// Fail fast if the JWT signing secret is missing, too weak, or left as the committed
// placeholder. appsettings.json is a template only -- the real secret must come from
// outside source control, e.g. `dotnet user-secrets set "Jwt:Secret" "<64+ random chars>"`
// or a JWT__SECRET environment variable. Without this check the API would happily sign
// every token with a value that is public in the repository.
if (jwtOptions is null || string.IsNullOrWhiteSpace(jwtOptions.Secret))
{
    throw new InvalidOperationException(
        "Jwt:Secret is not configured. Set it via user-secrets or the JWT__SECRET " +
        "environment variable; it must not be stored in appsettings.json.");
}

// TODO: re-enable once the server sets JWT__SECRET. Disabled because the shared
// appsettings still use the CHANGE-ME placeholder.
/*if (jwtOptions.Secret.StartsWith("CHANGE-ME", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Jwt:Secret is still the placeholder value. Replace it with a real secret " +
        "supplied outside source control.");
}*/

// HMAC-SHA256 requires a key of at least 256 bits.
if (Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is too short. It must be at least 32 bytes (256 bits) for HMAC-SHA256.");
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
// Serves the temporary local photo storage at /uploads/... (see
// LocalFileImageService). Not needed once Storage:Provider is "R2".
// .heic (listing documents) is not in the default content-type map, so it would 404.
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".heic"] = "image/heic";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = staticContentTypes });
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
