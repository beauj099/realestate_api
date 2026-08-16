using System.Data;
using Dapper;

namespace RealEstateApi.Infrastructure.Data;

/// <summary>
/// Base class for Dapper repositories. Centralizes connection creation and the
/// <see cref="CommandType.StoredProcedure"/> convention so each repository only
/// declares the proc name and parameters. Also gives one place to later add
/// cross-cutting concerns (CancellationToken, logging, retry).
/// </summary>
public abstract class DapperRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    protected DapperRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ----- Stored-procedure helpers -----

    protected async Task<IEnumerable<T>> QueryProcAsync<T>(string proc, object? param = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<T>(proc, param, commandType: CommandType.StoredProcedure);
    }

    /// <summary>Executes a proc expected to return exactly one row (create/upsert). Throws otherwise.</summary>
    protected async Task<T> QuerySingleProcAsync<T>(string proc, object? param = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<T>(proc, param, commandType: CommandType.StoredProcedure);
    }

    /// <summary>Executes a proc that may return zero or one row; returns null when absent.</summary>
    protected async Task<T?> QuerySingleOrDefaultProcAsync<T>(string proc, object? param = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(proc, param, commandType: CommandType.StoredProcedure);
    }

    protected async Task ExecuteProcAsync(string proc, object? param = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(proc, param, commandType: CommandType.StoredProcedure);
    }

    /// <summary>
    /// Executes a proc that returns multiple result sets. The <paramref name="read"/> callback
    /// consumes the grid reader while the connection is still open.
    /// </summary>
    protected async Task<TResult> QueryMultipleProcAsync<TResult>(
        string proc, object? param, Func<SqlMapper.GridReader, Task<TResult>> read)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(proc, param, commandType: CommandType.StoredProcedure);
        return await read(multi);
    }

    // ----- Raw-SQL escape hatches (for the few queries that are not stored procedures) -----

    protected async Task<T?> QuerySingleOrDefaultSqlAsync<T>(string sql, object? param = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    protected async Task ExecuteSqlAsync(string sql, object? param = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, param);
    }
}
