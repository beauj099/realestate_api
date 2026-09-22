using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class PasswordResetCodeRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public PasswordResetCodeRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Expires every still-usable code for the user, so only the newest one works.
    /// Invalidated codes keep UsedAt NULL; UsedAt is reserved for a successful reset.
    /// </summary>
    public async Task InvalidateActiveForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE PasswordResetCodes SET ExpiresAt = GETUTCDATE() WHERE UserId = @UserId AND UsedAt IS NULL AND ExpiresAt > GETUTCDATE()",
            new { UserId = userId }, cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
    }

    public async Task CreateAsync(PasswordResetCode code, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO PasswordResetCodes (UserId, CodeHash, ExpiresAt, Attempts, CreatedAt) VALUES (@UserId, @CodeHash, @ExpiresAt, 0, @CreatedAt)",
            new { code.UserId, code.CodeHash, code.ExpiresAt, code.CreatedAt },
            cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
    }

    /// <summary>The newest code for the user that is unused, unexpired and under the attempt limit.</summary>
    public async Task<PasswordResetCode?> GetActiveForUserAsync(int userId, int maxAttempts, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            @"SELECT TOP 1 Id, UserId, CodeHash, ExpiresAt, Attempts, UsedAt, CreatedAt
              FROM PasswordResetCodes
              WHERE UserId = @UserId AND UsedAt IS NULL AND ExpiresAt > GETUTCDATE() AND Attempts < @MaxAttempts
              ORDER BY CreatedAt DESC, Id DESC",
            new { UserId = userId, MaxAttempts = maxAttempts }, cancellationToken: cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<PasswordResetCode>(command);
    }

    /// <summary>
    /// Records a wrong guess. Once the attempt count reaches <paramref name="maxAttempts"/>
    /// the code is also expired so it can never be used again.
    /// </summary>
    public async Task RegisterFailedAttemptAsync(int id, int maxAttempts, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            @"UPDATE PasswordResetCodes
              SET Attempts = Attempts + 1,
                  ExpiresAt = CASE WHEN Attempts + 1 >= @MaxAttempts THEN GETUTCDATE() ELSE ExpiresAt END
              WHERE Id = @Id AND UsedAt IS NULL",
            new { Id = id, MaxAttempts = maxAttempts }, cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
    }

    /// <summary>
    /// Consumes the code. Returns false if it was used, expired or locked out in the
    /// meantime (concurrent requests), in which case the reset must not proceed.
    /// </summary>
    public async Task<bool> MarkUsedAsync(int id, int maxAttempts, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            @"UPDATE PasswordResetCodes SET UsedAt = GETUTCDATE()
              WHERE Id = @Id AND UsedAt IS NULL AND ExpiresAt > GETUTCDATE() AND Attempts < @MaxAttempts",
            new { Id = id, MaxAttempts = maxAttempts }, cancellationToken: cancellationToken);
        return await conn.ExecuteAsync(command) == 1;
    }
}
