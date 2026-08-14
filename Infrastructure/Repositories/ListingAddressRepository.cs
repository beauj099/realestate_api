using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingAddressRepository
{
    private const string Columns = "ListingAddressId, ListingId, ErfNumber, EstateName, StreetNumber, UnitNumber, Street, Suburb, City, Province, Country, PostalCode, Latitude, Longitude";

    private readonly DbConnectionFactory _connectionFactory;

    public ListingAddressRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ListingAddress?> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingAddress WHERE ListingId = @ListingId",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingAddress>(command);
    }

    public async Task<ListingAddress> UpsertAsync(ListingAddress address, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "MERGE ListingAddress AS t " +
            "USING (SELECT @ListingId AS ListingId) AS s " +
            "ON t.ListingId = s.ListingId " +
            "WHEN MATCHED THEN UPDATE SET " +
            "ErfNumber = @ErfNumber, EstateName = @EstateName, StreetNumber = @StreetNumber, UnitNumber = @UnitNumber, " +
            "Street = @Street, Suburb = @Suburb, City = @City, Province = @Province, Country = @Country, " +
            "PostalCode = @PostalCode, Latitude = @Latitude, Longitude = @Longitude " +
            "WHEN NOT MATCHED THEN INSERT (ListingId, ErfNumber, EstateName, StreetNumber, UnitNumber, Street, Suburb, City, Province, Country, PostalCode, Latitude, Longitude) " +
            "VALUES (@ListingId, @ErfNumber, @EstateName, @StreetNumber, @UnitNumber, @Street, @Suburb, @City, @Province, @Country, @PostalCode, @Latitude, @Longitude) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")};",
            address, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingAddress>(command);
    }
}