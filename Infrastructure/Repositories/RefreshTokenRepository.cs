using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class RefreshTokenRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public RefreshTokenRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, UserId, TokenHash, ExpiresAt, CreatedAt, IsRevoked FROM RefreshTokens WHERE TokenHash = @TokenHash",
            new { TokenHash = tokenHash }, cancellationToken: cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<RefreshToken>(command);
    }

    public async Task RevokeUserTokensAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE RefreshTokens SET IsRevoked = 1 WHERE UserId = @UserId AND IsRevoked = 0",
            new { UserId = userId }, cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
    }

    public async Task CreateAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO RefreshTokens (UserId, TokenHash, ExpiresAt, CreatedAt, IsRevoked) VALUES (@UserId, @TokenHash, @ExpiresAt, @CreatedAt, @IsRevoked)",
            new { refreshToken.UserId, refreshToken.TokenHash, refreshToken.ExpiresAt, refreshToken.CreatedAt, refreshToken.IsRevoked },
            cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
    }

    public async Task RevokeAsync(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE RefreshTokens SET IsRevoked = 1 WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
    }
}