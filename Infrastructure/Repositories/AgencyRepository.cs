using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class AgencyRepository
{
    private const string Columns =
        "Id, Slug, Name, Monogram, PrimaryColor, SecondaryColor, OnPrimaryColor, BannerColor, LogoUrl, " +
        "SortOrder, IsCustom, IsActive, CreatedByUserId, CreatedAt, UpdatedAt";

    private readonly DbConnectionFactory _connectionFactory;

    public AgencyRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>Active agencies: listed brands in their order, then agent-added ones by name.</summary>
    public async Task<IEnumerable<Agency>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM Agencies WHERE IsActive = 1 ORDER BY IsCustom ASC, SortOrder ASC, Name ASC",
            cancellationToken: cancellationToken);
        return await connection.QueryAsync<Agency>(command);
    }

    public async Task<Agency?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM Agencies WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Agency>(command);
    }

    /// <summary>An active agency with this exact name, ignoring case.</summary>
    public async Task<Agency?> FindActiveByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT TOP 1 {Columns} FROM Agencies WHERE IsActive = 1 AND LOWER(Name) = LOWER(@Name) ORDER BY IsCustom ASC, Id ASC",
            new { Name = name }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Agency>(command);
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT COUNT(1) FROM Agencies WHERE Slug = @Slug",
            new { Slug = slug }, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<int>(command) > 0;
    }

    public async Task<Agency> CreateCustomAsync(string slug, string name, string monogram, int? createdByUserId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $@"INSERT INTO Agencies (Slug, Name, Monogram, IsCustom, IsActive, CreatedByUserId, CreatedAt)
               OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")}
               VALUES (@Slug, @Name, @Monogram, 1, 1, @CreatedByUserId, GETUTCDATE())",
            new { Slug = slug, Name = name, Monogram = monogram, CreatedByUserId = createdByUserId },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<Agency>(command);
    }

    public async Task UpdateAsync(int id, string name, string monogram, string? primaryColor, string? secondaryColor,
        string? onPrimaryColor, string? bannerColor, int sortOrder, bool isActive, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            @"UPDATE Agencies SET Name = @Name, Monogram = @Monogram, PrimaryColor = @PrimaryColor,
                  SecondaryColor = @SecondaryColor, OnPrimaryColor = @OnPrimaryColor, BannerColor = @BannerColor,
                  SortOrder = @SortOrder, IsActive = @IsActive, UpdatedAt = GETUTCDATE()
              WHERE Id = @Id",
            new
            {
                Id = id, Name = name, Monogram = monogram, PrimaryColor = primaryColor, SecondaryColor = secondaryColor,
                OnPrimaryColor = onPrimaryColor, BannerColor = bannerColor, SortOrder = sortOrder, IsActive = isActive
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task SetLogoUrlAsync(int id, string? logoUrl, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE Agencies SET LogoUrl = @LogoUrl, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = id, LogoUrl = logoUrl }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
