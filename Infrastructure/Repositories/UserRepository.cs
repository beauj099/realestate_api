using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class UserRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public UserRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectColumns = "Id, Username, PasswordHash, DisplayName, Role, IsActive, CreatedAt, FullName, Email, Mobile, AgencyName, AgencyRegistrationNumber, LicenceNumber";

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM Users WHERE (Username = @Username OR Email = @Username) AND IsActive = 1",
            new { Username = username }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<User>(command);
    }

    /// <summary>
    /// An account deleted within the grace period (<see cref="Application.Services.AccountDeletionService.GraceDays"/>),
    /// by username or email: signing in restores it. Null for anything else, including accounts
    /// switched off by an admin (no deletion date) or already anonymised.
    /// </summary>
    public async Task<User?> GetPendingDeletionAsync(string username, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM Users WHERE (Username = @Username OR Email = @Username) AND IsActive = 0 " +
            "AND DeletionRequestedAt IS NOT NULL AND DeletionRequestedAt >= DATEADD(day, -@Days, SYSUTCDATETIME())",
            new { Username = username, Days = Application.Services.AccountDeletionService.GraceDays },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<User>(command);
    }

    /// <summary>Ends a deletion's grace period: the account is active again, as it was.</summary>
    public async Task RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Users SET IsActive = 1, DeletionRequestedAt = NULL WHERE Id = @Id AND DeletionRequestedAt IS NOT NULL",
            new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM Users WHERE Email = @Email AND IsActive = 1",
            new { Email = email }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<User>(command);
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM Users WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<User>(command);
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            @"INSERT INTO Users (Username, PasswordHash, DisplayName, Role, IsActive, CreatedAt, FullName, Email, Mobile, AgencyName, AgencyRegistrationNumber, LicenceNumber)
              OUTPUT INSERTED.Id, INSERTED.Username, INSERTED.PasswordHash, INSERTED.DisplayName, INSERTED.Role, INSERTED.IsActive, INSERTED.CreatedAt, INSERTED.FullName, INSERTED.Email, INSERTED.Mobile, INSERTED.AgencyName, INSERTED.AgencyRegistrationNumber, INSERTED.LicenceNumber
              VALUES (@Username, @PasswordHash, @DisplayName, @Role, @IsActive, @CreatedAt, @FullName, @Email, @Mobile, @AgencyName, @AgencyRegistrationNumber, @LicenceNumber)",
            new
            {
                user.Username,
                user.PasswordHash,
                user.DisplayName,
                user.Role,
                user.IsActive,
                user.CreatedAt,
                user.FullName,
                user.Email,
                user.Mobile,
                user.AgencyName,
                user.AgencyRegistrationNumber,
                user.LicenceNumber
            },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<User>(command);
    }

    public async Task<User?> UpdateProfileAsync(int id, string displayName, string email, string mobile, string? agencyName, string? agencyRegistrationNumber, string? licenceNumber, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $@"UPDATE Users SET DisplayName = @DisplayName, Email = @Email, Mobile = @Mobile,
                AgencyName = @AgencyName, AgencyRegistrationNumber = @AgencyRegistrationNumber, LicenceNumber = @LicenceNumber
              OUTPUT INSERTED.{SelectColumns.Replace(", ", ", INSERTED.")}
              WHERE Id = @Id",
            new { Id = id, DisplayName = displayName, Email = email, Mobile = mobile, AgencyName = agencyName, AgencyRegistrationNumber = agencyRegistrationNumber, LicenceNumber = licenceNumber },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<User>(command);
    }

    public async Task UpdatePasswordHashAsync(int id, string passwordHash, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @Id",
            new { Id = id, PasswordHash = passwordHash }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
