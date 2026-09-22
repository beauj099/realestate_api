using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ListingDocumentRepository
{
    private const string Columns = "Id, ListingId, Category, FileName, StorageKey, Url, ContentType, SizeBytes, CreatedAt";

    private readonly DbConnectionFactory _connectionFactory;

    public ListingDocumentRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<ListingDocument>> GetByListingIdAsync(int listingId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingDocuments WHERE ListingId = @ListingId ORDER BY CreatedAt ASC, Id ASC",
            new { ListingId = listingId }, cancellationToken: cancellationToken);
        return await connection.QueryAsync<ListingDocument>(command);
    }

    public async Task<ListingDocument?> GetByIdAsync(int documentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {Columns} FROM ListingDocuments WHERE Id = @Id",
            new { Id = documentId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ListingDocument>(command);
    }

    public async Task<ListingDocument> CreateAsync(ListingDocument document, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "INSERT INTO ListingDocuments (ListingId, Category, FileName, StorageKey, Url, ContentType, SizeBytes, CreatedAt) " +
            $"OUTPUT INSERTED.{Columns.Replace(", ", ", INSERTED.")} " +
            "VALUES (@ListingId, @Category, @FileName, @StorageKey, @Url, @ContentType, @SizeBytes, GETUTCDATE())",
            new
            {
                document.ListingId,
                document.Category,
                document.FileName,
                document.StorageKey,
                document.Url,
                document.ContentType,
                document.SizeBytes
            },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<ListingDocument>(command);
    }

    public async Task DeleteAsync(int documentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM ListingDocuments WHERE Id = @Id", new { Id = documentId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
