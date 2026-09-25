using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class AgencyService
{
    /// <summary>Prefix of every agent-added agency's slug (the app relies on it).</summary>
    public const string CustomSlugPrefix = "custom-";

    private readonly AgencyRepository _repo;
    private readonly IImageStorage _imageStorage;
    private readonly IOptions<R2Options> _r2Options;
    private readonly ILogger<AgencyService> _logger;

    public AgencyService(AgencyRepository repo, IImageStorage imageStorage, IOptions<R2Options> r2Options, ILogger<AgencyService> logger)
    {
        _repo = repo;
        _imageStorage = imageStorage;
        _r2Options = r2Options;
        _logger = logger;
    }

    public async Task<IEnumerable<AgencyDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var agencies = await _repo.GetActiveAsync(cancellationToken);
        return agencies.Select(ToDto);
    }

    /// <summary>
    /// Adds an agency an agent could not find in the list. A name that already
    /// exists (any case) returns that agency instead of a duplicate; a logo sent
    /// with it is only attached to an agent-added agency that has none yet.
    /// </summary>
    public async Task<(AgencyDto Agency, bool Created)> AddCustomAsync(
        string name, int? userId, LogoUpload? logo, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        var existing = await _repo.FindActiveByNameAsync(trimmed, cancellationToken);
        if (existing is not null)
        {
            if (logo is not null && existing.IsCustom && existing.LogoUrl is null)
            {
                existing.LogoUrl = await StoreLogoAsync(existing, logo, cancellationToken);
                await _repo.SetLogoUrlAsync(existing.Id, existing.LogoUrl, cancellationToken);
            }
            return (ToDto(existing), false);
        }

        var slug = await UniqueSlugAsync(trimmed, cancellationToken);
        var created = await _repo.CreateCustomAsync(slug, trimmed, MonogramFor(trimmed), userId, cancellationToken);
        if (logo is not null)
        {
            created.LogoUrl = await StoreLogoAsync(created, logo, cancellationToken);
            await _repo.SetLogoUrlAsync(created.Id, created.LogoUrl, cancellationToken);
        }
        return (ToDto(created), true);
    }

    /// <summary>Admin restyle: name, colours, order and visibility.</summary>
    public async Task<AgencyDto> UpdateAsync(int id, UpdateAgencyRequest request, CancellationToken cancellationToken = default)
    {
        _ = await _repo.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Agency {id} not found");

        var name = request.Name.Trim();
        var monogram = string.IsNullOrWhiteSpace(request.Monogram)
            ? MonogramFor(name)
            : request.Monogram.Trim().ToUpperInvariant();
        await _repo.UpdateAsync(id, name, monogram, NormalizeColor(request.PrimaryColor), NormalizeColor(request.SecondaryColor),
            NormalizeColor(request.OnPrimaryColor), NormalizeColor(request.BannerColor), request.SortOrder, request.IsActive,
            cancellationToken);
        return ToDto((await _repo.GetByIdAsync(id, cancellationToken))!);
    }

    /// <summary>
    /// Replaces an agency's logo. Admins may change any agency; an agent only an
    /// agency they added themselves (others read as not found).
    /// </summary>
    public async Task<AgencyDto> SetLogoAsync(int id, int? userId, bool isAdmin, LogoUpload logo, CancellationToken cancellationToken = default)
    {
        var agency = await _repo.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Agency {id} not found");
        var ownsIt = agency.IsCustom && userId is not null && agency.CreatedByUserId == userId;
        if (!isAdmin && !ownsIt)
            throw new KeyNotFoundException($"Agency {id} not found");

        var oldUrl = agency.LogoUrl;
        agency.LogoUrl = await StoreLogoAsync(agency, logo, cancellationToken);
        await _repo.SetLogoUrlAsync(agency.Id, agency.LogoUrl, cancellationToken);
        await TryDeleteOldLogoAsync(oldUrl, cancellationToken);
        return ToDto(agency);
    }

    public static bool IsValidColor(string? color) =>
        string.IsNullOrWhiteSpace(color) || Regex.IsMatch(color.Trim(), "^#[0-9A-Fa-f]{6}$");

    private static string? NormalizeColor(string? color) =>
        string.IsNullOrWhiteSpace(color) ? null : color.Trim().ToUpperInvariant();

    // A fresh key per upload, so no cache ever serves the previous logo.
    private async Task<string> StoreLogoAsync(Agency agency, LogoUpload logo, CancellationToken cancellationToken)
    {
        var key = $"agencies/{agency.Slug}-{Guid.NewGuid().ToString("N")[..8]}{logo.Extension}";
        return await _imageStorage.UploadAsync(logo.Stream, key, logo.ContentType, cancellationToken);
    }

    // Best effort: only our own agency logos are removed, and a failure just
    // leaves an orphaned object behind.
    private async Task TryDeleteOldLogoAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(url)) return;
        const string localPrefix = "/uploads/";
        var r2Prefix = (_r2Options.Value.PublicUrl ?? string.Empty).TrimEnd('/') + "/";
        string? key = null;
        if (url.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase)) key = url[localPrefix.Length..];
        else if (r2Prefix.Length > 1 && url.StartsWith(r2Prefix, StringComparison.Ordinal)) key = url[r2Prefix.Length..];
        if (key is null || !key.StartsWith("agencies/", StringComparison.Ordinal)) return;
        try
        {
            await _imageStorage.DeleteAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete old agency logo {Key}", key);
        }
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = CustomSlugPrefix + Slugify(name);
        var slug = baseSlug;
        for (var n = 2; await _repo.SlugExistsAsync(slug, cancellationToken); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }

    /// <summary>"Engel &amp; Völkers" -> "engel-volkers".</summary>
    public static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        var slug = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "agency" : slug;
    }

    /// <summary>Up to two initials, e.g. "Bay Realty" -> "BR".</summary>
    public static string MonogramFor(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "?";
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    private static AgencyDto ToDto(Agency a) => new(
        a.Id, a.Slug, a.Name, a.Monogram, a.PrimaryColor, a.SecondaryColor, a.OnPrimaryColor, a.BannerColor,
        a.LogoUrl, a.SortOrder, a.IsCustom);
}

/// <summary>A validated logo file on its way to storage.</summary>
public record LogoUpload(Stream Stream, string Extension, string ContentType);
