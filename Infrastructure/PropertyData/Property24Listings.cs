// ---------------------------------------------------------------------------------------------
// Property24 — homes currently for sale near a property, for the report's "on the market" page.
//
// Shown as Property24's listings, credited and linked back to Property24; never presented as our
// data. Within robots.txt (checked 2026-09-27): the suburb for-sale pages, listing pages and the
// sitemaps are allowed; the autocomplete, advanced search, image handler, recent-sales and
// contact endpoints are not, and none of them is used. On demand only (one suburb page, then the
// few listings shown), one request at a time with a pause, cached for six hours.
//
//   Suburbs  /sitemap/Suburbs?PropertyCategory=House&SearchType=ForSale lists every suburb as
//            /houses-for-sale/{suburb}/{town}/{province}/{id} (20 756 of them). The id is what
//            counts: the slugs are cosmetic (a wrong slug redirects to the id's own page).
//   Listings /houses-for-sale/{suburb}/{town}/{province}/{id}: ~20 tiles, each with the listing
//            number, link, price (itemprop content), title ("5 Bedroom House"), address, bedrooms,
//            bathrooms, parking, erf or floor size and the main photo.
//   Listing  /for-sale/…/{id}/{listingNumber}: "Listing Date 29 August 2026", "Floor Size 300 m²".
//
// Property24 splits suburbs more finely than the City (Strand North / Central / South, where the
// City has "Strand"), so a suburb matches by name within its town, or by prefix.
// The listing agent's name and photo are on the tiles too; they are not read.
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PropertyData.Listings
{
    public sealed record P24Suburb(string Slug, string TownSlug, string ProvinceSlug, int Id)
    {
        public string Url => $"{Property24Client.Root}/houses-for-sale/{Slug}/{TownSlug}/{ProvinceSlug}/{Id}";
        public string Name => Property24Client.Title(Slug);
        public string Town => Property24Client.Title(TownSlug);
    }

    public sealed record P24Listing(
        string ListingNumber,
        string Url,
        decimal? PriceZar,
        string Title,
        string? Suburb,
        string? Address,
        string? Excerpt,
        int? Bedrooms,
        double? Bathrooms,
        int? Parking,
        double? FloorM2,
        double? ErfM2,
        string? ImageUrl,
        DateOnly? ListedOn = null);

    public sealed class Property24Client(HttpClient http, IMemoryCache cache, ILogger<Property24Client> log)
    {
        public const string Root = "https://www.property24.com";
        private const string SuburbSitemap = Root + "/sitemap/Suburbs?PropertyCategory=House&SearchType=ForSale";

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTimeOffset _lastCall;
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(800);

        private static readonly Regex SuburbLoc = new(
            @"/houses-for-sale/(?<suburb>[a-z0-9-]+)/(?<town>[a-z0-9-]+)/(?<province>[a-z0-9-]+)/(?<id>\d+)",
            RegexOptions.Compiled);

        /// <summary>Where the suburb list is kept between restarts (it is 3.6 MB to fetch).</summary>
        public static string SuburbFile { get; set; } = Path.Combine(Path.GetTempPath(), "realworth-p24-suburbs.xml");

        /// <summary>
        /// Every suburb with houses for sale; refreshed weekly. Kept on disk too, so a restarted
        /// API does not fetch the sitemap again, and an out-of-date copy beats none when
        /// Property24 is busy.
        /// </summary>
        public async Task<IReadOnlyList<P24Suburb>> SuburbsAsync(CancellationToken ct = default) =>
            (await cache.GetOrCreateAsync("p24:suburbs", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
                var saved = new FileInfo(SuburbFile);
                if (saved.Exists && saved.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7))
                    return ParseSuburbs(await File.ReadAllTextAsync(saved.FullName, ct));
                try
                {
                    var sitemap = await GetAsync(SuburbSitemap, ct);
                    await File.WriteAllTextAsync(saved.FullName, sitemap, ct);
                    return ParseSuburbs(sitemap);
                }
                catch (HttpRequestException) when (saved.Exists)
                {
                    log.LogWarning("Property24 suburb list unavailable; using the copy from {Date}", saved.LastWriteTimeUtc);
                    return ParseSuburbs(await File.ReadAllTextAsync(saved.FullName, ct));
                }
            }))!;

        /// <summary>A suburb by its id alone: the page address only needs the id (the slugs are cosmetic).</summary>
        public static P24Suburb ById(int id, string? name = null, string? town = null) =>
            new(Slug(name ?? "suburb"), Slug(town ?? "town"), "province", id);

        public static IReadOnlyList<P24Suburb> ParseSuburbs(string sitemap) =>
            SuburbLoc.Matches(sitemap)
                .Select(m => new P24Suburb(m.Groups["suburb"].Value, m.Groups["town"].Value,
                    m.Groups["province"].Value, int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture)))
                .DistinctBy(s => s.Id)
                .ToList();

        /// <summary>
        /// Property24's suburbs for a report's suburb: the same name (in the same town and
        /// province when known), else the finer suburbs it covers ("Strand" → Strand North,
        /// Strand Central, Strand South).
        /// </summary>
        public static IReadOnlyList<P24Suburb> Match(IReadOnlyList<P24Suburb> all, string suburb, string? town, string? province)
        {
            var slug = Slug(suburb);
            var townSlug = town is null ? null : Slug(town.StartsWith("THE ", StringComparison.OrdinalIgnoreCase) ? town[4..] : town);
            IEnumerable<P24Suburb> scope = all;
            if (province is not null) scope = scope.Where(s => s.ProvinceSlug == province);

            var exact = scope.Where(s => s.Slug == slug).ToList();
            if (exact.Count > 1 && townSlug is not null && exact.Any(s => s.TownSlug == townSlug))
                exact = exact.Where(s => s.TownSlug == townSlug).ToList();
            if (exact.Count > 0) return exact;

            var parts = scope.Where(s => s.Slug.StartsWith(slug + "-", StringComparison.Ordinal)
                                         && (townSlug is null || s.TownSlug == townSlug || s.TownSlug == slug))
                             .OrderBy(s => s.Slug)
                             .ToList();
            return parts;
        }

        /// <summary>The first page of houses for sale in a suburb (about 20), cached six hours.</summary>
        public async Task<IReadOnlyList<P24Listing>> ListingsAsync(P24Suburb suburb, CancellationToken ct = default) =>
            (await cache.GetOrCreateAsync($"p24:listings:{suburb.Id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
                return ParseListings(await GetAsync(suburb.Url, ct));
            }))!;

        /// <summary>Fills in what only the listing's own page has: its date and sizes.</summary>
        public async Task<P24Listing> WithDetailsAsync(P24Listing listing, CancellationToken ct = default)
        {
            try
            {
                var detail = await cache.GetOrCreateAsync($"p24:detail:{listing.ListingNumber}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
                    return ParseDetails(await GetAsync(listing.Url, ct));
                });
                return listing with
                {
                    ListedOn = detail!.ListedOn ?? listing.ListedOn,
                    FloorM2 = detail.FloorM2 ?? listing.FloorM2,
                    ErfM2 = detail.ErfM2 ?? listing.ErfM2,
                };
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning(ex, "Property24 listing {Number} unavailable", listing.ListingNumber);
                return listing;
            }
        }

        public static IReadOnlyList<P24Listing> ParseListings(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var tiles = doc.DocumentNode.SelectNodes("//div[contains(@class,'p24_regularTile') and @data-listing-number]");
            if (tiles is null) return [];

            var result = new List<P24Listing>();
            foreach (var tile in tiles)
            {
                var number = tile.GetAttributeValue("data-listing-number", "");
                // The content link (not the branding link, which carries the agency).
                var link = tile.SelectSingleNode(".//a[contains(@class,'p24_content')]")
                           ?? tile.SelectSingleNode(".//span[contains(@class,'js_listingTileImageHolder')]//a");
                var href = WebUtility.HtmlDecode(link?.GetAttributeValue("href", "") ?? "");
                if (number.Length == 0 || href.Length == 0) continue;
                var path = href.Split('?')[0];

                string? Text(string cls) =>
                    tile.SelectSingleNode($".//*[contains(concat(' ', normalize-space(@class), ' '), ' {cls} ')]") is { } n
                        ? Clean(n.InnerText) is { Length: > 0 } t ? t : null
                        : null;
                string? Feature(string title) =>
                    tile.SelectSingleNode($".//span[contains(@class,'p24_featureDetails') and @title='{title}']/span") is { } n
                        ? Clean(n.InnerText)
                        : null;

                var price = tile.SelectSingleNode(".//span[@itemprop='price']")?.GetAttributeValue("content", null);
                result.Add(new P24Listing(
                    ListingNumber: number,
                    Url: Root + path,
                    PriceZar: decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : null,
                    Title: Text("p24_title") ?? "Property for sale",
                    Suburb: Text("p24_location"),
                    Address: Text("p24_address"),
                    Excerpt: Text("p24_excerpt"),
                    Bedrooms: IntOf(Feature("Bedrooms")),
                    Bathrooms: NumberOf(Feature("Bathrooms")),
                    Parking: IntOf(Feature("Parking Spaces")) ?? IntOf(Feature("Garages")),
                    FloorM2: NumberOf(Feature("Floor Size")),
                    ErfM2: NumberOf(Feature("Erf Size")),
                    ImageUrl: MainImage(tile)));
            }
            return result.DistinctBy(l => l.ListingNumber).ToList();
        }

        /// <summary>The main photo; lazy-loaded tiles hold it in lazy-src (src is /blank.gif).</summary>
        private static string? MainImage(HtmlNode tile)
        {
            var img = tile.SelectSingleNode(".//img[contains(@class,'js_P24_listingImage')]");
            if (img is null) return null;
            foreach (var attribute in new[] { "lazy-src", "data-src", "src" })
            {
                var url = img.GetAttributeValue(attribute, null);
                if (url is not null && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
            }
            return null;
        }

        public static (DateOnly? ListedOn, double? FloorM2, double? ErfM2) ParseDetailsText(string text)
        {
            DateOnly? listed = Regex.Match(text, @"Listing Date\s+(\d{1,2} \w+ \d{4})") is { Success: true } m
                && DateOnly.TryParseExact(m.Groups[1].Value, "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : null;
            double? Size(string label) => Regex.Match(text, label + @"\s+([\d\s ]+(?:\.\d+)?)\s*m²") is { Success: true } s
                ? NumberOf(s.Groups[1].Value) : null;
            return (listed, Size("Floor Size"), Size("Erf Size"));
        }

        private static P24Details ParseDetails(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            foreach (var n in doc.DocumentNode.SelectNodes("//script|//style")?.ToList() ?? []) n.Remove();
            var (listed, floor, erf) = ParseDetailsText(Clean(doc.DocumentNode.InnerText));
            return new P24Details(listed, floor, erf);
        }

        private sealed record P24Details(DateOnly? ListedOn, double? FloorM2, double? ErfM2);

        private async Task<string> GetAsync(string url, CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try
            {
                var wait = _lastCall + Pause - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                return await http.GetStringAsync(url, ct);
            }
            finally
            {
                _lastCall = DateTimeOffset.UtcNow;
                Gate.Release();
            }
        }

        public static string Slug(string name) =>
            Regex.Replace(Regex.Replace(name.Trim().ToLowerInvariant().Replace("'", ""), @"[^a-z0-9]+", "-"), "-+", "-").Trim('-');

        public static string Title(string slug) =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));

        private static string Clean(string s) => Regex.Replace(WebUtility.HtmlDecode(s), @"\s+", " ").Trim();

        private static int? IntOf(string? s) => int.TryParse(Regex.Match(s ?? "", @"\d+").Value, out var v) ? v : null;

        private static double? NumberOf(string? s)
        {
            var digits = Regex.Replace(s ?? "", @"[^\d.]", "");
            return double.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        }
    }
}
