using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

/// <summary>
/// Documents attached to a listing's Expenses section (utility bills etc.).
/// Same ownership checks as <see cref="ListingPhotoService"/>; files go through
/// the shared <see cref="IImageStorage"/> under "listings/{listingId}/documents/".
/// </summary>
public class ListingDocumentService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingDocumentRepository _documentRepo;
    private readonly IImageStorage _storage;

    public ListingDocumentService(
        ListingRepository listingRepo,
        ListingDocumentRepository documentRepo,
        IImageStorage storage)
    {
        _listingRepo = listingRepo;
        _documentRepo = documentRepo;
        _storage = storage;
    }

    public async Task<IEnumerable<ListingDocumentDto>> GetDocumentsAsync(int listingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        var documents = await _documentRepo.GetByListingIdAsync(listingId, cancellationToken);
        return documents.Select(ToDto);
    }

    /// <param name="category">Already validated and lowercased.</param>
    /// <param name="fileName">Sanitised display name; never used to build the storage key.</param>
    /// <param name="contentType">A key of <see cref="ListingDocumentRules.AllowedContentTypes"/>.</param>
    public async Task<ListingDocumentDto> UploadAsync(
        int listingId, int? userId, bool isAdmin,
        Stream fileStream, string category, string fileName, string contentType, long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        // The key is server-generated; the client file name is display-only.
        var extension = ListingDocumentRules.AllowedContentTypes[contentType];
        var key = $"listings/{listingId}/documents/{Guid.NewGuid()}{extension}";
        var url = await _storage.UploadAsync(fileStream, key, contentType, cancellationToken);

        ListingDocument created;
        try
        {
            created = await _documentRepo.CreateAsync(new ListingDocument
            {
                ListingId = listingId,
                Category = category,
                FileName = fileName,
                StorageKey = key,
                Url = url,
                ContentType = contentType,
                SizeBytes = sizeBytes
            }, cancellationToken);
        }
        catch
        {
            // No row, so nothing would ever reference or clean up the object.
            try { await _storage.DeleteAsync(key, CancellationToken.None); } catch { /* best effort */ }
            throw;
        }

        return ToDto(created);
    }

    public async Task DeleteAsync(int listingId, int documentId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var document = await _documentRepo.GetByIdAsync(documentId, cancellationToken);
        if (document is null || document.ListingId != listingId)
            throw new KeyNotFoundException($"Document {documentId} not found under listing {listingId}");

        // Same order as photos: storage object first, then the row. The prefix check
        // means a bad row can never make this delete another listing's files.
        if (document.StorageKey.StartsWith($"listings/{listingId}/documents/", StringComparison.Ordinal))
            await _storage.DeleteAsync(document.StorageKey, cancellationToken);
        await _documentRepo.DeleteAsync(documentId, cancellationToken);
    }

    private static ListingDocumentDto ToDto(ListingDocument d) =>
        new(d.Id, d.Category, d.FileName, d.Url, d.ContentType, d.SizeBytes, d.CreatedAt);
}

/// <summary>Upload rules for listing documents, kept free of I/O so they can be unit-tested.</summary>
public static class ListingDocumentRules
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 200;

    public static readonly string[] Categories = ["water", "electricity", "municipal", "levies", "other"];

    /// <summary>Allowed content type to the extension used in the storage key.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/heic"] = ".heic"
        };

    private static readonly Dictionary<string, string> ContentTypeByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".heic"] = "image/heic"
        };

    /// <summary>Lowercased category, or null if it is not one of <see cref="Categories"/>.</summary>
    public static string? NormalizeCategory(string? category)
    {
        var normalized = category?.Trim().ToLowerInvariant();
        return normalized is not null && Categories.Contains(normalized) ? normalized : null;
    }

    /// <summary>
    /// The canonical allowed content type, or null if not allowed. Multipart clients
    /// (e.g. Dart's http package) often send "application/octet-stream" or nothing; in
    /// that case only, the type is inferred from the file extension. An explicit,
    /// disallowed type (e.g. "text/plain") is always rejected.
    /// </summary>
    public static string? ResolveContentType(string? declaredContentType, string? fileName)
    {
        var declared = declaredContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (declared == "image/jpg") declared = "image/jpeg";

        if (!string.IsNullOrEmpty(declared) && AllowedContentTypes.ContainsKey(declared))
            return declared;

        if (string.IsNullOrEmpty(declared) || declared == "application/octet-stream")
        {
            var ext = Path.GetExtension(SanitizeFileName(fileName) ?? string.Empty);
            return ContentTypeByExtension.TryGetValue(ext, out var inferred) ? inferred : null;
        }

        return null;
    }

    /// <summary>
    /// Display name only: strips any client-supplied path (either separator style) and
    /// control characters, trims, and caps at <see cref="MaxFileNameLength"/> characters,
    /// keeping the extension when it has to cut. Null if nothing usable is left.
    /// </summary>
    public static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0 || name is "." or "..")
            return null;

        if (name.Length > MaxFileNameLength)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length is 0 or > 10) ext = string.Empty;
            name = name[..(MaxFileNameLength - ext.Length)] + ext;
        }

        return name;
    }
}
