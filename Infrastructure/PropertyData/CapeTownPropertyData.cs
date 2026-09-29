// ---------------------------------------------------------------------------------------------
// PropertyData.CapeTown — City of Cape Town property data adapter
//
// Single-file drop for a .NET 8 backend. Split into per-class files if you prefer; the regions
// below map 1:1 onto sensible files. Nothing here needs credentials: every endpoint used is
// public. AfriGIS (national ownership/transfers) is a separate adapter behind the same interface.
//
// NuGet:  HtmlAgilityPack >= 1.11.60
// Everything else is BCL (System.Net.Http.Json, System.Text.Json).
//
// Verified against the live services 2026-09-26 with 17 Pine Road, Claremont.
// See GoldenFixtureTests.cs for the assertions that lock this behaviour in.
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#region Models

namespace PropertyData.Core.Models
{
    public sealed record LatLng(double Lat, double Lng);

    /// <summary>Closed ring of WGS84 points. First point repeated at the end.</summary>
    public sealed record Ring(IReadOnlyList<LatLng> Points);

    public sealed record PropertyRef(
        string Erf,
        string? Sg26,
        string? ValuationRef,
        string Township,
        string Suburb,
        string Municipality = "coct");

    public sealed record ParsedAddress(
        int StreetNo,
        string? StreetNoSuffix,
        string StreetName,      // normalised: "PINE"
        string? StreetType,     // "ROAD" — kept for display, not for querying
        string? Suburb,         // "CLAREMONT"
        string? City,
        string Raw);

    public sealed record BuildingFootprint(
        double RoofM2,
        double? HeightM,
        int? CapturedYyyyMm,
        string? AcquisitionMethod,
        Ring Outline)
    {
        /// <summary>Crude storey estimate; 3m per storey. Display as "approx.", never as fact.</summary>
        public int? EstimatedStoreys => HeightM is null ? null : Math.Max(1, (int)Math.Round(HeightM.Value / 3.0));
    }

    public sealed record ApprovedWork(
        DateOnly? SubmissionDate,
        DateOnly? ApprovalDate,
        DateOnly? CompletionDate,
        DateOnly? OccupancyDate,
        string? Description,
        string? PrimaryCategory,
        string? SecondaryCategory,
        double? AreaM2,
        decimal? ValueZar,
        int? Units);

    public sealed record MunicipalValuation(
        decimal ValueZar,
        DateOnly AsAt,
        string Category,              // "RESIDENTIAL"
        string RollVersion,           // "GV2025"
        string RegisteredDescription, // "53927 CAPE TOWN"
        double? ExtentM2,
        DateOnly? EffectiveFrom,
        DateOnly? DisputeExpiry);

    public sealed record SuburbBenchmark(
        string Suburb,
        int ResidentialCount,
        double MedianLandM2,
        double MedianBuildingM2,
        decimal Gv2022Zar,
        decimal Gv2025Zar)
    {
        public double GrowthFactor => Gv2022Zar == 0 ? 1 : (double)(Gv2025Zar / Gv2022Zar);
        public double GrowthPercent => (GrowthFactor - 1) * 100;
        /// <summary>Compound annual rate implied over the 3 years between rolls.</summary>
        public double AnnualGrowthPercent => (Math.Pow(GrowthFactor, 1.0 / 3.0) - 1) * 100;
    }

    public enum ComparableExclusion
    {
        None, ZeroPrice, ImplausiblePrice, DissimilarSize, TooOld, IsSubject,
        /// <summary>One transfer of several properties at one price, recorded against each of them.</summary>
        MultiPropertySale,
        /// <summary>No building on record, while the subject has one (vacant land, or unknown).</summary>
        NoBuilding,
        /// <summary>Further away than the radius the report settled on.</summary>
        TooFar,
    }

    /// <summary>A registered sale of the subject property itself.</summary>
    public sealed record SaleRecord(DateOnly Date, decimal PriceZar);

    public sealed record Comparable
    {
        public required string ValuationRef { get; init; }
        public required string Address { get; init; }
        public required string RegisteredDescription { get; init; }
        public string? Erf { get; init; }
        public double ErfExtentM2 { get; init; }
        public double DwellingExtentM2 { get; init; }
        public DateOnly SaleDate { get; init; }
        public decimal SalePriceZar { get; init; }

        /// <summary>Where the sold property is (parcel centre) and how far from the subject, when known.</summary>
        public LatLng? Location { get; set; }
        public double? DistanceM { get; set; }

        public decimal? PricePerErfM2 => ErfExtentM2 > 0 ? SalePriceZar / (decimal)ErfExtentM2 : null;
        public decimal? PricePerDwellingM2 => DwellingExtentM2 > 0 ? SalePriceZar / (decimal)DwellingExtentM2 : null;

        /// <summary>Sale price indexed to the report date using the suburb trend.</summary>
        public decimal? IndexedPriceZar { get; set; }
        public ComparableExclusion Exclusion { get; set; } = ComparableExclusion.None;
        public bool Included => Exclusion == ComparableExclusion.None;

        /// <summary>Not used for the range, but listed so the report always shows enough sales.</summary>
        public bool Reference { get; set; }
    }

    public sealed record ComparableSet(
        IReadOnlyList<Comparable> All,
        IReadOnlyList<Comparable> Included,
        int ExcludedZeroPrice,
        int ExcludedImplausible,
        int ExcludedDissimilar,
        int ExcludedTooOld,
        decimal? MedianPricePerDwellingM2,
        decimal? MedianPricePerErfM2,
        decimal? ImpliedValueLowZar,
        decimal? ImpliedValueMidZar,
        decimal? ImpliedValueHighZar,
        // The radius the comparables were taken from (null: the City's whole neighbourhood list).
        int? RadiusM = null);

    public sealed record Provenance(string Field, string Source, DateTimeOffset FetchedAt);

    public sealed record PropertyRecord
    {
        public required PropertyRef Ref { get; init; }
        public required string FormattedAddress { get; init; }
        public LatLng? Location { get; init; }

        // Site
        public double? ExtentM2Deed { get; init; }       // from the roll — prefer this
        public double? ExtentM2Geodesic { get; init; }   // computed from the parcel ring
        public string? ZoningCode { get; init; }
        public string? ZoningDescription { get; init; }
        public string? Ward { get; init; }
        public string? SubCouncil { get; init; }
        public string? LegalStatus { get; init; }
        public Ring? Boundary { get; init; }

        // Improvements
        public double? DwellingExtentM2 { get; init; }   // the City's own figure
        public IReadOnlyList<BuildingFootprint> Buildings { get; init; } = [];
        public IReadOnlyList<ApprovedWork> ApprovedWork { get; init; } = [];

        public MunicipalValuation? Valuation { get; init; }
        public SuburbBenchmark? Suburb { get; init; }
        public ComparableSet? Comparables { get; init; }

        /// <summary>The subject's own last registered sale, when the source publishes it.</summary>
        public SaleRecord? LastSale { get; init; }

        /// <summary>Populated by the AfriGIS adapter only, and gated on the POPIA checks.</summary>
        public object? Ownership { get; init; }

        public IReadOnlyList<Provenance> Provenance { get; init; } = [];

        /// <summary>Who published this data, for credits: "City of Cape Town open data".</summary>
        public string DataSource { get; init; } = "City of Cape Town open data";

        public double? BestExtentM2 => ExtentM2Deed ?? ExtentM2Geodesic;
        public double? TotalRoofM2 => Buildings.Count == 0 ? null : Buildings.Sum(b => b.RoofM2);
    }
}

#endregion

#region Abstractions

namespace PropertyData.Core
{
    using PropertyData.Core.Models;

    public sealed record ResolveQuery(string? Address = null, double? Lat = null, double? Lng = null, string? Erf = null, string? Suburb = null);

    public sealed record RecordOptions(
        bool IncludeComparables = true,
        bool IncludeBuildings = true,
        bool IncludeApprovedWork = true,
        bool IncludeDwellingExtent = true,   // needs the stateful roll postback; slowest step
        int ComparableSinceYear = 0);        // 0 = last 4 years

    public interface IPropertyDataProvider
    {
        string Name { get; }
        bool Handles(string municipality);
        Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default);
        Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default);
    }
}

#endregion

#region Geo

namespace PropertyData.CapeTown.Internal
{
    using PropertyData.Core.Models;

    public static class Geo
    {
        private const double Rad = Math.PI / 180.0;

        // WGS84 ellipsoid.
        private const double SemiMajorM = 6378137.0;
        private const double EccentricitySq = 0.00669437999014;

        /// <summary>
        /// Ground area of a WGS84 ring, in m². Projects the ring onto a local plane using the
        /// ellipsoid's radii of curvature at the ring's mean latitude, then takes the shoelace area:
        /// exact to well under 0.1% at parcel scale. (A sphere of equatorial radius, as first
        /// written, read ~0.7% high at Cape Town's latitude.)
        /// NEVER use the service's Shape__Area field: it is Web Mercator and reads ~45% high at
        /// Cape Town's latitude (1569 m² for a 1085 m² erf).
        /// </summary>
        public static double RingAreaM2(Ring ring)
        {
            var p = ring.Points;
            if (p.Count < 4) return 0;

            double meanLat = 0;
            for (int i = 0; i < p.Count - 1; i++) meanLat += p[i].Lat;
            meanLat = meanLat / (p.Count - 1) * Rad;

            var sin = Math.Sin(meanLat);
            var w = Math.Sqrt(1 - EccentricitySq * sin * sin);
            var metresPerRadLat = SemiMajorM * (1 - EccentricitySq) / (w * w * w);   // meridional
            var metresPerRadLng = SemiMajorM / w * Math.Cos(meanLat);                // prime vertical

            double s = 0;
            for (int i = 0; i < p.Count - 1; i++)
            {
                double x1 = p[i].Lng * Rad * metresPerRadLng, y1 = p[i].Lat * Rad * metresPerRadLat;
                double x2 = p[i + 1].Lng * Rad * metresPerRadLng, y2 = p[i + 1].Lat * Rad * metresPerRadLat;
                s += x1 * y2 - x2 * y1;
            }
            return Math.Abs(s / 2.0);
        }

        /// <summary>
        /// Addresses from a GPS point: the parcel the point is on first, then the parcels within
        /// <paramref name="radiusM"/> nearest first (a phone's fix is often on the pavement or the
        /// neighbour's side of the fence).
        /// </summary>
        public static List<T> NearestFirst<T>(IEnumerable<(T Item, Ring? Ring)> parcels, LatLng pt) =>
            parcels
                .Select(p => (p.Item, Inside: p.Ring is not null && Contains(p.Ring, pt),
                    Metres: p.Ring is null ? double.MaxValue : DistanceM(Centroid(p.Ring), pt)))
                .OrderByDescending(p => p.Inside).ThenBy(p => p.Metres)
                .Select(p => p.Item)
                .ToList();

        /// <summary>Great-circle distance in metres (haversine; plenty at neighbourhood scale).</summary>
        public static double DistanceM(LatLng a, LatLng b)
        {
            var dLat = (b.Lat - a.Lat) * Rad;
            var dLng = (b.Lng - a.Lng) * Rad;
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(a.Lat * Rad) * Math.Cos(b.Lat * Rad) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * 6_371_008.8 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        public static LatLng Centroid(Ring ring)
        {
            var p = ring.Points;
            double lat = 0, lng = 0;
            int n = p.Count - 1 > 0 ? p.Count - 1 : p.Count;   // skip the repeated closing point
            for (int i = 0; i < n; i++) { lat += p[i].Lat; lng += p[i].Lng; }
            return new LatLng(lat / n, lng / n);
        }

        /// <summary>Ray-casting point-in-polygon. Used to drop neighbours' buildings.</summary>
        public static bool Contains(Ring ring, LatLng pt)
        {
            var p = ring.Points;
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                bool straddles = (p[i].Lat > pt.Lat) != (p[j].Lat > pt.Lat);
                if (!straddles) continue;
                double x = (p[j].Lng - p[i].Lng) * (pt.Lat - p[i].Lat) / (p[j].Lat - p[i].Lat) + p[i].Lng;
                if (pt.Lng < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Local flat projection in metres around an origin. Good enough for a site plan; do not
        /// use it for anything spanning more than a few hundred metres.
        /// </summary>
        public static (double X, double Y) ToLocalMetres(LatLng pt, LatLng origin) =>
            ((pt.Lng - origin.Lng) * Math.Cos(origin.Lat * Rad) * 111_320.0,
             (pt.Lat - origin.Lat) * 110_540.0);
    }
}

#endregion

#region Address normalisation

namespace PropertyData.CapeTown.Internal
{
    using PropertyData.Core.Models;

    /// <summary>
    /// Cape Town's parcels layer keeps the street TYPE in its own column (LU_STR_NAME_TYPE), so
    /// STR_NAME is "PINE", never "PINE ROAD". Query with the type stripped or you get zero rows.
    /// </summary>
    public static class AddressNormalizer
    {
        private static readonly Dictionary<string, string> TypeSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["RD"] = "ROAD", ["ROAD"] = "ROAD",
            ["ST"] = "STREET", ["STR"] = "STREET", ["STREET"] = "STREET",
            ["AVE"] = "AVENUE", ["AV"] = "AVENUE", ["AVENUE"] = "AVENUE",
            ["CL"] = "CLOSE", ["CLOSE"] = "CLOSE",
            ["CR"] = "CRESCENT", ["CRES"] = "CRESCENT", ["CRESCENT"] = "CRESCENT",
            ["DR"] = "DRIVE", ["DRIVE"] = "DRIVE",
            ["LN"] = "LANE", ["LANE"] = "LANE",
            ["WAY"] = "WAY", ["WALK"] = "WALK", ["PL"] = "PLACE", ["PLACE"] = "PLACE",
            ["BLVD"] = "BOULEVARD", ["BOULEVARD"] = "BOULEVARD",
            ["TER"] = "TERRACE", ["TERRACE"] = "TERRACE",
            ["MEWS"] = "MEWS", ["PARK"] = "PARK", ["RISE"] = "RISE", ["VIEW"] = "VIEW",
            ["LAAN"] = "AVENUE", ["STRAAT"] = "STREET", ["WEG"] = "ROAD",   // Afrikaans
        };

        private static readonly string[] UnitPrefixes = ["UNIT", "FLAT", "APT", "APARTMENT", "DOOR", "NO", "ERF", "STAND"];

        public static ParsedAddress Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Address is empty", nameof(raw));

            var cleaned = raw.ToUpperInvariant()
                             .Replace("&", " AND ")
                             .Replace(".", " ");
            // "UNIT 3, 17 PINE ROAD" -> drop the unit part; sectional title is looked up separately
            foreach (var pfx in UnitPrefixes)
            {
                var m = System.Text.RegularExpressions.Regex.Match(cleaned, $@"^\s*{pfx}\s+\w+\s*[,\-]\s*");
                if (m.Success) { cleaned = cleaned[m.Length..]; break; }
            }

            var parts = cleaned.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var streetPart = parts.Length > 0 ? parts[0] : cleaned;
            string? suburb = parts.Length > 1 ? parts[1].Trim() : null;
            string? city = parts.Length > 2 ? parts[^1].Trim() : null;

            var tokens = streetPart.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count == 0) throw new FormatException($"Cannot parse address: {raw}");

            // Street number, optionally with a letter suffix: 17, 17B, 21-23 (take the first)
            var numMatch = System.Text.RegularExpressions.Regex.Match(tokens[0], @"^(\d+)\s*([A-Z])?");
            if (!numMatch.Success) throw new FormatException($"No street number in: {raw}");
            int number = int.Parse(numMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            string? suffix = numMatch.Groups[2].Success ? numMatch.Groups[2].Value : null;
            tokens.RemoveAt(0);

            // Street type: the last type word after the street name. With a comma it ends the street
            // part ("17 PINE RD, CLAREMONT"); without one the suburb follows it ("17 PINE RD
            // CLAREMONT"), so everything after it is the suburb.
            string? type = null;
            for (int i = tokens.Count - 1; i >= 1; i--)
            {
                if (!TypeSynonyms.TryGetValue(tokens[i], out var canonical)) continue;
                var tail = tokens.Skip(i + 1).ToList();
                if (tail.Count > 0 && suburb is not null) continue;   // comma form: type must be last
                type = canonical;
                if (tail.Count > 0) suburb = string.Join(' ', tail);
                tokens = tokens.Take(i).ToList();
                break;
            }

            // No comma and no type word? The tail may be the suburb: "17 PINE CLAREMONT"
            if (type is null && suburb is null && tokens.Count > 1)
            {
                suburb = tokens[^1];
                tokens.RemoveAt(tokens.Count - 1);
            }

            var streetName = string.Join(' ', tokens).Trim();
            if (streetName.Length == 0) throw new FormatException($"No street name in: {raw}");

            return new ParsedAddress(number, suffix, streetName, type, suburb, city, raw);
        }

        public static bool IsStreetType(string token) => TypeSynonyms.ContainsKey(token);

        /// <summary>What has been typed so far, e.g. "17 pine rd clar".</summary>
        public sealed record PartialAddress(int? Number, string? Suffix, string Street, string? StreetType, string? Suburb);

        /// <summary>
        /// Reads a half-typed address for type-ahead: an optional leading number ("17", "12B"),
        /// street words up to a street type ("RD"), and anything after it as the start of the
        /// suburb. Null when there is no usable street text yet.
        /// </summary>
        public static PartialAddress? ParsePartial(string text)
        {
            var tokens = System.Text.RegularExpressions.Regex
                .Replace(text.ToUpperInvariant(), @"[^A-Z0-9 ]", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count == 0) return null;

            int? number = null;
            string? suffix = null;
            var numMatch = System.Text.RegularExpressions.Regex.Match(tokens[0], @"^(\d+)([A-Z])?$");
            if (numMatch.Success)
            {
                number = int.Parse(numMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                suffix = numMatch.Groups[2].Success ? numMatch.Groups[2].Value : null;
                tokens.RemoveAt(0);
            }
            if (tokens.Count == 0) return null;

            var typeAt = tokens.FindIndex(1, IsStreetType);
            var street = string.Join(' ', typeAt < 0 ? tokens : tokens.Take(typeAt));
            if (street.Length < 2) return null;
            return new PartialAddress(
                number, suffix, street,
                typeAt < 0 ? null : CanonicalType(tokens[typeAt]),
                typeAt < 0 ? null : string.Join(' ', tokens.Skip(typeAt + 1)));
        }

        /// <summary>
        /// The text as the start of a suburb name ("helder" → "HELDER"), or null when it cannot be
        /// one: it starts with a street number, or is shorter than three letters.
        /// </summary>
        public static string? SuburbPrefix(string text)
        {
            var name = System.Text.RegularExpressions.Regex
                .Replace(text.ToUpperInvariant(), @"[^A-Z ]", " ").Trim();
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ");
            if (name.Length < 3 || char.IsDigit(text.TrimStart().FirstOrDefault())) return null;
            return name;
        }

        /// <summary>"RD" → "ROAD"; null when the token is not a street type.</summary>
        public static string? CanonicalType(string token) => TypeSynonyms.GetValueOrDefault(token);

        /// <summary>Escapes a value for an ArcGIS SQL-92 where clause.</summary>
        public static string SqlLiteral(string v) => v.Replace("'", "''");
    }
}

#endregion

#region ArcGIS client

namespace PropertyData.CapeTown.Clients
{
    using PropertyData.Core.Models;

    public sealed class ArcGisClient(HttpClient http, ILogger<ArcGisClient> log)
    {
        /// <summary>
        /// POSTs a /query (POST avoids URL-length limits once geometry is involved) and pages
        /// through exceededTransferLimit. Returns the raw features array.
        /// </summary>
        public async Task<List<JsonElement>> QueryAsync(
            string layerUrl,
            IDictionary<string, string> form,
            CancellationToken ct = default,
            bool singlePage = false)
        {
            var results = new List<JsonElement>();
            int offset = 0;

            while (true)
            {
                var body = new Dictionary<string, string>(form) { ["f"] = "json" };
                if (offset > 0) body["resultOffset"] = offset.ToString(CultureInfo.InvariantCulture);

                using var resp = await http.PostAsync($"{layerUrl}/query", new FormUrlEncodedContent(body), ct);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var err))
                    throw new HttpRequestException($"ArcGIS error from {layerUrl}: {err}");

                if (!root.TryGetProperty("features", out var feats) || feats.GetArrayLength() == 0)
                    break;

                foreach (var f in feats.EnumerateArray()) results.Add(f.Clone());

                bool more = root.TryGetProperty("exceededTransferLimit", out var ex) && ex.ValueKind == JsonValueKind.True;
                if (!more || singlePage) break;
                offset += feats.GetArrayLength();
                if (offset > 20_000) { log.LogWarning("Paging cap hit on {Layer}", layerUrl); break; }
            }

            return results;
        }

        public static Ring? ReadRing(JsonElement feature)
        {
            if (!feature.TryGetProperty("geometry", out var g) || !g.TryGetProperty("rings", out var rings)) return null;
            if (rings.GetArrayLength() == 0) return null;
            var pts = rings[0].EnumerateArray()
                              .Select(p => new LatLng(p[1].GetDouble(), p[0].GetDouble()))
                              .ToList();
            if (pts.Count > 0 && (pts[0].Lat != pts[^1].Lat || pts[0].Lng != pts[^1].Lng)) pts.Add(pts[0]);
            return new Ring(pts);
        }

        public static string? Str(JsonElement attrs, string name) =>
            attrs.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

        public static double? Num(JsonElement attrs, string name) =>
            attrs.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        public static DateOnly? EpochDate(JsonElement attrs, string name)
        {
            var ms = Num(attrs, name);
            return ms is null ? null : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds((long)ms.Value).UtcDateTime);
        }
    }
}

#endregion

#region Cape Town spatial client

namespace PropertyData.CapeTown.Clients
{
    using PropertyData.CapeTown.Internal;
    using PropertyData.Core.Models;

    /// <summary>
    /// One address suggestion. With a street number it is a real erf (Erf, Location set);
    /// without one it is a street in a suburb, for the agent to add the number to.
    /// </summary>
    public sealed record AddressSuggestion(
        int? StreetNumber, string? StreetNumberSuffix, string StreetName, string? StreetType,
        string Suburb, string? Erf, string? Sg26, LatLng? Location);

    public sealed record ParcelHit(
        string Erf, string? Sg26, string? Zoning, string? Ward, string? SubCouncil,
        string? LegalStatus, string Suburb, string Township, string FormattedAddress, Ring? Boundary);

    /// <summary>
    /// Free, key-less ArcGIS endpoints published by the City of Cape Town.
    /// Layer URLs verified 2026-09-26. If one 404s, look it up again in the portal's search API:
    /// https://odp-cctegis.opendata.arcgis.com/api/search/v1/collections/dataset/items?q=&lt;name&gt;
    /// </summary>
    public sealed class CapeTownSpatialClient(ArcGisClient arc, ILogger<CapeTownSpatialClient> log)
    {
        private const string Hosted = "https://services6.arcgis.com/nyYfO9SxHU2ChQd9/arcgis/rest/services";
        // The City's production map server (the open data portal still links "esapqa", a test host).
        private const string Server = "https://citymaps.capetown.gov.za/agsext/rest/services";

        public const string ParcelsLayer    = $"{Hosted}/Property/FeatureServer/0";
        public const string ZoningLayer     = $"{Hosted}/Zoning/FeatureServer/0";
        public const string SuburbValLayer  = $"{Hosted}/Valuations_Suburbs_for_2022_and_2025/FeatureServer/0";
        public const string PlanApprovals   = $"{Hosted}/Building_Plan_Approvals_2014_to_2025/FeatureServer/0";
        public const string FootprintsLayer = $"{Server}/Theme_Based/ODP_SPLIT_6/FeatureServer/2";
        public const string RoadsLayer      = $"{Server}/Theme_Based/ODP_SPLIT_6/FeatureServer/8";

        private static Dictionary<string, string> BoxQuery(LatLng sw, LatLng ne, string fields) => new()
        {
            ["geometry"] = string.Create(CultureInfo.InvariantCulture, $"{sw.Lng},{sw.Lat},{ne.Lng},{ne.Lat}"),
            ["geometryType"] = "esriGeometryEnvelope",
            ["inSR"] = "4326",
            ["spatialRel"] = "esriSpatialRelIntersects",
            ["outFields"] = fields,
            ["returnGeometry"] = "true",
            ["outSR"] = "4326",
            ["geometryPrecision"] = "6",
        };

        /// <summary>Every parcel in a box, with its erf and street number, for the area map.</summary>
        public async Task<List<(string? Erf, string? Number, Ring Ring)>> GetParcelsInBoxAsync(LatLng sw, LatLng ne,
            CancellationToken ct = default)
        {
            var feats = await arc.QueryAsync(ParcelsLayer, BoxQuery(sw, ne, "PRTY_NMBR,ADR_NO,ADR_NO_SFX"), ct);
            var list = new List<(string?, string?, Ring)>(feats.Count);
            foreach (var f in feats)
            {
                if (ArcGisClient.ReadRing(f) is not { } ring) continue;
                var at = f.GetProperty("attributes");
                var no = ArcGisClient.Num(at, "ADR_NO");
                list.Add((ErfKey(ArcGisClient.Str(at, "PRTY_NMBR")),
                    no is null or 0 ? null : $"{(int)no.Value}{ArcGisClient.Str(at, "ADR_NO_SFX")}", ring));
            }
            return list;
        }

        /// <summary>Road centrelines in a box, with their names and widths, for the area map.</summary>
        public async Task<List<(string Name, string? Type, double? WidthM, List<LatLng> Line)>> GetRoadsInBoxAsync(
            LatLng sw, LatLng ne, CancellationToken ct = default)
        {
            var feats = await arc.QueryAsync(RoadsLayer, BoxQuery(sw, ne, "ROAD_NAME,ROAD_TYPE,RD_WIDTH"), ct);
            var list = new List<(string, string?, double?, List<LatLng>)>();
            foreach (var f in feats)
            {
                if (!f.TryGetProperty("geometry", out var g) || !g.TryGetProperty("paths", out var paths)) continue;
                var at = f.GetProperty("attributes");
                var name = ArcGisClient.Str(at, "ROAD_NAME") ?? "";
                foreach (var path in paths.EnumerateArray())
                    list.Add((name, ArcGisClient.Str(at, "ROAD_TYPE"), ArcGisClient.Num(at, "RD_WIDTH"),
                        path.EnumerateArray().Select(p => new LatLng(p[1].GetDouble(), p[0].GetDouble())).ToList()));
            }
            return list;
        }

        private const string ParcelFields =
            "PRTY_NMBR,SG26_CODE,ZONING,WARD_NAME,SUB_CNCL_NMBR,LU_LGL_STS_DSCR,OFC_SBRB_NAME,ALT_NAME,ADR_NO,ADR_NO_SFX,STR_NAME,LU_STR_NAME_TYPE";

        /// <summary>Attribute lookup — no geocoder needed. This is the cheap path for Cape Town.</summary>
        public async Task<List<ParcelHit>> FindByAddressAsync(ParsedAddress a, CancellationToken ct = default)
        {
            var where = new StringBuilder($"ADR_NO={a.StreetNo} AND STR_NAME='{AddressNormalizer.SqlLiteral(a.StreetName)}'");
            if (!string.IsNullOrWhiteSpace(a.Suburb))
                where.Append($" AND OFC_SBRB_NAME='{AddressNormalizer.SqlLiteral(a.Suburb!)}'");

            var hits = await RunParcelQuery(new Dictionary<string, string>
            {
                ["where"] = where.ToString(),
                ["outFields"] = ParcelFields,
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
            }, ct);

            // Suburb unknown or misspelled: retry without it, then rank by suburb similarity.
            if (hits.Count == 0 && !string.IsNullOrWhiteSpace(a.Suburb))
            {
                hits = await RunParcelQuery(new Dictionary<string, string>
                {
                    ["where"] = $"ADR_NO={a.StreetNo} AND STR_NAME='{AddressNormalizer.SqlLiteral(a.StreetName)}'",
                    ["outFields"] = ParcelFields,
                    ["returnGeometry"] = "true",
                    ["outSR"] = "4326",
                }, ct);

                hits = hits.OrderByDescending(h => Similar(h.Suburb, a.Suburb!)).ToList();
            }

            if (a.StreetNoSuffix is not null && hits.Count > 1)
            {
                var exact = hits.Where(h => h.FormattedAddress.Contains($"{a.StreetNo}{a.StreetNoSuffix} ", StringComparison.Ordinal)).ToList();
                if (exact.Count > 0) hits = exact;
            }

            return hits;
        }

        /// <summary>
        /// The centre of every parcel within <paramref name="radiusM"/> of a point, keyed by erf and
        /// allotment ("4429 STRAND"): what the City's area-sales list lacks to be measured by
        /// distance. About 2 800 parcels within 1 km in the Strand: two pages.
        /// </summary>
        public async Task<Dictionary<string, LatLng>> GetParcelCentresNearAsync(LatLng centre, double radiusM,
            CancellationToken ct = default)
        {
            var feats = await arc.QueryAsync(ParcelsLayer, new Dictionary<string, string>
            {
                ["geometry"] = JsonSerializer.Serialize(new { x = centre.Lng, y = centre.Lat, spatialReference = new { wkid = 4326 } }),
                ["geometryType"] = "esriGeometryPoint",
                ["inSR"] = "4326",
                ["distance"] = radiusM.ToString(CultureInfo.InvariantCulture),
                ["units"] = "esriSRUnit_Meter",
                ["spatialRel"] = "esriSpatialRelIntersects",
                ["outFields"] = "PRTY_NMBR,ALT_NAME",
                ["returnGeometry"] = "false",
                ["returnCentroid"] = "true",
                ["outSR"] = "4326",
            }, ct);

            var centres = new Dictionary<string, LatLng>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in feats)
            {
                if (!f.TryGetProperty("centroid", out var c)) continue;
                var at = f.GetProperty("attributes");
                var erf = ErfKey(ArcGisClient.Str(at, "PRTY_NMBR"));
                if (erf is null) continue;
                var point = new LatLng(c.GetProperty("y").GetDouble(), c.GetProperty("x").GetDouble());
                centres.TryAdd($"{erf} {AllotmentKey(ArcGisClient.Str(at, "ALT_NAME"))}", point);
                centres.TryAdd(erf, point);   // erf alone, for a sale whose town is spelt differently
            }
            return centres;
        }

        /// <summary>"8769-RE" / "8769-2" → "8769".</summary>
        public static string? ErfKey(string? propertyNumber)
        {
            var m = System.Text.RegularExpressions.Regex.Match(propertyNumber ?? "", @"^\d+");
            return m.Success ? m.Value : null;
        }

        /// <summary>"THE STRAND" → "STRAND", so the parcels layer and the sales list agree.</summary>
        public static string AllotmentKey(string? allotment)
        {
            var a = (allotment ?? "").Trim().ToUpperInvariant();
            return a.StartsWith("THE ", StringComparison.Ordinal) ? a[4..] : a;
        }

        /// <summary>Point-in-polygon lookup. Use this once a geocoder is in the chain — it generalises.</summary>
        public async Task<List<ParcelHit>> FindByPointAsync(LatLng pt, CancellationToken ct = default)
        {
            var geometry = JsonSerializer.Serialize(new
            {
                x = pt.Lng,
                y = pt.Lat,
                spatialReference = new { wkid = 4326 }
            });

            return await RunParcelQuery(new Dictionary<string, string>
            {
                ["geometry"] = geometry,
                ["geometryType"] = "esriGeometryPoint",
                ["inSR"] = "4326",
                ["spatialRel"] = "esriSpatialRelIntersects",
                ["outFields"] = ParcelFields,
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
            }, ct);
        }

        /// <summary>
        /// Type-ahead: "17 pine" → erfs at 17 PINE…, "17 pine rd clar" narrows the suburb, "pine rd"
        /// (no number) → streets. One small single-page query per call; callers debounce and cache.
        /// </summary>
        public async Task<List<AddressSuggestion>> SuggestAsync(string text, int limit = 8, CancellationToken ct = default)
        {
            var found = await SuggestOnceAsync(text, limit, ct);
            // Mid-word ("17 pine r"): the half-typed last word matches nothing, so try without it.
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (found.Count == 0 && words.Length > 2)
                found = await SuggestOnceAsync(string.Join(' ', words[..^1]), limit, ct);
            return found;
        }

        /// <summary>Official suburb names starting with the text ("helder" → HELDERVUE).</summary>
        public async Task<List<string>> SuggestSuburbsAsync(string text, int limit = 3, CancellationToken ct = default)
        {
            var name = AddressNormalizer.SuburbPrefix(text);
            if (name is null) return [];
            var feats = await arc.QueryAsync(ParcelsLayer, new Dictionary<string, string>
            {
                ["where"] = $"OFC_SBRB_NAME LIKE '{AddressNormalizer.SqlLiteral(name)}%'",
                ["outFields"] = "OFC_SBRB_NAME",
                ["returnDistinctValues"] = "true",
                ["returnGeometry"] = "false",
                ["orderByFields"] = "OFC_SBRB_NAME",
                ["resultRecordCount"] = limit.ToString(CultureInfo.InvariantCulture),
            }, ct, singlePage: true);
            return feats.Select(f => ArcGisClient.Str(f.GetProperty("attributes"), "OFC_SBRB_NAME"))
                .OfType<string>().ToList();
        }

        private async Task<List<AddressSuggestion>> SuggestOnceAsync(string text, int limit, CancellationToken ct)
        {
            if (AddressNormalizer.ParsePartial(text) is not { } typed) return [];
            var (number, suffix, street, type, suburb) = typed;

            var where = new StringBuilder($"STR_NAME LIKE '{AddressNormalizer.SqlLiteral(street)}%'");
            if (number is not null) where.Append($" AND ADR_NO={number}");
            // A typed street type ("RD") narrows to it: "PINE RD" is not "PINETREE AVENUE".
            if (type is not null)
                where.Append($" AND UPPER(LU_STR_NAME_TYPE)='{AddressNormalizer.SqlLiteral(type)}'");
            if (!string.IsNullOrWhiteSpace(suburb))
                where.Append($" AND OFC_SBRB_NAME LIKE '{AddressNormalizer.SqlLiteral(suburb)}%'");

            if (number is null)
            {
                var streets = await arc.QueryAsync(ParcelsLayer, new Dictionary<string, string>
                {
                    ["where"] = where.ToString(),
                    ["outFields"] = "STR_NAME,LU_STR_NAME_TYPE,OFC_SBRB_NAME",
                    ["returnDistinctValues"] = "true",
                    ["returnGeometry"] = "false",
                    ["orderByFields"] = "STR_NAME,OFC_SBRB_NAME",
                    ["resultRecordCount"] = limit.ToString(CultureInfo.InvariantCulture),
                }, ct, singlePage: true);
                return streets.Select(f =>
                {
                    var at = f.GetProperty("attributes");
                    return new AddressSuggestion(null, null, ArcGisClient.Str(at, "STR_NAME") ?? "",
                        ArcGisClient.Str(at, "LU_STR_NAME_TYPE"), ArcGisClient.Str(at, "OFC_SBRB_NAME") ?? "",
                        null, null, null);
                }).ToList();
            }

            var feats = await arc.QueryAsync(ParcelsLayer, new Dictionary<string, string>
            {
                ["where"] = where.ToString(),
                ["outFields"] = ParcelFields,
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
                ["orderByFields"] = "STR_NAME,OFC_SBRB_NAME",
                ["resultRecordCount"] = limit.ToString(CultureInfo.InvariantCulture),
            }, ct, singlePage: true);

            var list = feats.Select(f => ToSuggestion(f, ArcGisClient.ReadRing(f)));
            // "17B": prefer the matching suffix, keep the rest after it.
            return suffix is null ? list.ToList()
                : list.OrderByDescending(s => string.Equals(s.StreetNumberSuffix, suffix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static AddressSuggestion ToSuggestion(JsonElement f, Ring? ring)
        {
            var at = f.GetProperty("attributes");
            var sfx = ArcGisClient.Str(at, "ADR_NO_SFX");
            return new AddressSuggestion(
                (int?)ArcGisClient.Num(at, "ADR_NO"), string.IsNullOrWhiteSpace(sfx) ? null : sfx,
                ArcGisClient.Str(at, "STR_NAME") ?? "", ArcGisClient.Str(at, "LU_STR_NAME_TYPE"),
                ArcGisClient.Str(at, "OFC_SBRB_NAME") ?? "",
                ArcGisClient.Str(at, "PRTY_NMBR"), ArcGisClient.Str(at, "SG26_CODE"),
                ring is null ? null : Geo.Centroid(ring));
        }

        /// <summary>
        /// The City's address for a GPS point: the parcel it is on, then the nearest within
        /// <paramref name="radiusM"/> — with the house number, street and official suburb.
        /// </summary>
        public async Task<List<AddressSuggestion>> AddressesAtAsync(LatLng pt, double radiusM = 30,
            CancellationToken ct = default)
        {
            var feats = await arc.QueryAsync(ParcelsLayer, new Dictionary<string, string>
            {
                ["geometry"] = JsonSerializer.Serialize(new { x = pt.Lng, y = pt.Lat, spatialReference = new { wkid = 4326 } }),
                ["geometryType"] = "esriGeometryPoint",
                ["inSR"] = "4326",
                ["distance"] = radiusM.ToString(CultureInfo.InvariantCulture),
                ["units"] = "esriSRUnit_Meter",
                ["spatialRel"] = "esriSpatialRelIntersects",
                ["outFields"] = ParcelFields,
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
            }, ct, singlePage: true);
            return Geo.NearestFirst(feats.Select(f =>
            {
                var ring = ArcGisClient.ReadRing(f);
                return (ToSuggestion(f, ring), ring);
            }), pt);
        }

        public async Task<List<ParcelHit>> FindByErfAsync(string erf, string? suburb, CancellationToken ct = default)
        {
            var where = $"PRTY_NMBR='{AddressNormalizer.SqlLiteral(erf)}'";
            if (!string.IsNullOrWhiteSpace(suburb)) where += $" AND OFC_SBRB_NAME='{AddressNormalizer.SqlLiteral(suburb)}'";
            return await RunParcelQuery(new Dictionary<string, string>
            {
                ["where"] = where,
                ["outFields"] = ParcelFields,
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
            }, ct);
        }

        private async Task<List<ParcelHit>> RunParcelQuery(Dictionary<string, string> form, CancellationToken ct)
        {
            var feats = await arc.QueryAsync(ParcelsLayer, form, ct);
            var hits = new List<ParcelHit>(feats.Count);

            foreach (var f in feats)
            {
                var at = f.GetProperty("attributes");
                var no = ArcGisClient.Num(at, "ADR_NO");
                var sfx = (ArcGisClient.Str(at, "ADR_NO_SFX") ?? "").Trim();
                var street = ArcGisClient.Str(at, "STR_NAME");
                var type = ArcGisClient.Str(at, "LU_STR_NAME_TYPE");
                var suburb = ArcGisClient.Str(at, "OFC_SBRB_NAME") ?? "";

                var formatted = string.Join(' ', new[]
                {
                    no is null ? null : $"{(int)no.Value}{sfx}",
                    street, type?.ToUpperInvariant(), suburb
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

                hits.Add(new ParcelHit(
                    Erf: ArcGisClient.Str(at, "PRTY_NMBR") ?? "",
                    Sg26: ArcGisClient.Str(at, "SG26_CODE"),
                    Zoning: ArcGisClient.Str(at, "ZONING"),
                    Ward: ArcGisClient.Str(at, "WARD_NAME"),
                    SubCouncil: ArcGisClient.Str(at, "SUB_CNCL_NMBR"),
                    LegalStatus: ArcGisClient.Str(at, "LU_LGL_STS_DSCR"),
                    Suburb: suburb,
                    Township: ArcGisClient.Str(at, "ALT_NAME") ?? "CAPE TOWN",
                    FormattedAddress: formatted,
                    Boundary: ArcGisClient.ReadRing(f)));
            }

            return hits;
        }

        public async Task<(string? Code, string? Description)> GetZoningAsync(string sg26, CancellationToken ct = default)
        {
            var feats = await arc.QueryAsync(ZoningLayer, new Dictionary<string, string>
            {
                ["where"] = $"SG26_CODE='{AddressNormalizer.SqlLiteral(sg26)}'",
                ["outFields"] = "INT_ZONE_CODE,INT_ZONE_DESC",
                ["returnGeometry"] = "false",
            }, ct);

            if (feats.Count == 0) return (null, null);
            var at = feats[0].GetProperty("attributes");
            return (ArcGisClient.Str(at, "INT_ZONE_CODE"), ArcGisClient.Str(at, "INT_ZONE_DESC"));
        }

        /// <summary>
        /// Buildings on THIS parcel. Two gotchas handled: query with the parcel polygon (an
        /// envelope query returned 7 buildings for a parcel that has 5, pulling in neighbours),
        /// then keep only footprints whose centroid falls inside the parcel — semi-detached
        /// buildings still intersect across the boundary.
        /// </summary>
        public async Task<List<BuildingFootprint>> GetFootprintsAsync(Ring parcel, CancellationToken ct = default)
        {
            var polygon = JsonSerializer.Serialize(new
            {
                rings = new[] { parcel.Points.Select(p => new[] { p.Lng, p.Lat }).ToArray() },
                spatialReference = new { wkid = 4326 }
            });

            var feats = await arc.QueryAsync(FootprintsLayer, new Dictionary<string, string>
            {
                ["geometry"] = polygon,
                ["geometryType"] = "esriGeometryPolygon",
                ["inSR"] = "4326",
                ["spatialRel"] = "esriSpatialRelIntersects",
                ["outFields"] = "BLD_HGT,ACQS_MTHD,ACQS_PRD,DATA_SRC",
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
            }, ct);

            var list = new List<BuildingFootprint>();
            foreach (var f in feats)
            {
                var ring = ArcGisClient.ReadRing(f);
                if (ring is null) continue;
                if (!Geo.Contains(parcel, Geo.Centroid(ring))) continue;   // neighbour's building

                var at = f.GetProperty("attributes");
                list.Add(new BuildingFootprint(
                    RoofM2: Math.Round(Geo.RingAreaM2(ring), 1),
                    HeightM: ArcGisClient.Num(at, "BLD_HGT"),
                    CapturedYyyyMm: (int?)ArcGisClient.Num(at, "ACQS_PRD"),
                    AcquisitionMethod: ArcGisClient.Str(at, "ACQS_MTHD"),
                    Outline: ring));
            }

            return list.OrderByDescending(b => b.RoofM2).ToList();
        }

        public async Task<List<ApprovedWork>> GetApprovedWorkAsync(string erf, string suburb, CancellationToken ct = default)
        {
            // ERF_Number is numeric in this layer; Suburb carries qualifiers like "NYANGA (CAPE)".
            if (!int.TryParse(erf, out var erfNo)) return [];
            var where = $"ERF_Number={erfNo}";
            if (!string.IsNullOrWhiteSpace(suburb))
                where += $" AND Suburb LIKE '{AddressNormalizer.SqlLiteral(suburb)}%'";

            var feats = await arc.QueryAsync(PlanApprovals, new Dictionary<string, string>
            {
                ["where"] = where,
                ["outFields"] = "Submission_Date,Approval_Date,Completion_Date,OCC_Issued_Date,Building_Work_Descri,"
                              + "Type_of_Work_Description,Primary_Category__,Secondary_Category__,Area_of_New_Work,"
                              + "Building_Work_Value,Number_of_Units",
                ["returnGeometry"] = "false",
            }, ct);

            return feats.Select(f =>
            {
                var at = f.GetProperty("attributes");
                return new ApprovedWork(
                    SubmissionDate: ArcGisClient.EpochDate(at, "Submission_Date"),
                    ApprovalDate: ArcGisClient.EpochDate(at, "Approval_Date"),
                    CompletionDate: ArcGisClient.EpochDate(at, "Completion_Date"),
                    OccupancyDate: ArcGisClient.EpochDate(at, "OCC_Issued_Date"),
                    Description: ArcGisClient.Str(at, "Building_Work_Descri") ?? ArcGisClient.Str(at, "Type_of_Work_Description"),
                    PrimaryCategory: ArcGisClient.Str(at, "Primary_Category__"),
                    SecondaryCategory: ArcGisClient.Str(at, "Secondary_Category__"),
                    AreaM2: ArcGisClient.Num(at, "Area_of_New_Work"),
                    ValueZar: (decimal?)ArcGisClient.Num(at, "Building_Work_Value"),
                    Units: (int?)ArcGisClient.Num(at, "Number_of_Units"));
            })
            .OrderByDescending(w => w.ApprovalDate ?? w.SubmissionDate)
            .ToList();
        }

        public async Task<SuburbBenchmark?> GetSuburbBenchmarkAsync(string suburb, CancellationToken ct = default)
        {
            var feats = await arc.QueryAsync(SuburbValLayer, new Dictionary<string, string>
            {
                ["where"] = $"OFFICIAL_SUBURB='{AddressNormalizer.SqlLiteral(suburb)}'",
                ["outFields"] = "OFFICIAL_SUBURB,NUM_RES_PROP,MED_LAND_EXTENT_m2,MED_TOT_BLD_AREA_m2,GV2022_VAL,GV2025_VAL",
                ["returnGeometry"] = "false",
            }, ct);

            if (feats.Count == 0) { log.LogInformation("No suburb benchmark for {Suburb}", suburb); return null; }
            var at = feats[0].GetProperty("attributes");
            return new SuburbBenchmark(
                ArcGisClient.Str(at, "OFFICIAL_SUBURB") ?? suburb,
                (int)(ArcGisClient.Num(at, "NUM_RES_PROP") ?? 0),
                ArcGisClient.Num(at, "MED_LAND_EXTENT_m2") ?? 0,
                ArcGisClient.Num(at, "MED_TOT_BLD_AREA_m2") ?? 0,
                (decimal)(ArcGisClient.Num(at, "GV2022_VAL") ?? 0),
                (decimal)(ArcGisClient.Num(at, "GV2025_VAL") ?? 0));
        }

        private static int Similar(string a, string b)
        {
            var at = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var bt = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return bt.Count(at.Contains);
        }
    }
}

#endregion

#region Valuation roll client (HTML)

namespace PropertyData.CapeTown.Clients
{
    using PropertyData.Core.Models;

    public sealed record RollRow(
        string ValuationRef,
        string RegisteredDescription,
        string Category,
        string PhysicalAddress,
        double? ExtentM2,
        decimal? MarketValueZar,
        string RollVersion,
        DateOnly? EffectiveFrom,
        DateOnly? DisputeExpiry,
        string? PostbackTarget);

    /// <summary>
    /// The GV2025 roll is a public register under s.23 of the Municipal Property Rates Act, served
    /// by an ASP.NET WebForms app. Three behaviours, established by testing 2026-09-26:
    ///
    ///   Results?Search=ADD,{no},{street}   works as a stateless GET
    ///   Results?Search=VAL,{ref}           works as a stateless GET
    ///   Sales?parcelid={lowercase ref}     works as a stateless GET, and returns ALL rows in the
    ///                                      HTML (2 237 for the test parcel) — DataTables only
    ///                                      paginates them in the browser, so one GET is enough
    ///   DetStructRes?...                   FAILS as a deep link; needs the session + __doPostBack
    ///                                      from a Results page (see TryGetDwellingExtentAsync)
    ///
    /// Read the City's terms of use before pulling at volume, cache results your side so one report
    /// is one fetch, and keep the UserAgent honest so they can contact you rather than block you.
    /// </summary>
    public sealed class CapeTownRollClient(HttpClient http, ILogger<CapeTownRollClient> log)
    {
        public const string Base = "https://web1.capetown.gov.za/web1/gv2025";

        public Task<List<RollRow>> SearchByAddressAsync(int streetNo, string streetName, CancellationToken ct = default)
            => SearchAsync($"{Base}/Results?Search=ADD,{streetNo},{Uri.EscapeDataString(streetName)}", ct);

        public Task<List<RollRow>> SearchByValuationRefAsync(string valuationRef, CancellationToken ct = default)
            => SearchAsync($"{Base}/Results?Search=VAL,{Uri.EscapeDataString(valuationRef)}", ct);

        public Task<List<RollRow>> SearchByErfAsync(string erf, CancellationToken ct = default)
            => SearchAsync($"{Base}/Results?Search=ERF,{Uri.EscapeDataString(erf)}", ct);

        private async Task<List<RollRow>> SearchAsync(string url, CancellationToken ct)
        {
            var html = await http.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return ParseRollRows(doc);
        }

        internal static List<RollRow> ParseRollRows(HtmlDocument doc)
        {
            var rows = new List<RollRow>();

            // Data rows are identified by the property-reference anchor, whose href carries the
            // postback target we need later: javascript:__doPostBack('dgSearch$ctl03$lbParcelId','')
            var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href,'lbParcelId')]");
            if (anchors is null) return rows;

            foreach (var a in anchors)
            {
                var tr = a.Ancestors("tr").FirstOrDefault();
                if (tr is null) continue;
                var cells = tr.SelectNodes("./td")?.Select(td => Clean(td.InnerText)).ToList();
                if (cells is null || cells.Count < 10) continue;

                // The href is HTML-encoded: __doPostBack(&#39;dgSearch$ctl08$lbParcelId&#39;,&#39;&#39;)
                var href = HtmlEntity.DeEntitize(a.GetAttributeValue("href", "")) ?? "";
                var target = System.Text.RegularExpressions.Regex.Match(href, @"__doPostBack\('([^']+)'").Groups[1].Value;

                rows.Add(new RollRow(
                    ValuationRef: Clean(a.InnerText),
                    RegisteredDescription: cells[1],
                    Category: cells[2],
                    PhysicalAddress: cells[3],
                    ExtentM2: ParseDouble(cells[4]),
                    MarketValueZar: ParseMoney(cells[5]),
                    RollVersion: cells.Count > 8 ? cells[8] : "",
                    EffectiveFrom: ParseDate(cells.Count > 9 ? cells[9] : null),
                    DisputeExpiry: ParseDate(cells.Count > 10 ? cells[10] : null),
                    PostbackTarget: string.IsNullOrEmpty(target) ? null : target));
            }

            return rows;
        }

        /// <summary>
        /// Every recent sale the City lists "in the general area" of the parcel. One GET returns the
        /// whole set. Raw and unfiltered — run it through ComparableAnalyzer before showing anyone.
        /// </summary>
        public async Task<List<Comparable>> GetAreaSalesAsync(string valuationRef, CancellationToken ct = default)
        {
            var url = $"{Base}/Sales?parcelid={valuationRef.ToLowerInvariant()}";
            var html = await http.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode("//table[@id='gvSales']")
                     ?? doc.DocumentNode.SelectNodes("//table")?
                            .OrderByDescending(t => t.SelectNodes(".//tr")?.Count ?? 0)
                            .FirstOrDefault();

            var list = new List<Comparable>();
            var trs = table?.SelectNodes(".//tr");
            if (trs is null) { log.LogWarning("No sales table at {Url}", url); return list; }

            foreach (var tr in trs)
            {
                var tds = tr.SelectNodes("./td");
                if (tds is null || tds.Count < 7) continue;

                var cells = tds.Select(td => Clean(td.InnerText)).ToList();
                var saleDate = ParseDate(cells[5]);
                if (saleDate is null) continue;

                var regDesc = cells[2];                                   // "55775 CAPE TOWN"
                var erf = regDesc.Split(' ').FirstOrDefault();

                list.Add(new Comparable
                {
                    ValuationRef = cells[0],
                    Address = cells[1],
                    RegisteredDescription = regDesc,
                    Erf = erf,
                    ErfExtentM2 = ParseDouble(cells[3]) ?? 0,
                    DwellingExtentM2 = ParseDouble(cells[4]) ?? 0,
                    SaleDate = saleDate.Value,
                    SalePriceZar = ParseMoney(cells[6]) ?? 0m,
                });
            }

            log.LogInformation("{Count} raw area sales for {Ref}", list.Count, valuationRef);
            return list;
        }

        /// <summary>
        /// Dwelling extent — the City's own record of building m². Needs the WebForms session:
        /// GET the Results page for the reference, lift the hidden fields, POST the row's postback,
        /// then read the attribute page it redirects to. Returns null rather than throwing; the
        /// caller falls back to total roof area from the footprints.
        /// </summary>
        public async Task<double?> TryGetDwellingExtentAsync(string valuationRef, CancellationToken ct = default)
        {
            try
            {
                var resultsUrl = $"{Base}/Results?Search=VAL,{Uri.EscapeDataString(valuationRef)}";
                var html = await http.GetStringAsync(resultsUrl, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var row = ParseRollRows(doc).FirstOrDefault(r =>
                    string.Equals(r.ValuationRef, valuationRef, StringComparison.OrdinalIgnoreCase));
                if (row?.PostbackTarget is null) return null;

                var form = new Dictionary<string, string>
                {
                    ["__EVENTTARGET"] = row.PostbackTarget,
                    ["__EVENTARGUMENT"] = "",
                };
                foreach (var name in new[] { "__VIEWSTATE", "__VIEWSTATEGENERATOR", "__EVENTVALIDATION", "__VIEWSTATEENCRYPTED" })
                {
                    var v = doc.DocumentNode.SelectSingleNode($"//input[@name='{name}']")?.GetAttributeValue("value", null);
                    if (v is not null) form[name] = v;
                }

                using var resp = await http.PostAsync(resultsUrl, new FormUrlEncodedContent(form), ct);
                var detail = await resp.Content.ReadAsStringAsync(ct);

                // The redirect target is DetStructRes; if the app bounced us to the error page, give up.
                if (detail.Contains("something went wrong", StringComparison.OrdinalIgnoreCase)) return null;

                // Tags become spaces: InnerText glues neighbouring cells together
                // ("Dwelling Extent300"), which the pattern below would not match.
                var text = Clean(System.Text.RegularExpressions.Regex.Replace(detail, "<[^>]+>", " "));

                var m = System.Text.RegularExpressions.Regex.Match(text, @"Dwelling Extent\s+([\d.,]+)");
                return m.Success ? ParseDouble(m.Groups[1].Value) : null;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Dwelling extent lookup failed for {Ref}; falling back to roof area", valuationRef);
                return null;
            }
        }

        internal static string Clean(string? s) =>
            System.Text.RegularExpressions.Regex.Replace(HtmlEntity.DeEntitize(s ?? "") ?? "", @"\s+", " ").Trim();

        internal static double? ParseDouble(string? s) =>
            double.TryParse((s ?? "").Replace(" ", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

        internal static decimal? ParseMoney(string? s)
        {
            var cleaned = (s ?? "").Replace("R", "").Replace(" ", "").Replace(",", "").Trim();
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        internal static DateOnly? ParseDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var first = s.Split(new[] { " - ", " " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? s;
            string[] formats = ["yyyy/MM/dd", "yyyy-MM-dd", "dd/MM/yyyy", "yyyy/M/d"];
            return DateOnly.TryParseExact(first, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
    }
}

#endregion

#region Comparable analysis

namespace PropertyData.CapeTown.Services
{
    using PropertyData.Core.Models;

    public sealed record ComparableRules(
        decimal MinPlausiblePriceZar = 100_000m,
        double DwellingToleranceFraction = 0.30,   // ±30% on building size
        double ErfToleranceFraction = 0.50,
        int MaxAgeYears = 4,
        int MinIncluded = 3,
        // Nearest first: keep the comparables within the first radius that still leaves this many.
        int MinNearby = 6,
        // Listed at least this many: the next most alike sales fill up, for reference only.
        int MinShown = 10,
        // How value scales with size: price ∝ size^0.6 (double the size, ~1.5× the price).
        double SizeElasticity = 0.6)
    {
        public static readonly int[] RadiusStepsM = [500, 1000];
    }

    /// <summary>
    /// Where the credibility of the report actually lives. The City's list is raw: it includes
    /// non-arm's-length transfers at R 0, part-transfers at odd prices, and "general area" is the
    /// City's own neighbourhood, not a radius. Filter, then index to today, then take medians.
    /// </summary>
    public static class ComparableAnalyzer
    {
        public static ComparableSet Analyze(
            IReadOnlyList<Comparable> raw,
            PropertyRecord subject,
            SuburbBenchmark? suburb,
            DateOnly reportDate,
            ComparableRules? rules = null)
        {
            var r = rules ?? new ComparableRules();
            var subjectDwelling = subject.DwellingExtentM2 ?? subject.TotalRoofM2 ?? 0;
            var subjectErf = subject.BestExtentM2 ?? 0;
            var cutoff = reportDate.AddYears(-r.MaxAgeYears);

            // Several properties transferred on one day for one identical price: a bulk deal (or a
            // portfolio), recorded against each erf. Not a price for any one of them.
            var bulk = raw.Where(c => c.SalePriceZar > 0)
                .GroupBy(c => (c.SaleDate, c.SalePriceZar))
                .Where(g => g.Select(c => c.Erf ?? c.ValuationRef).Distinct().Count() > 1)
                .SelectMany(g => g)
                .ToHashSet();

            foreach (var c in raw)
            {
                if (!string.IsNullOrEmpty(subject.Ref.ValuationRef) &&
                    string.Equals(c.ValuationRef, subject.Ref.ValuationRef, StringComparison.OrdinalIgnoreCase))
                { c.Exclusion = ComparableExclusion.IsSubject; continue; }

                if (c.SalePriceZar <= 0) { c.Exclusion = ComparableExclusion.ZeroPrice; continue; }
                if (c.SalePriceZar < r.MinPlausiblePriceZar) { c.Exclusion = ComparableExclusion.ImplausiblePrice; continue; }
                if (c.SaleDate < cutoff) { c.Exclusion = ComparableExclusion.TooOld; continue; }
                if (bulk.Contains(c)) { c.Exclusion = ComparableExclusion.MultiPropertySale; continue; }
                if (subjectDwelling > 0 && c.DwellingExtentM2 <= 0 && raw.Any(x => x.DwellingExtentM2 > 0))
                { c.Exclusion = ComparableExclusion.NoBuilding; continue; }

                c.Exclusion = ComparableExclusion.None;
                c.IndexedPriceZar = Index(c.SalePriceZar, c.SaleDate, reportDate, suburb);
            }

            var candidates = raw.Where(c => c.Included).ToList();

            // 1. Nearest first: the smallest radius that still leaves enough sales. A house down the
            //    road says more than a same-sized one across the suburb, and price per m² of building
            //    already allows for size. Sales of unknown distance stay only when no radius does.
            int? radius = null;
            if (candidates.Any(c => c.DistanceM is not null))
            {
                foreach (var step in ComparableRules.RadiusStepsM)
                {
                    if (candidates.Count(c => c.DistanceM <= step) < r.MinNearby) continue;
                    radius = step;
                    foreach (var c in candidates.Where(c => c.DistanceM is null || c.DistanceM > step))
                        c.Exclusion = ComparableExclusion.TooFar;
                    candidates = candidates.Where(c => c.Included).ToList();
                    break;
                }
            }

            // 2. Then similar size among those: within 30%, else 50%, else the closest in size. A
            //    report with two comparables is worse than one with five slightly-less-similar ones.
            bool Similar(Comparable c, double tolerance)
            {
                if (subjectDwelling > 0 && c.DwellingExtentM2 > 0)
                    return Math.Abs(c.DwellingExtentM2 / subjectDwelling - 1) <= tolerance;
                if (subjectErf > 0 && c.ErfExtentM2 > 0)
                    return Math.Abs(c.ErfExtentM2 / subjectErf - 1) <= Math.Max(tolerance, r.ErfToleranceFraction);
                return true;
            }
            var included = candidates.Where(c => Similar(c, r.DwellingToleranceFraction)).ToList();
            if (included.Count < r.MinNearby) included = candidates.Where(c => Similar(c, 0.5)).ToList();
            if (included.Count < r.MinIncluded)
                included = candidates.OrderBy(c => SizeDistance(c, subjectDwelling, subjectErf))
                                     .Take(Math.Max(r.MinIncluded, 8))
                                     .ToList();
            foreach (var c in candidates.Where(c => !included.Contains(c)))
                c.Exclusion = ComparableExclusion.DissimilarSize;

            // 3. At least MinShown listed: the next most alike market sales (nearer ones first when
            //    alike, older ones too), for reference. They do not move the range: a home half the
            //    size says little about this one's value, but the agent and seller see the market.
            foreach (var c in raw) c.Reference = false;
            foreach (var c in raw
                         .Where(c => c.Exclusion is ComparableExclusion.DissimilarSize
                             or ComparableExclusion.TooFar or ComparableExclusion.TooOld)
                         .OrderBy(c => SizeDistance(c, subjectDwelling, subjectErf))
                         .ThenBy(c => c.DistanceM ?? double.MaxValue)
                         .Take(Math.Max(0, r.MinShown - included.Count)))
            {
                c.Reference = true;
                c.IndexedPriceZar ??= Index(c.SalePriceZar, c.SaleDate, reportDate, suburb);
            }

            var perDwelling = Median(included.Where(c => c.DwellingExtentM2 > 0)
                                             .Select(c => (c.IndexedPriceZar ?? c.SalePriceZar) / (decimal)c.DwellingExtentM2));
            var perErf = Median(included.Where(c => c.ErfExtentM2 > 0)
                                        .Select(c => (c.IndexedPriceZar ?? c.SalePriceZar) / (decimal)c.ErfExtentM2));

            // Each sale carried over to this property's size. Not pro rata: a home twice the size
            // sells for about 1.5 times as much (size elasticity 0.6), so scaling price per m²
            // straight up overvalues larger homes and undervalues smaller ones.
            decimal? mid = null, low = null, high = null;
            IEnumerable<decimal> Adjusted(Func<Comparable, double> size, double subjectSize) =>
                included.Where(c => size(c) > 0).Select(c =>
                    (c.IndexedPriceZar ?? c.SalePriceZar) * (decimal)Math.Pow(subjectSize / size(c), r.SizeElasticity));
            var implied = subjectDwelling > 0 && perDwelling is not null
                ? Adjusted(c => c.DwellingExtentM2, subjectDwelling).ToList()
                : subjectErf > 0 && perErf is not null
                    ? Adjusted(c => c.ErfExtentM2, subjectErf).ToList()
                    : [];
            if (implied.Count > 0)
            {
                mid = Median(implied);
                (low, high) = Spread(implied);
            }

            return new ComparableSet(
                All: raw,
                Included: included,
                ExcludedZeroPrice: raw.Count(c => c.Exclusion == ComparableExclusion.ZeroPrice),
                ExcludedImplausible: raw.Count(c => c.Exclusion == ComparableExclusion.ImplausiblePrice),
                ExcludedDissimilar: raw.Count(c => c.Exclusion == ComparableExclusion.DissimilarSize),
                ExcludedTooOld: raw.Count(c => c.Exclusion == ComparableExclusion.TooOld),
                MedianPricePerDwellingM2: perDwelling is null ? null : Math.Round(perDwelling.Value),
                MedianPricePerErfM2: perErf is null ? null : Math.Round(perErf.Value),
                ImpliedValueLowZar: Round(low),
                ImpliedValueMidZar: Round(mid),
                ImpliedValueHighZar: Round(high),
                RadiusM: radius);
        }

        /// <summary>
        /// Time-adjust an older sale. Uses the suburb's GV2022→GV2025 move as an annual rate — the
        /// only free trend signal available. Swap in the AfriGIS annual median series when that key
        /// arrives; it is per-suburb per-year and much better than one compounded number.
        /// </summary>
        private static decimal Index(decimal price, DateOnly saleDate, DateOnly reportDate, SuburbBenchmark? suburb)
        {
            if (suburb is null || suburb.Gv2022Zar <= 0) return price;
            var years = (reportDate.DayNumber - saleDate.DayNumber) / 365.25;
            if (years <= 0) return price;
            var annual = suburb.AnnualGrowthPercent / 100.0;
            return Math.Round(price * (decimal)Math.Pow(1 + annual, years), 0);
        }

        private static double SizeDistance(Comparable c, double subjectDwelling, double subjectErf)
        {
            if (subjectDwelling > 0 && c.DwellingExtentM2 > 0) return Math.Abs(c.DwellingExtentM2 - subjectDwelling) / subjectDwelling;
            if (subjectErf > 0 && c.ErfExtentM2 > 0) return Math.Abs(c.ErfExtentM2 - subjectErf) / subjectErf;
            return double.MaxValue;
        }

        private static decimal? Median(IEnumerable<decimal> values)
        {
            var v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return null;
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2m;
        }

        private static (decimal P25, decimal P75) Spread(IEnumerable<decimal> values)
        {
            var v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return (0, 0);
            // Interpolated between neighbours: with seven sales, rounding to a position picked the
            // 3rd and 5th values (R 3.98m - R 4.05m), a range far narrower than the sales.
            decimal At(double q)
            {
                var pos = q * (v.Count - 1);
                var lo = (int)Math.Floor(pos);
                var hi = Math.Min(lo + 1, v.Count - 1);
                return v[lo] + (v[hi] - v[lo]) * (decimal)(pos - lo);
            }
            return (At(0.25), At(0.75));
        }

        private static decimal? Round(decimal? v) => v is null ? null : Math.Round(v.Value / 10_000m) * 10_000m;
    }
}

#endregion

#region Site plan renderer (SVG)

namespace PropertyData.CapeTown.Services
{
    using PropertyData.CapeTown.Internal;
    using PropertyData.Core.Models;

    public sealed record SitePlanOptions(
        int WidthPx = 900,
        int HeightPx = 650,
        bool ShowNorthArrow = true,
        bool ShowScaleBar = true,
        bool ShowTitleBlock = true,
        bool ShowBuildingLabels = true,
        string ParcelStroke = "#14201f",
        string BuildingFill = "#9aa8a5",
        string BuildingStroke = "#14201f",
        string TextColor = "#14201f",
        string Background = "#ffffff");

    /// <summary>
    /// Draws the parcel and its buildings as an SVG site plan. This is the print-safe alternative
    /// to a Street View screenshot: our own drawing, from open municipal geometry, so there is no
    /// attribution box to preserve and no print restriction to worry about.
    ///
    /// Renders to a string. For Flutter use flutter_svg; for PDF use Svg.Skia (SkiaSharp) or drop
    /// the SVG straight into QuestPDF.
    /// </summary>
    public static class SitePlanRenderer
    {
        public static string Render(PropertyRecord rec, SitePlanOptions? options = null)
        {
            var o = options ?? new SitePlanOptions();
            if (rec.Boundary is null || rec.Boundary.Points.Count < 4)
                return EmptyPlan(o, "No cadastral boundary available");

            var origin = Geo.Centroid(rec.Boundary);

            // Project everything to local metres (Y up), then fit to the viewport (Y down).
            var parcel = rec.Boundary.Points.Select(p => Geo.ToLocalMetres(p, origin)).ToList();
            var buildings = rec.Buildings
                .Select(b => (B: b, Pts: b.Outline.Points.Select(p => Geo.ToLocalMetres(p, origin)).ToList()))
                .ToList();

            var all = parcel.Concat(buildings.SelectMany(b => b.Pts)).ToList();
            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X);
            double minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);

            double padPx = 56, titleH = o.ShowTitleBlock ? 92 : 24;
            double usableW = o.WidthPx - 2 * padPx, usableH = o.HeightPx - padPx - titleH;
            double spanX = Math.Max(maxX - minX, 1), spanY = Math.Max(maxY - minY, 1);
            double scale = Math.Min(usableW / spanX, usableH / spanY);     // px per metre

            double offX = padPx + (usableW - spanX * scale) / 2;
            double offY = padPx + (usableH - spanY * scale) / 2;

            (double x, double y) Px((double X, double Y) m) =>
                (offX + (m.X - minX) * scale, offY + (maxY - m.Y) * scale);

            string Path(List<(double X, double Y)> pts) =>
                string.Join(" ", pts.Select((p, i) =>
                {
                    var (x, y) = Px(p);
                    return $"{(i == 0 ? "M" : "L")}{x.ToString("0.##", CultureInfo.InvariantCulture)},{y.ToString("0.##", CultureInfo.InvariantCulture)}";
                })) + " Z";

            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture,
                $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {o.WidthPx} {o.HeightPx}" width="{o.WidthPx}" height="{o.HeightPx}" font-family="Helvetica, Arial, sans-serif">""");
            sb.Append(CultureInfo.InvariantCulture, $"""<rect width="{o.WidthPx}" height="{o.HeightPx}" fill="{o.Background}"/>""");

            // Parcel
            sb.Append(CultureInfo.InvariantCulture,
                $"""<path d="{Path(parcel)}" fill="none" stroke="{o.ParcelStroke}" stroke-width="2.2" stroke-linejoin="round"/>""");

            // Buildings
            foreach (var (b, pts) in buildings)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<path d="{Path(pts)}" fill="{o.BuildingFill}" fill-opacity="0.75" stroke="{o.BuildingStroke}" stroke-width="1.2"/>""");

                if (!o.ShowBuildingLabels || b.RoofM2 < 25) continue;
                var c = Px(pts.Aggregate((0.0, 0.0), (acc, p) => (acc.Item1 + p.X / pts.Count, acc.Item2 + p.Y / pts.Count)));
                // Invariant: a server on a comma-decimal culture (en-ZA) would print "6,5 m".
                var label = string.Create(CultureInfo.InvariantCulture, $"{b.RoofM2:0} m²");
                if (b.HeightM is not null) label += string.Create(CultureInfo.InvariantCulture, $" · {b.HeightM:0.0} m");
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{c.x:0.#}" y="{c.y:0.#}" font-size="11" fill="{o.TextColor}" text-anchor="middle" dominant-baseline="middle">{Esc(label)}</text>""");
            }

            // Boundary dimensions, on each run longer than 4 m
            for (int i = 0; i < parcel.Count - 1; i++)
            {
                var a = parcel[i]; var b = parcel[i + 1];
                double len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                if (len < 4) continue;
                var (ax, ay) = Px(a); var (bx, by) = Px(b);
                double mx = (ax + bx) / 2, my = (ay + by) / 2;
                double angle = Math.Atan2(by - ay, bx - ax) * 180 / Math.PI;
                if (angle > 90 || angle < -90) angle += 180;
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{mx:0.#}" y="{my:0.#}" font-size="10" fill="{o.TextColor}" fill-opacity="0.75" text-anchor="middle" dy="-4" transform="rotate({angle:0.#} {mx:0.#} {my:0.#})">{len:0.#} m</text>""");
            }

            if (o.ShowNorthArrow)
            {
                double nx = o.WidthPx - 44, ny = 46;
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<g stroke="{o.TextColor}" fill="{o.TextColor}"><line x1="{nx}" y1="{ny + 20}" x2="{nx}" y2="{ny - 14}" stroke-width="1.4"/><polygon points="{nx},{ny - 20} {nx - 5},{ny - 8} {nx + 5},{ny - 8}"/><text x="{nx}" y="{ny + 34}" font-size="11" text-anchor="middle" stroke="none">N</text></g>""");
            }

            if (o.ShowScaleBar)
            {
                double target = 10;                                   // metres
                while (target * scale < 60) target *= 2;
                double barPx = target * scale;
                double bx0 = padPx, by0 = o.HeightPx - titleH + 4;
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<g stroke="{o.TextColor}" fill="{o.TextColor}"><line x1="{bx0}" y1="{by0}" x2="{bx0 + barPx}" y2="{by0}" stroke-width="1.6"/><line x1="{bx0}" y1="{by0 - 4}" x2="{bx0}" y2="{by0 + 4}" stroke-width="1.6"/><line x1="{bx0 + barPx}" y1="{by0 - 4}" x2="{bx0 + barPx}" y2="{by0 + 4}" stroke-width="1.6"/><text x="{bx0 + barPx / 2}" y="{by0 + 16}" font-size="10" text-anchor="middle" stroke="none">{target:0} m</text></g>""");
            }

            if (o.ShowTitleBlock)
            {
                double ty = o.HeightPx - titleH + 34;
                var extent = rec.BestExtentM2 is null ? "" : string.Create(CultureInfo.InvariantCulture, $"Erf extent {rec.BestExtentM2:0} m²");
                var roof = rec.TotalRoofM2 is null ? "" : string.Create(CultureInfo.InvariantCulture, $"  ·  Buildings {rec.TotalRoofM2:0} m² footprint");
                var captured = rec.Buildings.FirstOrDefault()?.CapturedYyyyMm;
                var src = captured is null
                    ? $"Cadastre: {rec.DataSource}"
                    : $"Cadastre and building footprints: {rec.DataSource} (footprints captured {captured.ToString()![..4]}-{captured.ToString()![4..]})";

                sb.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{padPx}" y="{ty}" font-size="13" font-weight="bold" fill="{o.TextColor}">{Esc(rec.FormattedAddress)}</text>""");
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{padPx}" y="{ty + 17}" font-size="11" fill="{o.TextColor}" fill-opacity="0.8">{Esc($"Erf {rec.Ref.Erf} {rec.Ref.Township}  ·  {extent}{roof}")}</text>""");
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{padPx}" y="{ty + 32}" font-size="9" fill="{o.TextColor}" fill-opacity="0.6">{Esc(src)}</text>""");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string EmptyPlan(SitePlanOptions o, string message) =>
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {o.WidthPx} {o.HeightPx}" width="{o.WidthPx}" height="{o.HeightPx}"><rect width="{o.WidthPx}" height="{o.HeightPx}" fill="{o.Background}"/><text x="{o.WidthPx / 2}" y="{o.HeightPx / 2}" font-family="Helvetica, Arial, sans-serif" font-size="13" fill="#6d7e7c" text-anchor="middle">{Esc(message)}</text></svg>""";

        private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

#endregion

#region Provider

namespace PropertyData.CapeTown
{
    using PropertyData.CapeTown.Clients;
    using PropertyData.CapeTown.Internal;
    using PropertyData.CapeTown.Services;
    using PropertyData.Core;
    using PropertyData.Core.Models;

    public sealed class CapeTownPropertyProvider(
        CapeTownSpatialClient spatial,
        CapeTownRollClient roll,
        ILogger<CapeTownPropertyProvider> log) : IPropertyDataProvider
    {
        public string Name => "City of Cape Town (open data + GV2025 roll)";
        public bool Handles(string municipality) => municipality.Equals("coct", StringComparison.OrdinalIgnoreCase);

        public async Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default)
        {
            List<ParcelHit> hits;

            if (q.Lat is not null && q.Lng is not null)
                hits = await spatial.FindByPointAsync(new LatLng(q.Lat.Value, q.Lng.Value), ct);
            else if (!string.IsNullOrWhiteSpace(q.Erf))
                hits = await spatial.FindByErfAsync(q.Erf!, q.Suburb, ct);
            else if (!string.IsNullOrWhiteSpace(q.Address))
                hits = await spatial.FindByAddressAsync(AddressNormalizer.Parse(q.Address!), ct);
            else
                throw new ArgumentException("Give an address, a lat/lng or an erf", nameof(q));

            return hits.Select(h => new PropertyRef(h.Erf, h.Sg26, null, h.Township, h.Suburb)).ToList();
        }

        private async Task<List<T>> Optional<T>(Task<List<T>> task, string what)
        {
            try
            {
                return await task;
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning(ex, "Cape Town {What} unavailable; the report goes without", what);
                return [];
            }
        }

        public async Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default)
        {
            var o = opts ?? new RecordOptions();
            var prov = new List<Provenance>();
            var now = DateTimeOffset.UtcNow;
            void Cite(string field, string source) => prov.Add(new Provenance(field, source, now));

            // 1. Parcel — geometry, zoning label, ward
            var parcels = await spatial.FindByErfAsync(@ref.Erf, @ref.Suburb, ct);
            var parcel = parcels.FirstOrDefault(p => p.Sg26 == @ref.Sg26) ?? parcels.FirstOrDefault()
                         ?? throw new KeyNotFoundException($"Erf {@ref.Erf} not found in the Cape Town cadastre");
            Cite("boundary", CapeTownSpatialClient.ParcelsLayer);
            Cite("zoning", CapeTownSpatialClient.ParcelsLayer);

            double? geodesic = parcel.Boundary is null ? null : Math.Round(Geo.RingAreaM2(parcel.Boundary), 0);

            // 2. Roll row — municipal value, category, deed extent. Search by erf: an address search
            //    matches every street starting with the name ("PINE" -> Pine Acre, Pinedene, ...),
            //    and the roll pages those 10 at a time in no fixed order, so the subject can fall
            //    off the first page. The erf search returns the one row. Address is the fallback.
            var rollRow = PickRollRow(await roll.SearchByErfAsync(parcel.Erf, ct), parcel);
            if (rollRow is null)
            {
                var addr = AddressNormalizer.Parse(parcel.FormattedAddress);
                rollRow = PickRollRow(await roll.SearchByAddressAsync(addr.StreetNo, addr.StreetName, ct), parcel);
            }
            if (rollRow is not null) Cite("municipalValuation", $"{CapeTownRollClient.Base}/Results");

            // 3. Zoning detail, suburb benchmark, footprints, approved work — independent, so parallel
            // The parcel's own SG26: a ref resolved from an erf alone does not carry one yet.
            var sg26 = @ref.Sg26 ?? parcel.Sg26;
            var zoningTask = sg26 is null ? Task.FromResult<(string?, string?)>((null, null))
                                          : spatial.GetZoningAsync(sg26, ct);
            var suburbTask = spatial.GetSuburbBenchmarkAsync(parcel.Suburb, ct);
            // Buildings and plan approvals are detail: a City host being down must not cost the report.
            var footTask = o.IncludeBuildings && parcel.Boundary is not null
                ? Optional(spatial.GetFootprintsAsync(parcel.Boundary, ct), "building footprints")
                : Task.FromResult(new List<BuildingFootprint>());
            var workTask = o.IncludeApprovedWork
                ? Optional(spatial.GetApprovedWorkAsync(parcel.Erf, parcel.Suburb, ct), "plan approvals")
                : Task.FromResult(new List<ApprovedWork>());

            await Task.WhenAll(zoningTask, suburbTask, footTask, workTask);
            var (zCode, zDesc) = zoningTask.Result;
            var buildings = footTask.Result;
            var work = workTask.Result;
            var suburb = suburbTask.Result;

            if (buildings.Count > 0) Cite("buildings", CapeTownSpatialClient.FootprintsLayer);
            if (work.Count > 0) Cite("approvedWork", CapeTownSpatialClient.PlanApprovals);
            if (suburb is not null) Cite("suburbBenchmark", CapeTownSpatialClient.SuburbValLayer);

            // 4. Dwelling extent — the stateful step; fall back to roof area
            double? dwelling = null;
            if (o.IncludeDwellingExtent && rollRow is not null)
            {
                dwelling = await roll.TryGetDwellingExtentAsync(rollRow.ValuationRef, ct);
                if (dwelling is not null) Cite("dwellingExtent", $"{CapeTownRollClient.Base}/DetStructRes");
            }

            var refWithVal = @ref with { ValuationRef = rollRow?.ValuationRef, Sg26 = @ref.Sg26 ?? parcel.Sg26 };

            var record = new PropertyRecord
            {
                Ref = refWithVal,
                FormattedAddress = rollRow?.PhysicalAddress ?? parcel.FormattedAddress,
                Location = parcel.Boundary is null ? null : Geo.Centroid(parcel.Boundary),
                ExtentM2Deed = rollRow?.ExtentM2,
                ExtentM2Geodesic = geodesic,
                ZoningCode = zCode,
                ZoningDescription = zDesc ?? parcel.Zoning,
                Ward = parcel.Ward,
                SubCouncil = parcel.SubCouncil,
                LegalStatus = parcel.LegalStatus,
                Boundary = parcel.Boundary,
                DwellingExtentM2 = dwelling,
                Buildings = buildings,
                ApprovedWork = work,
                Suburb = suburb,
                Valuation = rollRow is null || rollRow.MarketValueZar is null ? null : new MunicipalValuation(
                    ValueZar: rollRow.MarketValueZar.Value,
                    AsAt: new DateOnly(2025, 7, 1),           // GV2025 date of valuation
                    Category: rollRow.Category,
                    RollVersion: rollRow.RollVersion,
                    RegisteredDescription: rollRow.RegisteredDescription,
                    ExtentM2: rollRow.ExtentM2,
                    EffectiveFrom: rollRow.EffectiveFrom,
                    DisputeExpiry: rollRow.DisputeExpiry),
                Provenance = prov,
            };

            // 5. Comparables — needs the subject's own sizes, so it runs last
            if (o.IncludeComparables && rollRow is not null)
            {
                var rawSales = await roll.GetAreaSalesAsync(rollRow.ValuationRef, ct);
                Cite("comparables", $"{CapeTownRollClient.Base}/Sales");

                var rules = o.ComparableSinceYear > 0
                    ? new ComparableRules(MaxAgeYears: Math.Max(1, DateTime.UtcNow.Year - o.ComparableSinceYear))
                    : new ComparableRules();

                // The City's list is its own "general area"; measure each sale so the analyzer can
                // keep the nearest. Without the parcel centres it falls back to the whole list.
                if (record.Location is { } here)
                {
                    try
                    {
                        var centres = await spatial.GetParcelCentresNearAsync(here, ComparableRules.RadiusStepsM[^1], ct);
                        foreach (var sale in rawSales)
                        {
                            var parts = sale.RegisteredDescription.Split(' ', 2);
                            var erfKey = CapeTownSpatialClient.ErfKey(parts[0]);
                            if (erfKey is null) continue;
                            var town = parts.Length > 1 ? CapeTownSpatialClient.AllotmentKey(parts[1]) : "";
                            if (centres.TryGetValue($"{erfKey} {town}", out var at) || centres.TryGetValue(erfKey, out at))
                            {
                                sale.Location = at;
                                sale.DistanceM = Math.Round(Geo.DistanceM(here, at));
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        log.LogWarning(ex, "Parcel centres unavailable; comparables not measured by distance");
                    }
                }

                var set = ComparableAnalyzer.Analyze(rawSales, record, suburb,
                    DateOnly.FromDateTime(DateTime.UtcNow), rules);
                var ownSale = rawSales.Where(c => c.Exclusion == ComparableExclusion.IsSubject && c.SalePriceZar > 0)
                    .OrderByDescending(c => c.SaleDate).FirstOrDefault();
                if (ownSale is not null) record = record with { LastSale = new SaleRecord(ownSale.SaleDate, ownSale.SalePriceZar) };

                log.LogInformation("Erf {Erf}: {Raw} raw sales → {Kept} kept (R0: {Zero}, old: {Old}, size: {Size})",
                    parcel.Erf, set.All.Count, set.Included.Count, set.ExcludedZeroPrice, set.ExcludedTooOld, set.ExcludedDissimilar);

                record = record with { Comparables = set, Provenance = prov };
            }

            return record;
        }

        private static RollRow? PickRollRow(List<RollRow> rows, ParcelHit parcel)
        {
            if (rows.Count == 0) return null;

            // The roll's registered description is "<erf> <township>", e.g. "53927 CAPE TOWN".
            var byErf = rows.Where(r => r.RegisteredDescription
                                         .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                         .FirstOrDefault() == parcel.Erf).ToList();
            if (byErf.Count == 1) return byErf[0];
            if (byErf.Count > 1) return byErf.FirstOrDefault(r => r.PhysicalAddress.Contains(parcel.Suburb, StringComparison.OrdinalIgnoreCase)) ?? byErf[0];

            return rows.FirstOrDefault(r => r.PhysicalAddress.Contains(parcel.Suburb, StringComparison.OrdinalIgnoreCase));
        }
    }
}

#endregion

#region DI

namespace PropertyData.CapeTown
{
    using PropertyData.CapeTown.Clients;
    using PropertyData.Core;

    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// builder.Services.AddCapeTownPropertyData("PropValu/1.0 (+https://yourdomain.co.za; you@yourdomain.co.za)");
        /// Identify yourself honestly in the UserAgent — it is what gets you a call instead of a block.
        /// </summary>
        public static IServiceCollection AddCapeTownPropertyData(this IServiceCollection services, string userAgent)
        {
            services.AddHttpClient<ArcGisClient>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(45);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            });

            services.AddHttpClient<CapeTownRollClient>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(60);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // WebForms postbacks need the session cookie, and Results -> DetStructRes is a redirect.
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
            });

            services.AddScoped<CapeTownSpatialClient>();
            services.AddScoped<IPropertyDataProvider, CapeTownPropertyProvider>();
            return services;
        }
    }
}

#endregion
