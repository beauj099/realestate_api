using System.Text.Json;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

/// <summary>An image on its way to storage (profile photo, signature, brochure page).</summary>
public record ImageUpload(Stream Stream, string Extension, string ContentType);

public class AgentProfileService
{
    /// <summary>Brochure pages an agent may attach to their report pack.</summary>
    public const int MaxBrochurePages = 12;

    private readonly UserRepository _userRepository;
    private readonly AgentProfileRepository _profiles;
    private readonly IImageStorage _images;
    private readonly IOptions<R2Options> _r2Options;
    private readonly ILogger<AgentProfileService> _logger;

    public AgentProfileService(UserRepository userRepository, AgentProfileRepository profiles, IImageStorage images,
        IOptions<R2Options> r2Options, ILogger<AgentProfileService> logger)
    {
        _userRepository = userRepository;
        _profiles = profiles;
        _images = images;
        _r2Options = r2Options;
        _logger = logger;
    }

    public async Task<AgentProfileDto?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return null;
        return ToDto(user, await _profiles.GetAsync(userId, cancellationToken));
    }

    public async Task<AgentProfileDto?> UpdateAsync(int userId, UpdateAgentProfileRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null && existing.Id != userId)
            throw new InvalidOperationException("Email address is already registered.");

        var updated = await _userRepository.UpdateProfileAsync(
            userId,
            request.DisplayName.Trim(),
            normalizedEmail,
            request.Mobile.Trim(),
            request.AgencyName?.Trim(),
            request.AgencyRegistrationNumber?.Trim(),
            request.LicenceNumber?.Trim(),
            cancellationToken);
        if (updated is null) return null;

        // Report-pack fields: null leaves a field alone, "" clears it.
        var p = await _profiles.GetAsync(userId, cancellationToken) ?? new AgentProfile { UserId = userId };
        p.AgencySlug = Keep(request.AgencySlug, p.AgencySlug);
        p.PpraNumber = Keep(request.PpraNumber, p.PpraNumber);
        p.JobTitle = Keep(request.JobTitle, p.JobTitle);
        p.Bio = Keep(request.Bio, p.Bio);
        p.Website = Keep(request.Website, p.Website);
        if (request.Qualifications is { } q)
        {
            var lines = q.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            p.Qualifications = lines.Count == 0 ? null : string.Join('\n', lines);
        }
        if (request.Office is { } o)
        {
            p.OfficeName = Keep(o.Name, p.OfficeName);
            p.OfficeAddress = Keep(o.Address, p.OfficeAddress);
            p.OfficePhone = Keep(o.Phone, p.OfficePhone);
            p.OfficeEmail = Keep(o.Email, p.OfficeEmail);
            p.OfficeWebsite = Keep(o.Website, p.OfficeWebsite);
            p.OfficeFooter = Keep(o.Footer, p.OfficeFooter);
            p.OfficeSlogan = Keep(o.Slogan, p.OfficeSlogan);
            p.OfficeHeadline = Keep(o.Headline, p.OfficeHeadline);
        }
        await _profiles.SaveAsync(p, cancellationToken);
        return ToDto(updated, p);
    }

    public Task<AgentProfileDto?> SetPhotoAsync(int userId, ImageUpload image, CancellationToken ct) =>
        ReplaceImageAsync(userId, image, "photo", p => p.PhotoUrl, (p, url) => p.PhotoUrl = url, ct);

    /// <summary>One of the office's logo variants (<see cref="OfficeLogosDto.Kinds"/>).</summary>
    public Task<AgentProfileDto?> SetOfficeLogoAsync(int userId, string kind, ImageUpload image, CancellationToken ct) =>
        ReplaceImageAsync(userId, image, "logo-" + kind,
            p => OfficeLogosDto.Read(p.OfficeLogosJson)?.Get(kind),
            (p, url) => p.OfficeLogosJson = OfficeLogosDto.With(p.OfficeLogosJson, kind, url), ct);

    /// <summary>Removes one of the office's logos, so the agency's is used again.</summary>
    public async Task<AgentProfileDto?> RemoveOfficeLogoAsync(int userId, string kind, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null) return null;
        var p = await _profiles.GetAsync(userId, ct) ?? new AgentProfile { UserId = userId };
        var old = OfficeLogosDto.Read(p.OfficeLogosJson)?.Get(kind);
        p.OfficeLogosJson = OfficeLogosDto.With(p.OfficeLogosJson, kind, null);
        await _profiles.SaveAsync(p, ct);
        await TryDeleteAsync(old, ct);
        return ToDto(user, p);
    }

    public Task<AgentProfileDto?> SetSignatureAsync(int userId, ImageUpload image, CancellationToken ct) =>
        ReplaceImageAsync(userId, image, "signature", p => p.SignatureUrl, (p, url) => p.SignatureUrl = url, ct);

    /// <summary>Adds brochure pages after the agent's existing ones (their own set, not the agency's).</summary>
    public async Task<AgentProfileDto?> AddBrochurePagesAsync(int userId, IReadOnlyList<ImageUpload> pages, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null) return null;
        var p = await _profiles.GetAsync(userId, ct) ?? new AgentProfile { UserId = userId };
        var list = ReadPages(p.BrochurePagesJson)?.ToList() ?? [];
        if (list.Count + pages.Count > MaxBrochurePages)
            throw new ArgumentException($"A report pack takes up to {MaxBrochurePages} brochure pages.");
        foreach (var page in pages)
            list.Add(await _images.UploadAsync(page.Stream, Key(userId, "brochure", page.Extension), page.ContentType, ct));
        p.BrochurePagesJson = JsonSerializer.Serialize(list);
        await _profiles.SaveAsync(p, ct);
        return ToDto(user, p);
    }

    /// <summary>
    /// Replaces the agent's brochure pages with <paramref name="keep"/> (a reorder or removal of
    /// the current ones); null goes back to the agency's pages.
    /// </summary>
    public async Task<AgentProfileDto?> SetBrochurePagesAsync(int userId, IReadOnlyList<string>? keep, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null) return null;
        var p = await _profiles.GetAsync(userId, ct) ?? new AgentProfile { UserId = userId };
        var current = ReadPages(p.BrochurePagesJson) ?? [];
        if (keep is not null && keep.Any(url => !current.Contains(url)))
            throw new ArgumentException("Only the agent's own uploaded pages can be kept or reordered.");
        foreach (var gone in current.Where(url => keep is null || !keep.Contains(url)))
            await TryDeleteAsync(gone, ct);
        p.BrochurePagesJson = keep is null ? null : JsonSerializer.Serialize(keep);
        await _profiles.SaveAsync(p, ct);
        return ToDto(user, p);
    }

    public async Task<AgentProfileDto?> SetReportSettingsAsync(int userId, JsonElement settings, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null) return null;
        var p = await _profiles.GetAsync(userId, ct) ?? new AgentProfile { UserId = userId };
        p.ReportSettingsJson = settings.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : settings.GetRawText();
        await _profiles.SaveAsync(p, ct);
        return ToDto(user, p);
    }

    private async Task<AgentProfileDto?> ReplaceImageAsync(int userId, ImageUpload image, string kind,
        Func<AgentProfile, string?> get, Action<AgentProfile, string> set, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null) return null;
        var p = await _profiles.GetAsync(userId, ct) ?? new AgentProfile { UserId = userId };
        var old = get(p);
        set(p, await _images.UploadAsync(image.Stream, Key(userId, kind, image.Extension), image.ContentType, ct));
        await _profiles.SaveAsync(p, ct);
        await TryDeleteAsync(old, ct);
        return ToDto(user, p);
    }

    // A fresh key per upload, so no cache ever serves the previous image.
    private static string Key(int userId, string kind, string extension) =>
        $"agents/{userId}/{kind}-{Guid.NewGuid().ToString("N")[..10]}{extension}";

    // Best effort, and only our own agent images.
    private async Task TryDeleteAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url)) return;
        const string localPrefix = "/uploads/";
        var r2Prefix = (_r2Options.Value.PublicUrl ?? string.Empty).TrimEnd('/') + "/";
        string? key = null;
        if (url.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase)) key = url[localPrefix.Length..];
        else if (r2Prefix.Length > 1 && url.StartsWith(r2Prefix, StringComparison.Ordinal)) key = url[r2Prefix.Length..];
        if (key is null || !key.StartsWith("agents/", StringComparison.Ordinal)) return;
        try
        {
            await _images.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete old agent image {Key}", key);
        }
    }

    private static string? Keep(string? requested, string? current) =>
        requested is null ? current : requested.Trim().Length == 0 ? null : requested.Trim();

    public static IReadOnlyList<string>? ReadPages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentProfileDto ToDto(User user, AgentProfile? p) =>
        new(user.Id, user.DisplayName, user.Email, user.Mobile, user.AgencyName, user.AgencyRegistrationNumber,
            user.LicenceNumber, user.Role)
        {
            AgencySlug = p?.AgencySlug,
            PpraNumber = p?.PpraNumber,
            JobTitle = p?.JobTitle,
            Bio = p?.Bio,
            Qualifications = p?.Qualifications?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            Website = p?.Website,
            PhotoUrl = p?.PhotoUrl,
            SignatureUrl = p?.SignatureUrl,
            Office = new OfficeDto(p?.OfficeName, p?.OfficeAddress, p?.OfficePhone, p?.OfficeEmail, p?.OfficeWebsite, p?.OfficeFooter,
                p?.OfficeSlogan, p?.OfficeHeadline, OfficeLogosDto.Read(p?.OfficeLogosJson)),
            BrochurePages = ReadPages(p?.BrochurePagesJson),
            ReportSettings = string.IsNullOrWhiteSpace(p?.ReportSettingsJson)
                ? null
                : JsonDocument.Parse(p.ReportSettingsJson).RootElement.Clone(),
        };
}
