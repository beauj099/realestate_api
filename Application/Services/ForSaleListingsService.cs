using PropertyData.Listings;

namespace RealEstateApi.Application.Services;

public record ForSaleSuburbDto(int Id, string Name, string Town, string Url);

public record ForSaleListingDto(
    string ListingNumber, string Url, decimal? PriceZar, string Title, string? Suburb, string? Address,
    string? Excerpt, int? Bedrooms, double? Bathrooms, int? Parking, double? FloorM2, double? ErfM2,
    string? ImageUrl, string? ListedOn, double? DistanceM = null);

/// <summary>
/// Homes for sale near the property, from Property24, credited to Property24 and linked to it.
/// <see cref="Suburbs"/> are the property's own Property24 suburbs (the agent may pick another);
/// <see cref="AlsoSearched"/> are the surrounding suburbs searched as well, since suburb names
/// differ between the City and Property24. With the property's location, each listing carries its
/// distance and the nearest are kept.
/// </summary>
public record ForSaleDto(string Source, string Attribution, IReadOnlyList<ForSaleSuburbDto> Suburbs,
    IReadOnlyList<ForSaleListingDto> Listings, IReadOnlyList<ForSaleSuburbDto>? AlsoSearched = null);

/// <summary>Picks the listings on Property24 most like the subject property.</summary>
public class ForSaleListingsService(Property24Client p24, ILogger<ForSaleListingsService> log)
{
    public const int MaxListings = 6;
    private const string Attribution =
        "Listings from Property24.com, as advertised there. Follow the links for each listing on Property24.";

    /// <summary>Surrounding Property24 suburbs searched as well (most listings first).</summary>
    public const int MaxNeighbours = 3;

    /// <summary>Listings whose page is read for its location, before keeping the nearest.</summary>
    public const int MaxLocated = 12;

    /// <summary>A listing this close to the property is the property itself, advertised.</summary>
    public const double SamePropertyM = 25;

    /// <summary>How far a home may be; widened once when too few are that close.</summary>
    public static readonly double[] RadiusStepsM = [2500, 5000];

    public async Task<ForSaleDto> FindAsync(string municipality, string suburb, string? township, int? suburbId,
        int? bedrooms, double? floorM2, double? erfM2, int max, CancellationToken ct,
        double? lat = null, double? lng = null, decimal? priceZar = null)
    {
        max = Math.Clamp(max, 1, MaxListings);
        // A known Property24 suburb needs no suburb list: its id makes the page address.
        var suburbs = suburbId is { } id
            ? [Property24Client.ById(id, suburb, township)]
            : Property24Client.Match(await p24.SuburbsAsync(ct), suburb, township, ProvinceOf(municipality))
                // Houses are not sold in industrial areas ("Strand Industria").
                .Where(s => !s.Slug.Contains("industr", StringComparison.Ordinal))
                .Take(4).ToList();

        var listings = new List<P24Listing>();
        var neighbours = new List<P24Neighbour>();
        foreach (var s in suburbs)
        {
            try
            {
                var page = await p24.PageAsync(s, ct);
                listings.AddRange(page.Listings);
                neighbours.AddRange(page.Surrounding);
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning(ex, "Property24 suburb {Suburb} unavailable", s.Url);
            }
        }

        // Suburb names differ between sources, so with a location the surrounding suburbs are
        // always searched; without one, only when the property's own suburbs have too few.
        var located = lat is not null && lng is not null;
        var alsoSearched = new List<P24Suburb>();
        if (located || listings.Count < max)
        {
            var own = suburbs.Select(s => s.Id).ToHashSet();
            foreach (var n in neighbours.Where(n => !own.Contains(n.Id) && n.Count > 0)
                         .DistinctBy(n => n.Id).OrderByDescending(n => n.Count).Take(MaxNeighbours))
            {
                var near = Property24Client.ById(n.Id, n.Name, township);
                try
                {
                    listings.AddRange((await p24.PageAsync(near, ct)).Listings);
                    alsoSearched.Add(near);
                }
                catch (HttpRequestException ex)
                {
                    log.LogWarning(ex, "Property24 suburb {Suburb} unavailable", near.Url);
                }
            }
        }

        // One home is often advertised by more than one agency: same price, size and text.
        var alike = Rank(listings.DistinctBy(l => l.ListingNumber), bedrooms, floorM2, erfM2, priceZar)
            .DistinctBy(l => (l.PriceZar, l.Bedrooms, l.ErfM2, l.Excerpt ?? l.Address ?? l.ListingNumber))
            .ToList();

        List<(P24Listing Listing, double? DistanceM)> chosen;
        if (located)
        {
            // Read the most alike listings' pages for their map position, then keep the nearest.
            var here = new PropertyData.Core.Models.LatLng(lat!.Value, lng!.Value);
            var detailed = new List<(P24Listing Listing, double? DistanceM)>();
            foreach (var l in alike.Take(MaxLocated))
            {
                var d = await p24.WithDetailsAsync(l, ct);
                double? metres = d.Lat is { } la && d.Lng is { } lo
                    ? PropertyData.CapeTown.Internal.Geo.DistanceM(here, new PropertyData.Core.Models.LatLng(la, lo))
                    : null;
                // The property's own advert is not a home "like" it.
                if (metres is < SamePropertyM) continue;
                detailed.Add((d, metres));
            }
            chosen = NearestAlike(detailed, bedrooms, floorM2, erfM2, max, priceZar);
        }
        else
        {
            chosen = [];
            foreach (var l in alike.Take(max)) chosen.Add((await p24.WithDetailsAsync(l, ct), null));
        }

        ForSaleSuburbDto Dto(P24Suburb s) => new(s.Id, s.Name, s.Town, s.Url);
        return new ForSaleDto("Property24", Attribution,
            suburbs.Select(Dto).ToList(),
            chosen.Select(c => new ForSaleListingDto(c.Listing.ListingNumber, c.Listing.Url, c.Listing.PriceZar,
                c.Listing.Title, c.Listing.Suburb, c.Listing.Address, c.Listing.Excerpt, c.Listing.Bedrooms,
                c.Listing.Bathrooms, c.Listing.Parking, c.Listing.FloorM2, c.Listing.ErfM2, c.Listing.ImageUrl,
                c.Listing.ListedOn?.ToString("yyyy-MM-dd"),
                c.DistanceM is null ? null : Math.Round(c.DistanceM.Value))).ToList(),
            alsoSearched.Select(Dto).ToList());
    }

    /// <summary>
    /// Within 2.5 km (5 km when fewer than wanted are that close), most alike first with distance
    /// counted in: each kilometre weighs like half a bedroom. Listings without a map position
    /// only fill in when too few are placed.
    /// </summary>
    public static List<(P24Listing Listing, double? DistanceM)> NearestAlike(
        IReadOnlyList<(P24Listing Listing, double? DistanceM)> listings, int? bedrooms, double? floorM2,
        double? erfM2, int max, decimal? priceZar = null)
    {
        foreach (var radius in RadiusStepsM)
        {
            var near = listings.Where(l => l.DistanceM <= radius).ToList();
            if (near.Count >= max || (radius == RadiusStepsM[^1] && near.Count > 0))
                return near
                    .OrderBy(l => l.Listing.PriceZar is null)
                    .ThenBy(l => Score(l.Listing, bedrooms, floorM2, erfM2, priceZar) + l.DistanceM!.Value / 1000 * 0.5)
                    .Take(max).ToList();
        }
        // Nothing placed nearby: the most alike, placed ones first.
        return listings.OrderBy(l => l.DistanceM is null).Take(max).ToList();
    }

    /// <summary>
    /// Most alike first: bedrooms within one, then closest in size (floor, else erf), priced
    /// listings before "POA".
    /// </summary>
    public static IEnumerable<P24Listing> Rank(IEnumerable<P24Listing> listings, int? bedrooms, double? floorM2, double? erfM2,
        decimal? priceZar = null)
    {
        return listings
            .OrderBy(l => l.PriceZar is null)
            .ThenBy(l => Score(l, bedrooms, floorM2, erfM2, priceZar));
    }

    /// <summary>
    /// How unlike the subject: bedrooms apart, three times the size ratio's log, and, with a price
    /// to compare with (the report's value), four times the price ratio's log (so 25% dearer
    /// weighs about like one bedroom more).
    /// </summary>
    public static double Score(P24Listing l, int? bedrooms, double? floorM2, double? erfM2, decimal? priceZar = null)
    {
        var priceGap = priceZar is > 0 && l.PriceZar is > 0
            ? Math.Abs(Math.Log((double)(l.PriceZar.Value / priceZar.Value))) * 4
            : 0;
        var sizeGap = floorM2 is > 0 && l.FloorM2 is > 0 ? Math.Abs(Math.Log(l.FloorM2.Value / floorM2.Value))
            : erfM2 is > 0 && l.ErfM2 is > 0 ? Math.Abs(Math.Log(l.ErfM2.Value / erfM2.Value))
            : 1;
        var bedGap = bedrooms is null || l.Bedrooms is null ? 1 : Math.Abs(l.Bedrooms.Value - bedrooms.Value);
        return bedGap + sizeGap * 3 + priceGap;
    }

    private static string? ProvinceOf(string municipality) => municipality switch
    {
        "coct" or "drakenstein" or "mosselbay" => "western-cape",
        "coj" or "tshwane" => "gauteng",
        _ => null,
    };
}
