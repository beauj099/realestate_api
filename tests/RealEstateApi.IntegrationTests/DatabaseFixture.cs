using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string DefaultMasterConnection = "Server=.\\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string DefaultTestConnection = "Server=.\\SQLEXPRESS;Database=PropertyListingsDB_Test;Trusted_Connection=True;TrustServerCertificate=True;";

    private string? _schemaSql;

    public string MasterConnectionString { get; private set; } = DefaultMasterConnection;
    public string ConnectionString { get; private set; } = DefaultTestConnection;
    public DbConnectionFactory Factory => new(ConnectionString);
    public R2ImageService R2ImageService { get; private set; } = null!;

    public int PropertyTypeId { get; private set; }
    public int RoomTypeId { get; private set; }
    public int ConditionCategoryId { get; private set; }
    public int ParkingTypeId { get; private set; }
    public int FeatureId { get; private set; }
    public int FacingId { get; private set; }
    public int ZoningId { get; private set; }

    public async Task InitializeAsync()
    {
        var master = Environment.GetEnvironmentVariable("REALESTATE_TEST_MASTER_CONNECTION");
        var test = Environment.GetEnvironmentVariable("REALESTATE_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(master)) MasterConnectionString = master;
        if (!string.IsNullOrWhiteSpace(test)) ConnectionString = test;

        R2ImageService = new R2ImageService(Options.Create(new R2Options
        {
            BucketName = "test-bucket",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            Endpoint = "https://test.invalid",
            PublicUrl = "https://test.invalid"
        }));

        if (Environment.GetEnvironmentVariable("REALESTATE_TEST_RECREATE") == "1")
        {
            var dbName = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
            await using (var masterConn = new SqlConnection(MasterConnectionString))
            {
                await masterConn.ExecuteAsync(
                    $"IF DB_ID(N'{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END");
            }
        }

        await CreateDatabaseIfMissingAsync();
        await CreateSchemaIfMissingAsync();
        await ResetAsync();
    }

    public async Task DisposeAsync()
    {
        R2ImageService.Dispose();
    }

    public async Task ResetAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.ExecuteAsync("EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT all'");
        await conn.ExecuteAsync("EXEC sp_MSforeachtable 'DELETE FROM ?'");
        await conn.ExecuteAsync("EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all'");
        await SeedAsync(conn);
    }

    private async Task CreateDatabaseIfMissingAsync()
    {
        var dbName = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        await using var masterConn = new SqlConnection(MasterConnectionString);
        await masterConn.ExecuteAsync($"IF DB_ID(N'{dbName}') IS NULL BEGIN CREATE DATABASE [{dbName}]; END");
    }

    private async Task CreateSchemaIfMissingAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        var hasSchema = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Users'");
        if (hasSchema > 0) return;

        await conn.ExecuteAsync(LoadSchemaSql());
    }

    private string LoadSchemaSql()
    {
        if (_schemaSql is not null) return _schemaSql;

        var assembly = typeof(DatabaseFixture).Assembly;
        using var stream = assembly.GetManifestResourceStream("RealEstateApi.IntegrationTests.Schema.sql")
            ?? throw new InvalidOperationException("Embedded Schema.sql resource not found.");
        using var reader = new StreamReader(stream);
        _schemaSql = reader.ReadToEnd();
        return _schemaSql;
    }

    private async Task SeedAsync(SqlConnection conn)
    {
        PropertyTypeId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO PropertyType (Name, SortOrder, IsActive) VALUES (@Name, @SortOrder, @IsActive); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { Name = "House", SortOrder = 1, IsActive = true });
        await conn.ExecuteAsync(
            "INSERT INTO PropertyType (Name, SortOrder, IsActive) VALUES (@Name, @SortOrder, @IsActive)",
            new { Name = "Apartment", SortOrder = 2, IsActive = true });

        RoomTypeId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO RoomTypes (Description) VALUES (@Description); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { Description = "Bedroom" });
        await conn.ExecuteAsync("INSERT INTO RoomTypes (Description) VALUES ('Bathroom'), ('Kitchen')");

        await conn.ExecuteAsync(
            "INSERT INTO Feature (Id, Category, Description) VALUES (1, 'Interior', 'Built-in cupboards'), (2, 'Interior', 'Tiled floors'), (3, 'Exterior', 'Balcony')");
        FeatureId = 1;

        ConditionCategoryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO ConditionCategory (Description) VALUES (@Description); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { Description = "Excellent" });
        await conn.ExecuteAsync("INSERT INTO ConditionCategory (Description) VALUES ('Good'), ('Fair'), ('Poor')");

        ParkingTypeId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO ParkingType (Description) VALUES (@Description); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { Description = "Garage" });
        await conn.ExecuteAsync("INSERT INTO ParkingType (Description) VALUES ('Carport'), ('Open parking')");

        await conn.ExecuteAsync("INSERT INTO Facing (Id, Description) VALUES (1, 'North'), (2, 'South')");
        FacingId = 1;

        await conn.ExecuteAsync("INSERT INTO Zoning (Id, Description) VALUES (1, 'Residential'), (2, 'Commercial')");
        ZoningId = 1;

        var passwordHash = BCrypt.Net.BCrypt.HashPassword("admin123");
        await conn.ExecuteAsync(
            "INSERT INTO Users (Username, PasswordHash, DisplayName, Role, IsActive, CreatedAt) VALUES " +
            "('admin', @AdminHash, 'Admin User', 'Admin', 1, SYSUTCDATETIME()), " +
            "('agent1', @AgentHash, 'Alice Agent', 'Agent', 1, SYSUTCDATETIME())",
            new { AdminHash = passwordHash, AgentHash = passwordHash });
    }
}
