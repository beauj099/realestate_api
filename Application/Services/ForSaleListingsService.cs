using PropertyData.Listings;

namespace RealEstateApi.Application.Services;

public record ForSaleSuburbDto(int Id, string Name, string Town, string Url);

public record ForSaleListingDto(
    string ListingNumber, string Url, decimal? PriceZar, string Title, string? Suburb, string? Address,
    string? Excerpt, int? Bedrooms, double? Bathrooms, int? Parking, double? FloorM2, double? ErfM2,
    string? ImageUrl, string? ListedOn);

/// <summary>
/// Homes for sale near the property, from Property24, credited to Property24 and linked to it.
/// <see cref="Suburbs"/> are the Property24 suburbs searched (the agent may pick another).
/// </summary>
public record ForSaleDto(string Source, string Attribution, IReadOnlyList<ForSaleSuburbDto> Suburbs,
    IReadOnlyList<ForSaleListingDto> Listings);

/// <summary>Picks the listings on Property24 most like the subject property.</summary>
public class ForSaleListingsService(Property24Client p24, ILogger<ForSaleListingsService> log)
{
    public const int MaxListings = 6;
    private const string Attribution =
        "Listings from Property24.com, as advertised there. Follow the links for each listing on Property24.";

    public async Task<ForSaleDto> FindAsync(string municipality, string suburb, string? township, int? suburbId,
        int? bedrooms, double? floorM2, double? erfM2, int max, CancellationToken ct)
    {
        max = Math.Clamp(max, 1, MaxListings);
        var all = await p24.SuburbsAsync(ct);
        var suburbs = suburbId is { } id
            ? all.Where(s => s.Id == id).ToList()
            : Property24Client.Match(all, suburb, township, ProvinceOf(municipality)).Take(3).ToList();

        var listings = new List<P24Listing>();
        foreach (var s in suburbs)
        {
            try
            {
                listings.AddRange(await p24.ListingsAsync(s, ct));
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning(ex, "Property24 suburb {Suburb} unavailable", s.Url);
            }
        }

        // One home is often advertised by more than one agency: same price, size and text.
        var best = Rank(listings.DistinctBy(l => l.ListingNumber), bedrooms, floorM2, erfM2)
            .DistinctBy(l => (l.PriceZar, l.Bedrooms, l.ErfM2, l.Excerpt ?? l.Address ?? l.ListingNumber))
            .Take(max)
            .ToList();
        var detailed = new List<P24Listing>();
        foreach (var l in best) detailed.Add(await p24.WithDetailsAsync(l, ct));

        return new ForSaleDto("Property24", Attribution,
            suburbs.Select(s => new ForSaleSuburbDto(s.Id, s.Name, s.Town, s.Url)).ToList(),
            detailed.Select(l => new ForSaleListingDto(l.ListingNumber, l.Url, l.PriceZar, l.Title, l.Suburb, l.Address,
                l.Excerpt, l.Bedrooms, l.Bathrooms, l.Parking, l.FloorM2, l.ErfM2, l.ImageUrl,
                l.ListedOn?.ToString("yyyy-MM-dd"))).ToList());
    }

    /// <summary>
    /// Most alike first: bedrooms within one, then closest in size (floor, else erf), priced
    /// listings before "POA".
    /// </summary>
    public static IEnumerable<P24Listing> Rank(IEnumerable<P24Listing> listings, int? bedrooms, double? floorM2, double? erfM2)
    {
        double SizeGap(P24Listing l) =>
            floorM2 is > 0 && l.FloorM2 is > 0 ? Math.Abs(Math.Log(l.FloorM2.Value / floorM2.Value))
            : erfM2 is > 0 && l.ErfM2 is > 0 ? Math.Abs(Math.Log(l.ErfM2.Value / erfM2.Value))
            : 1;
        double BedGap(P24Listing l) => bedrooms is null || l.Bedrooms is null ? 1 : Math.Abs(l.Bedrooms.Value - bedrooms.Value);
        return listings
            .OrderBy(l => l.PriceZar is null)
            .ThenBy(l => BedGap(l) + SizeGap(l) * 3);
    }

    private static string? ProvinceOf(string municipality) => municipality switch
    {
        "coct" or "drakenstein" or "mosselbay" => "western-cape",
        "coj" or "tshwane" => "gauteng",
        _ => null,
    };
}
