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

    public async Task<IEnumerable<Contact>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, ListingId " +
            "FROM Contact WHERE ListingId = @ListingId ORDER BY FullName",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<Contact>(command);
    }

    public async Task<Contact?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Id, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, ListingId " +
            "FROM Contact WHERE Id = @Id",
            new { Id = id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Contact>(command);
    }

    public async Task<Contact> CreateAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO Contact (ListingId, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role) " +
            "OUTPUT INSERTED.Id, INSERTED.FullName, INSERTED.IdNumber, INSERTED.CompanyName, INSERTED.CompanyRegistrationNumber, INSERTED.MobilePhone, INSERTED.EmailAddress, INSERTED.Role, INSERTED.ListingId " +
            "VALUES (@ListingId, @FullName, @IdNumber, @CompanyName, @CompanyRegistrationNumber, @MobilePhone, @EmailAddress, @Role)",
            contact, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Contact>(command);
    }

    public async Task<Contact?> UpdateAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "UPDATE Contact SET FullName = COALESCE(@FullName, FullName), IdNumber = COALESCE(@IdNumber, IdNumber), " +
            "CompanyName = COALESCE(@CompanyName, CompanyName), CompanyRegistrationNumber = COALESCE(@CompanyRegistrationNumber, CompanyRegistrationNumber), " +
            "MobilePhone = COALESCE(@MobilePhone, MobilePhone), EmailAddress = COALESCE(@EmailAddress, EmailAddress), Role = COALESCE(@Role, Role) " +
            "OUTPUT INSERTED.Id, INSERTED.FullName, INSERTED.IdNumber, INSERTED.CompanyName, INSERTED.CompanyRegistrationNumber, INSERTED.MobilePhone, INSERTED.EmailAddress, INSERTED.Role, INSERTED.ListingId " +
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