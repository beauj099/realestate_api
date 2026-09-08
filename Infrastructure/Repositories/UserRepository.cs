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
}
