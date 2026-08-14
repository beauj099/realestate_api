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

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Username, PasswordHash, DisplayName, Role, IsActive, CreatedAt FROM Users WHERE Username = @Username AND IsActive = 1",
            new { Username = username }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<User>(command);
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, Username, PasswordHash, DisplayName, Role, IsActive, CreatedAt FROM Users WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<User>(command);
    }
}
