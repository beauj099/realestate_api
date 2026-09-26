// ---------------------------------------------------------------------------------------------
// PropertyData.Johannesburg — City of Johannesburg, and the national cadastre fallback.
//
// Written against the live services on 2026-09-26 (field names read from each layer's metadata,
// counts from returnCountOnly), NOT from the earlier draft, whose guessed field names would have
// failed: its "Stands" layer is a group layer, its address fields do not exist, and its address
// layer is lines, not points.
//
// City of Johannesburg — ags.joburg.org.za/server (note /server/, not /arcgis/), no key.
//   Property/MapServer/8 "Registered Stands": 672 926 stands. Per stand: STAND_NO, TOWN_NAME_DESC,
//   AREA_SQMT, ZONING, street address, MARKET_VALUE (GV2023: 627 643 filled), CAT_DESC, and the
//   LAST REGISTERED SALE (PURCHASE_PRICE / PURCHASE_DATE: 546 297 filled, 43 457 since 2024,
//   registrations to mid-2026). So values and current sales are both here — the earlier research
//   looked only at ValuationPurchases, which does stop in mid-2022.
//   The layer also has OWNER. It is never requested: a person's name has no place in a third
//   party's report (POPIA).
//   Property/MapServer/1 "Building Footprints": only the CBDs (Johannesburg, Randburg, Sandton…),
//   so most suburban stands have none.
//
// National — Chief Surveyor-General cadastre, DFFE mirror, no key. Every erf in the country, but
// identity and geometry only, and sampled records are dated 2017. The fallback, not a source.
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyData.CapeTown.Clients;
using PropertyData.CapeTown.Internal;
using PropertyData.CapeTown.Services;
using PropertyData.Core;
using PropertyData.Core.Models;

namespace PropertyData.Johannesburg
{
    public sealed class JohannesburgPropertyProvider(ArcGisClient arc, ILogger<JohannesburgPropertyProvider> log)
        : IPropertyDataProvider
    {
        private const string Root = "https://ags.joburg.org.za/server/rest/services/Property/MapServer";
        public const string StandsLayer = $"{Root}/8";
        public const string FootprintsLayer = $"{Root}/1";

        public const string Municipality = "coj";
        public const string Source = "City of Johannesburg open data";

        /// <summary>GV2023 values each property as at 1 July 2022 (billed from 1 July 2023).</summary>
        public static readonly DateOnly RollDateOfValuation = new(2022, 7, 1);

        // No OWNER, deliberately.
        private const string StandFields =
            "SG_ID,STAND_NO,AREA_SQMT,TOWN_NAME_DESC,STATUS_DESC,LAND_TYPE_NAME,MARKET_VALUE,ZONING," +
            "STREET_NO,STREET_NAME,STREET_TYPE_NAME,WARD_NAME,REGION_NAME,CAT_DESC,VALUATION_EFFECTIVE_DATE," +
            "PURCHASE_PRICE,PURCHASE_DATE";

        private const string SaleFields =
            "SG_ID,STAND_NO,AREA_SQMT,TOWN_NAME_DESC,CAT_DESC,STREET_NO,STREET_NAME,STREET_TYPE_NAME," +
            "PURCHASE_PRICE,PURCHASE_DATE";

        /// <summary>Comparables come from stands within this distance of the subject.</summary>
        public const double ComparableRadiusM = 800;

        public string Name => "City of Johannesburg (open GIS: stands, GV2023 values, last registered sales)";
        public bool Handles(string municipality) => municipality is "coj" or "joburg" or "johannesburg";

        public async Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default)
        {
            List<JsonElement> feats;
            if (q.Lat is not null && q.Lng is not null)
            {
                feats = await arc.QueryAsync(StandsLayer, PointQuery(new LatLng(q.Lat.Value, q.Lng.Value), StandFields), ct);
            }
            else if (!string.IsNullOrWhiteSpace(q.Erf))
            {
                var where = $"STAND_NO='{AddressNormalizer.SqlLiteral(q.Erf!.Trim())}'";
                if (!string.IsNullOrWhiteSpace(q.Suburb))
                    where += $" AND TOWN_NAME_DESC LIKE '{AddressNormalizer.SqlLiteral(q.Suburb!.Trim().ToUpperInvariant())}%'";
                feats = await arc.QueryAsync(StandsLayer, Where(where, StandFields), ct);
            }
            else if (!string.IsNullOrWhiteSpace(q.Address))
            {
                var a = AddressNormalizer.Parse(q.Address!);
                var where = new StringBuilder(
                    $"STREET_NO='{a.StreetNo}{a.StreetNoSuffix}' AND STREET_NAME='{AddressNormalizer.SqlLiteral(a.StreetName)}'");
                if (a.StreetType is not null)
                    where.Append($" AND STREET_TYPE_NAME='{AddressNormalizer.SqlLiteral(a.StreetType)}'");
                if (!string.IsNullOrWhiteSpace(a.Suburb))
                    where.Append($" AND TOWN_NAME_DESC LIKE '{AddressNormalizer.SqlLiteral(a.Suburb!)}%'");
                feats = await arc.QueryAsync(StandsLayer, Where(where.ToString(), StandFields), ct);
            }
            else throw new ArgumentException("Give an address, a lat/lng or an erf", nameof(q));

            return feats.Select(f =>
            {
                var at = f.GetProperty("attributes");
                var town = ArcGisClient.Str(at, "TOWN_NAME_DESC") ?? "";
                return new PropertyRef(ArcGisClient.Str(at, "STAND_NO") ?? "", ArcGisClient.Str(at, "SG_ID"),
                    null, town, town, Municipality);
            }).Where(r => r.Erf.Length > 0).ToList();
        }

        public async Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default)
        {
            var o = opts ?? new RecordOptions();
            var prov = new List<Provenance>();
            var now = DateTimeOffset.UtcNow;
            void Cite(string field, string source) => prov.Add(new Provenance(field, source, now));

            var where = !string.IsNullOrWhiteSpace(@ref.Sg26)
                ? $"SG_ID='{AddressNormalizer.SqlLiteral(@ref.Sg26!)}'"
                : $"STAND_NO='{AddressNormalizer.SqlLiteral(@ref.Erf)}'" +
                  (string.IsNullOrWhiteSpace(@ref.Suburb) ? "" : $" AND TOWN_NAME_DESC='{AddressNormalizer.SqlLiteral(@ref.Suburb)}'");
            var feats = await arc.QueryAsync(StandsLayer, Where(where, StandFields), ct);
            if (feats.Count == 0)
                throw new KeyNotFoundException($"Erf {@ref.Erf} {@ref.Suburb} not found in the Johannesburg stands");

            var stand = feats[0];
            var at = stand.GetProperty("attributes");
            var ring = ArcGisClient.ReadRing(stand);
            var town = ArcGisClient.Str(at, "TOWN_NAME_DESC") ?? @ref.Suburb;
            var sg = ArcGisClient.Str(at, "SG_ID");
            Cite("boundary", StandsLayer);
            Cite("zoning", StandsLayer);

            var buildings = o.IncludeBuildings && ring is not null ? await GetFootprintsAsync(ring, ct) : [];
            if (buildings.Count > 0) Cite("buildings", FootprintsLayer);

            var value = ArcGisClient.Num(at, "MARKET_VALUE");
            if (value is > 0) Cite("municipalValuation", StandsLayer);
            var extent = ArcGisClient.Num(at, "AREA_SQMT");

            var record = new PropertyRecord
            {
                Ref = @ref with { Erf = ArcGisClient.Str(at, "STAND_NO") ?? @ref.Erf, Sg26 = sg, Township = town, Suburb = town, Municipality = Municipality },
                FormattedAddress = Address(at) ?? $"ERF {@ref.Erf} {town}",
                Location = ring is null ? null : Geo.Centroid(ring),
                ExtentM2Deed = extent is > 0 ? extent : null,
                ExtentM2Geodesic = ring is null ? null : Math.Round(Geo.RingAreaM2(ring), 0),
                ZoningDescription = ArcGisClient.Str(at, "ZONING"),
                Ward = ArcGisClient.Str(at, "WARD_NAME"),
                LegalStatus = Title(ArcGisClient.Str(at, "STATUS_DESC")),
                Boundary = ring,
                Buildings = buildings,
                Valuation = value is > 0 ? new MunicipalValuation(
                    ValueZar: (decimal)value.Value,
                    AsAt: RollDateOfValuation,
                    Category: (ArcGisClient.Str(at, "CAT_DESC") ?? "").ToUpperInvariant(),
                    RollVersion: "GV2023",
                    RegisteredDescription: $"{ArcGisClient.Str(at, "STAND_NO")} {town}",
                    ExtentM2: extent,
                    EffectiveFrom: ArcGisClient.EpochDate(at, "VALUATION_EFFECTIVE_DATE"),
                    DisputeExpiry: null) : null,
                DataSource = Source,
                Provenance = prov,
            };

            if (o.IncludeComparables && record.Location is not null)
            {
                var category = ArcGisClient.Str(at, "CAT_DESC");
                var sales = await GetNearbySalesAsync(record.Location, category, sg, DateOnly.FromDateTime(DateTime.UtcNow), ct);
                Cite("comparables", $"{StandsLayer} (last registered sale of each stand within {ComparableRadiusM:0} m)");

                // No building sizes here, so similarity is by erf size and the range by price per
                // erf m². No suburb trend either: sales are within four years and used as recorded.
                var set = ComparableAnalyzer.Analyze(sales, record, suburb: null, DateOnly.FromDateTime(DateTime.UtcNow));
                log.LogInformation("CoJ erf {Erf} {Town}: {Raw} sales within {R} m → {Kept} comparables",
                    record.Ref.Erf, town, set.All.Count, ComparableRadiusM, set.Included.Count);
                record = record with { Comparables = set, Provenance = prov };
            }

            return record;
        }

        /// <summary>
        /// The last registered sale of every stand of the same category within
        /// <see cref="ComparableRadiusM"/>, over the last four years. Raw: R0 transfers included, so the
        /// analyzer can count and exclude them.
        /// </summary>
        public async Task<List<Comparable>> GetNearbySalesAsync(LatLng centre, string? category, string? subjectSg,
            DateOnly today, CancellationToken ct = default)
        {
            var since = today.AddYears(-4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var where = $"PURCHASE_DATE >= DATE '{since}'";
            if (!string.IsNullOrWhiteSpace(category))
                where += $" AND CAT_DESC='{AddressNormalizer.SqlLiteral(category!)}'";

            var form = PointQuery(centre, SaleFields);
            form["distance"] = ComparableRadiusM.ToString(CultureInfo.InvariantCulture);
            form["units"] = "esriSRUnit_Meter";
            form["where"] = where;
            form["returnGeometry"] = "false";
            var feats = await arc.QueryAsync(StandsLayer, form, ct);

            var list = new List<Comparable>();
            foreach (var f in feats)
            {
                var at = f.GetProperty("attributes");
                var sg = ArcGisClient.Str(at, "SG_ID");
                var date = ArcGisClient.EpochDate(at, "PURCHASE_DATE");
                if (date is null || date.Value > today || sg == subjectSg) continue;
                list.Add(new Comparable
                {
                    ValuationRef = sg ?? Guid.NewGuid().ToString("N"),
                    Address = Address(at) ?? $"ERF {ArcGisClient.Str(at, "STAND_NO")} {ArcGisClient.Str(at, "TOWN_NAME_DESC")}",
                    RegisteredDescription = $"{ArcGisClient.Str(at, "STAND_NO")} {ArcGisClient.Str(at, "TOWN_NAME_DESC")}",
                    Erf = ArcGisClient.Str(at, "STAND_NO"),
                    ErfExtentM2 = ArcGisClient.Num(at, "AREA_SQMT") ?? 0,
                    DwellingExtentM2 = 0,   // not published for Johannesburg
                    SaleDate = date.Value,
                    SalePriceZar = (decimal)(ArcGisClient.Num(at, "PURCHASE_PRICE") ?? 0),
                });
            }
            return list;
        }

        /// <summary>Type-ahead over stand addresses ("10 thirteenth", "10 thirteenth st park").</summary>
        public async Task<List<AddressSuggestion>> SuggestAsync(string text, int limit = 8, CancellationToken ct = default)
        {
            if (AddressNormalizer.ParsePartial(text) is not { } typed) return [];
            var (number, suffix, street, type, suburb) = typed;

            var where = new StringBuilder($"STREET_NAME LIKE '{AddressNormalizer.SqlLiteral(street)}%'");
            if (number is not null) where.Append($" AND STREET_NO='{number}{suffix}'");
            if (type is not null) where.Append($" AND STREET_TYPE_NAME='{AddressNormalizer.SqlLiteral(type)}'");
            if (!string.IsNullOrWhiteSpace(suburb))
                where.Append($" AND TOWN_NAME_DESC LIKE '{AddressNormalizer.SqlLiteral(suburb)}%'");

            if (number is null)
            {
                var streets = await arc.QueryAsync(StandsLayer, new Dictionary<string, string>
                {
                    ["where"] = where.ToString(),
                    ["outFields"] = "STREET_NAME,STREET_TYPE_NAME,TOWN_NAME_DESC",
                    ["returnDistinctValues"] = "true",
                    ["returnGeometry"] = "false",
                    ["orderByFields"] = "STREET_NAME,TOWN_NAME_DESC",
                    ["resultRecordCount"] = limit.ToString(CultureInfo.InvariantCulture),
                }, ct, singlePage: true);
                return streets.Select(f =>
                {
                    var at = f.GetProperty("attributes");
                    return new AddressSuggestion(null, null, ArcGisClient.Str(at, "STREET_NAME") ?? "",
                        ArcGisClient.Str(at, "STREET_TYPE_NAME"), ArcGisClient.Str(at, "TOWN_NAME_DESC") ?? "",
                        null, null, null);
                }).ToList();
            }

            var feats = await arc.QueryAsync(StandsLayer, new Dictionary<string, string>
            {
                ["where"] = where.ToString(),
                ["outFields"] = "SG_ID,STAND_NO,STREET_NO,STREET_NAME,STREET_TYPE_NAME,TOWN_NAME_DESC",
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
                ["orderByFields"] = "STREET_NAME,TOWN_NAME_DESC",
                ["resultRecordCount"] = limit.ToString(CultureInfo.InvariantCulture),
            }, ct, singlePage: true);

            return feats.Select(f =>
            {
                var at = f.GetProperty("attributes");
                var ring = ArcGisClient.ReadRing(f);
                var no = ArcGisClient.Str(at, "STREET_NO") ?? "";
                var digits = new string(no.TakeWhile(char.IsDigit).ToArray());
                return new AddressSuggestion(
                    int.TryParse(digits, out var n) ? n : null,
                    no.Length > digits.Length ? no[digits.Length..] : null,
                    ArcGisClient.Str(at, "STREET_NAME") ?? "", ArcGisClient.Str(at, "STREET_TYPE_NAME"),
                    ArcGisClient.Str(at, "TOWN_NAME_DESC") ?? "",
                    ArcGisClient.Str(at, "STAND_NO"), ArcGisClient.Str(at, "SG_ID"),
                    ring is null ? null : Geo.Centroid(ring));
            }).ToList();
        }

        private async Task<List<BuildingFootprint>> GetFootprintsAsync(Ring parcel, CancellationToken ct)
        {
            var polygon = JsonSerializer.Serialize(new
            {
                rings = new[] { parcel.Points.Select(p => new[] { p.Lng, p.Lat }).ToArray() },
                spatialReference = new { wkid = 4326 },
            });
            var feats = await arc.QueryAsync(FootprintsLayer, new Dictionary<string, string>
            {
                ["geometry"] = polygon,
                ["geometryType"] = "esriGeometryPolygon",
                ["inSR"] = "4326",
                ["spatialRel"] = "esriSpatialRelIntersects",
                ["outFields"] = "OBJECTID",
                ["returnGeometry"] = "true",
                ["outSR"] = "4326",
            }, ct);

            return feats
                .Select(ArcGisClient.ReadRing)
                .OfType<Ring>()
                .Where(r => Geo.Contains(parcel, Geo.Centroid(r)))   // not the neighbour's
                .Select(r => new BuildingFootprint(Math.Round(Geo.RingAreaM2(r), 1), null, null, null, r))
                .OrderByDescending(b => b.RoofM2)
                .ToList();
        }

        private static Dictionary<string, string> Where(string where, string fields) => new()
        {
            ["where"] = where,
            ["outFields"] = fields,
            ["returnGeometry"] = "true",
            ["outSR"] = "4326",
        };

        private static Dictionary<string, string> PointQuery(LatLng pt, string fields) => new()
        {
            ["geometry"] = JsonSerializer.Serialize(new { x = pt.Lng, y = pt.Lat, spatialReference = new { wkid = 4326 } }),
            ["geometryType"] = "esriGeometryPoint",
            ["inSR"] = "4326",
            ["spatialRel"] = "esriSpatialRelIntersects",
            ["outFields"] = fields,
            ["returnGeometry"] = "true",
            ["outSR"] = "4326",
        };

        /// <summary>"10 THIRTEENTH STREET PARKHURST".</summary>
        private static string? Address(JsonElement at)
        {
            var street = ArcGisClient.Str(at, "STREET_NAME");
            if (street is null) return null;
            return string.Join(' ', new[]
            {
                ArcGisClient.Str(at, "STREET_NO"), street, ArcGisClient.Str(at, "STREET_TYPE_NAME"),
                ArcGisClient.Str(at, "TOWN_NAME_DESC"),
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string? Title(string? s) =>
            s is null ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
}

namespace PropertyData.National
{
    /// <summary>
    /// The fallback anywhere in South Africa: the Chief Surveyor-General cadastre (DFFE mirror; the
    /// CSG's own host no longer resolves). Every erf, free, no key — but identity, extent and
    /// boundary only, with records dated around 2017. No value, no sales.
    /// </summary>
    public sealed class NationalCadastreProvider(ArcGisClient arc) : IPropertyDataProvider
    {
        private const string Service =
            "https://dffeportal.environment.gov.za/hosting/rest/services/CSG_Cadaster/CSG_Cadastral_Data/MapServer";

        // Erven first; holdings and farm portions for rural points.
        private static readonly string[] Layers = [$"{Service}/2", $"{Service}/3", $"{Service}/1"];

        public const string Municipality = "national";
        public const string Source = "Chief Surveyor-General national cadastre";

        private const string Fields = "PRCL_KEY,PARCEL_NO,PORTION,PROVINCE,MIN_REGION,GEOM_AREA,DATE_STAMP";

        public string Name => "Chief Surveyor-General cadastre (national fallback)";
        public bool Handles(string municipality) => municipality == Municipality;

        public async Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default)
        {
            if (q.Lat is null || q.Lng is null) return [];   // it has no street addresses
            foreach (var layer in Layers)
            {
                var feats = await arc.QueryAsync(layer, new Dictionary<string, string>
                {
                    ["geometry"] = JsonSerializer.Serialize(new { x = q.Lng, y = q.Lat, spatialReference = new { wkid = 4326 } }),
                    ["geometryType"] = "esriGeometryPoint",
                    ["inSR"] = "4326",
                    ["spatialRel"] = "esriSpatialRelIntersects",
                    ["outFields"] = Fields,
                    ["returnGeometry"] = "false",
                }, ct);
                if (feats.Count == 0) continue;
                return feats.Select(f =>
                {
                    var at = f.GetProperty("attributes");
                    var region = ArcGisClient.Str(at, "MIN_REGION") ?? "";
                    return new PropertyRef(ErfLabel(at), ArcGisClient.Str(at, "PRCL_KEY"), null, region, region, Municipality);
                }).ToList();
            }
            return [];
        }

        public async Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(@ref.Sg26))
                throw new KeyNotFoundException("The national cadastre is looked up by its parcel key; resolve first");

            foreach (var layer in Layers)
            {
                var feats = await arc.QueryAsync(layer, new Dictionary<string, string>
                {
                    ["where"] = $"PRCL_KEY='{AddressNormalizer.SqlLiteral(@ref.Sg26!)}'",
                    ["outFields"] = Fields,
                    ["returnGeometry"] = "true",
                    ["outSR"] = "4326",
                }, ct);
                if (feats.Count == 0) continue;

                var at = feats[0].GetProperty("attributes");
                var ring = ArcGisClient.ReadRing(feats[0]);
                var region = ArcGisClient.Str(at, "MIN_REGION") ?? @ref.Suburb;
                var stamp = ArcGisClient.EpochDate(at, "DATE_STAMP");
                var now = DateTimeOffset.UtcNow;
                return new PropertyRecord
                {
                    Ref = @ref with { Erf = ErfLabel(at), Township = region, Suburb = region },
                    FormattedAddress = $"ERF {ErfLabel(at)} {region}",
                    Location = ring is null ? null : Geo.Centroid(ring),
                    ExtentM2Geodesic = ring is null ? null : Math.Round(Geo.RingAreaM2(ring), 0),
                    Boundary = ring,
                    DataSource = stamp is null ? Source : $"{Source} (records of {stamp:yyyy})",
                    Provenance = [new Provenance("boundary", layer, now)],
                };
            }
            throw new KeyNotFoundException($"Parcel {@ref.Sg26} not found in the national cadastre");
        }

        /// <summary>"1106", or "1106/2" for a portion.</summary>
        private static string ErfLabel(JsonElement at)
        {
            var no = ArcGisClient.Num(at, "PARCEL_NO");
            var portion = ArcGisClient.Num(at, "PORTION");
            var erf = no is null ? "" : ((long)no.Value).ToString(CultureInfo.InvariantCulture);
            return portion is > 0 ? $"{erf}/{(long)portion.Value}" : erf;
        }
    }
}
