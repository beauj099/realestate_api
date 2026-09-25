using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ContactRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ContactRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string Columns = "Id, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, OwnerType, ListingId";

    public async Task<IEnumerable<Contact>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} " +
            // By Id, not name: the first contact added is the primary owner.
            "FROM Contact WHERE ListingId = @ListingId ORDER BY Id",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<Contact>(command);
    }

    public async Task<Contact?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} " +
            "FROM Contact WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Contact>(command);
    }

    public async Task<Contact> CreateAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"INSERT INTO Contact (ListingId, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, OwnerType) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "VALUES (@ListingId, @FullName, @IdNumber, @CompanyName, @CompanyRegistrationNumber, @MobilePhone, @EmailAddress, @Role, @OwnerType)",
            contact, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Contact>(command);
    }

    public async Task<Contact?> UpdateAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE Contact SET FullName = COALESCE(@FullName, FullName), IdNumber = COALESCE(@IdNumber, IdNumber), " +
            "CompanyName = COALESCE(@CompanyName, CompanyName), CompanyRegistrationNumber = COALESCE(@CompanyRegistrationNumber, CompanyRegistrationNumber), " +
            "MobilePhone = COALESCE(@MobilePhone, MobilePhone), EmailAddress = COALESCE(@EmailAddress, EmailAddress), Role = COALESCE(@Role, Role), " +
            "OwnerType = COALESCE(@OwnerType, OwnerType) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "WHERE Id = @Id",
            contact, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Contact>(command);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM Contact WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}