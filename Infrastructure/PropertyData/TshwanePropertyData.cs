// ---------------------------------------------------------------------------------------------
// PropertyData.Tshwane — City of Tshwane municipal values, on top of the national cadastre.
//
// Checked against the live site on 2026-09-26 (not taken from the earlier draft, which was wrong
// about what a search returns):
//
//   propertyvaluations.tshwane.gov.za/NewSearch — ASP.NET WebForms. GET the page for __VIEWSTATE,
//   __VIEWSTATEGENERATOR and __EVENTVALIDATION, POST them back with txterf / txttownship and
//   btnsearch. No cookie is needed. Columns: TOWNSHIP, LISKEY, UNIT NO, DESCRIPTION, CATEGORY,
//   SIZE, MARKET VALUE, ADDRESS, REMARK, SCHEME NAME.
//
//   * BOTH txterf and txttownship are substring matches. erf "1" + township "WATERKLOOF" returned
//     5 767 rows across 34 townships (Waterkloof Ridge, Waterkloof Glen X05, …) — every stand whose
//     number contains a 1. So we always send both and filter exactly ourselves: erf 1104 +
//     WATERKLOOF RIDGE is 2 rows, about 20 KB, under a second.
//   * The cadastre names the base township ("WATERKLOOF RIDGE"); the roll names the extension
//     ("WATERKLOOF RIDGE X02"). The roll's own key (LISKEY) uses one township code for a township
//     and its extensions, so stand numbers are unique across them: match base name + optional
//     " Xnn" + exact stand and portion.
//   * MARKET VALUE "R 1" (228 of those 5 767 rows) is a placeholder, not a price.
//   * ADDRESS is empty on every row: the roll is keyed on erf and township, so the address is
//     resolved to an erf through the national cadastre (GPS pin) first.
//   * There are no sales in the roll, so a Tshwane report has a value but no comparables.
//   * The OWNER search field is never used (POPIA).
//
// Which parcels are Tshwane's: the cadastre's parcel key starts with "GTSH" (Johannesburg's with
// "GJHB"), Centurion included. The roll is GV2025: valued as at 1 July 2024, in effect 1 July 2025
// to 30 June 2029 (City of Tshwane notices).
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PropertyData.Core;
using PropertyData.Core.Models;
using PropertyData.National;

namespace PropertyData.Tshwane
{
    public sealed record TshwaneRollEntry(
        string Township,
        string? LisKey,
        string Description,   // "01104", "00510/ 1", "00062/ R", "00062/ 1 - UNIT 0002"
        string? UnitNo,
        string? Category,
        double? SizeM2,
        decimal? MarketValueZar,
        string? SchemeName)
    {
        private static readonly Regex Shape = new(
            @"^(?<stand>\d+)(?:/(?<portion>\d+|R))?(?:/R)?(?:-UNIT(?<unit>\d+))?$", RegexOptions.IgnoreCase);

        private Match Parsed => Shape.Match(Description.Replace(" ", ""));

        public int? Stand => Parsed.Success ? int.Parse(Parsed.Groups["stand"].Value, CultureInfo.InvariantCulture) : null;

        /// <summary>0 for a whole erf and for the remainder ("/R"), which the cadastre calls portion 0.</summary>
        public int Portion => Parsed.Success && int.TryParse(Parsed.Groups["portion"].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var p) ? p : 0;

        /// <summary>A sectional-title unit on the erf, e.g. 2 in "00062/ 1 - UNIT 0002".</summary>
        public int? Unit => Parsed.Success && Parsed.Groups["unit"].Success
            ? int.Parse(Parsed.Groups["unit"].Value, CultureInfo.InvariantCulture) : null;
    }

    public sealed class TshwaneRollClient(HttpClient http, ILogger<TshwaneRollClient> log)
    {
        public const string SearchUrl = "https://propertyvaluations.tshwane.gov.za/NewSearch";

        // One request at a time to the City from this server, with a pause between: it is their
        // server, and a report only needs one lookup.
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTimeOffset _lastCall;
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(400);

        /// <summary>The roll entries for exactly this stand ("1104" or "510/1") in this township.</summary>
        public async Task<IReadOnlyList<TshwaneRollEntry>> LookUpAsync(string township, string erf, CancellationToken ct = default)
        {
            var (stand, portion) = SplitErf(erf);
            if (stand is null) return [];
            var rows = await SearchAsync(stand.Value.ToString(CultureInfo.InvariantCulture), township, ct);
            var exact = rows.Where(r => Matches(r, township, stand.Value, portion)).ToList();
            log.LogInformation("Tshwane roll: erf {Erf} {Township}: {Exact} exact of {All} returned",
                erf, township, exact.Count, rows.Count);
            return exact;
        }

        private async Task<IReadOnlyList<TshwaneRollEntry>> SearchAsync(string erf, string township, CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try
            {
                var wait = _lastCall + Pause - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

                var page = new HtmlDocument();
                page.LoadHtml(await http.GetStringAsync(SearchUrl, ct));
                var form = new Dictionary<string, string>();
                foreach (var name in new[] { "__VIEWSTATE", "__VIEWSTATEGENERATOR", "__EVENTVALIDATION" })
                {
                    var value = page.DocumentNode.SelectSingleNode($"//input[@name='{name}']")?.GetAttributeValue("value", null);
                    if (value is null) throw new HttpRequestException($"Tshwane roll search page has no {name}; the form has changed");
                    form[name] = WebUtility.HtmlDecode(value);
                }
                form["__EVENTTARGET"] = "";
                form["__EVENTARGUMENT"] = "";
                form["txterf"] = erf;
                form["txttownship"] = township.ToUpperInvariant();
                form["btnsearch"] = "Search Properties";

                using var response = await http.PostAsync(SearchUrl, new FormUrlEncodedContent(form), ct);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(ct);
                return Parse(html);
            }
            finally
            {
                _lastCall = DateTimeOffset.UtcNow;
                Gate.Release();
            }
        }

        /// <summary>The results grid, by its header names (not positions).</summary>
        public static IReadOnlyList<TshwaneRollEntry> Parse(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var table = doc.DocumentNode.SelectNodes("//table")?
                .FirstOrDefault(t => t.InnerText.Contains("MARKET VALUE", StringComparison.OrdinalIgnoreCase));
            var rows = table?.SelectNodes(".//tr");
            if (rows is null || rows.Count < 2) return [];

            var headers = rows[0].SelectNodes("./th|./td")?.Select(h => Clean(h.InnerText).ToUpperInvariant()).ToList() ?? [];
            int Col(string name) => headers.FindIndex(h => h == name);
            int iTown = Col("TOWNSHIP"), iKey = Col("LISKEY"), iUnit = Col("UNIT NO"), iDesc = Col("DESCRIPTION"),
                iCat = Col("CATEGORY"), iSize = Col("SIZE"), iValue = Col("MARKET VALUE"), iScheme = Col("SCHEME NAME");
            if (iTown < 0 || iDesc < 0 || iValue < 0)
                throw new HttpRequestException("Tshwane roll results have changed shape: " + string.Join(", ", headers));

            var result = new List<TshwaneRollEntry>();
            foreach (var tr in rows.Skip(1))
            {
                var cells = tr.SelectNodes("./td")?.Select(td => Clean(td.InnerText)).ToList();
                if (cells is null || cells.Count < headers.Count) continue;
                string? At(int i) => i >= 0 && cells[i].Length > 0 && cells[i] != "NULL" ? cells[i] : null;

                result.Add(new TshwaneRollEntry(
                    Township: At(iTown) ?? "",
                    LisKey: At(iKey),
                    Description: At(iDesc) ?? "",
                    UnitNo: At(iUnit),
                    Category: At(iCat),
                    SizeM2: double.TryParse(At(iSize), NumberStyles.Number, CultureInfo.InvariantCulture, out var size) && size > 0 ? size : null,
                    MarketValueZar: Money(At(iValue)),
                    SchemeName: At(iScheme)));
            }
            return result;
        }

        /// <summary>"R 4500000" → 4 500 000. "R 1" is the roll's placeholder for no value.</summary>
        public static decimal? Money(string? s)
        {
            var digits = Regex.Replace(s ?? "", @"[^\d.]", "");
            return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 1 ? v : null;
        }

        /// <summary>
        /// The same stand: township is the cadastre's base name or one of its extensions
        /// ("WATERKLOOF RIDGE X02"), the stand number equal (the roll zero-pads it), and the portion
        /// equal ("00510/ 1"); a whole erf also matches its remainder ("…/R"). Sectional-title units
        /// on the erf match too; <see cref="TshwaneRollEntry.Unit"/> tells them apart.
        /// </summary>
        public static bool Matches(TshwaneRollEntry e, string township, int stand, int portion)
        {
            var town = township.Trim().ToUpperInvariant();
            if (e.Township != town && !Regex.IsMatch(e.Township, "^" + Regex.Escape(town) + @" X\d+$")) return false;

            return e.Stand == stand && e.Portion == portion;
        }

        /// <summary>"1104" → (1104, 0); "510/1" → (510, 1).</summary>
        public static (int? Stand, int Portion) SplitErf(string erf)
        {
            var parts = erf.Trim().Split('/');
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var stand)) return (null, 0);
            var portion = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0;
            return (stand, portion);
        }

        private static string Clean(string s) => Regex.Replace(WebUtility.HtmlDecode(s), @"\s+", " ").Trim();
    }

    /// <summary>
    /// A Tshwane property: identity and boundary from the national cadastre (found by GPS pin),
    /// the municipal value, rating category and registered size from the City's GV2025 roll.
    /// </summary>
    public sealed class TshwanePropertyProvider(
        NationalCadastreProvider national,
        TshwaneRollClient roll,
        ILogger<TshwanePropertyProvider> log) : IPropertyDataProvider
    {
        public const string Municipality = "tshwane";
        public const string ParcelKeyPrefix = "GTSH";
        public const string Source = "City of Tshwane GV2025 valuation roll";

        public string Name => "City of Tshwane valuation roll";
        public bool Handles(string municipality) => municipality == Municipality;

        public static bool IsTshwaneParcel(string? sg26) =>
            sg26 is not null && sg26.StartsWith(ParcelKeyPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Pins only (the roll has no addresses): the cadastre's parcel, if it is Tshwane's.</summary>
        public async Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default) =>
            (await national.ResolveAsync(q, ct))
                .Where(r => IsTshwaneParcel(r.Sg26))
                .Select(r => r with { Municipality = Municipality })
                .ToList();

        public async Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default)
        {
            var parcel = await national.FetchRecordAsync(@ref with { Municipality = NationalCadastreProvider.Municipality }, opts, ct);
            var township = parcel.Ref.Township;

            IReadOnlyList<TshwaneRollEntry> entries;
            try
            {
                entries = await roll.LookUpAsync(township, parcel.Ref.Erf, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // The cadastre part is still worth showing.
                log.LogWarning(ex, "Tshwane roll unavailable for erf {Erf} {Township}", parcel.Ref.Erf, township);
                return parcel with { Ref = parcel.Ref with { Municipality = Municipality } };
            }

            // A sectional-title scheme has one row per unit (each valued) besides the erf's own row
            // (usually "R 1"): the erf has no single value then, so the report says so instead.
            var units = entries.Where(e => e.Unit is not null).ToList();
            var whole = entries.Where(e => e.Unit is null).ToList();
            var entry = whole.FirstOrDefault(e => e.MarketValueZar is not null) ?? whole.FirstOrDefault();
            if (units.Count > 0) entry = entry is null ? null : entry with { MarketValueZar = null };
            var now = DateTimeOffset.UtcNow;

            return parcel with
            {
                Ref = parcel.Ref with
                {
                    Municipality = Municipality,
                    ValuationRef = entry?.LisKey,
                    Township = entry?.Township ?? township,
                },
                FormattedAddress = $"ERF {parcel.Ref.Erf} {entry?.Township ?? township}",
                ExtentM2Deed = entry?.SizeM2,
                LegalStatus = units.Count == 0 ? parcel.LegalStatus
                    : $"Sectional title: {units.Count} unit{(units.Count == 1 ? "" : "s")} on the roll, each valued separately",
                Valuation = entry?.MarketValueZar is { } value
                    ? new MunicipalValuation(
                        ValueZar: value,
                        AsAt: new DateOnly(2024, 7, 1),
                        Category: entry.Category ?? "",
                        RollVersion: "GV2025",
                        RegisteredDescription: $"{entry.Description.Replace(" ", "")} {entry.Township}",
                        ExtentM2: entry.SizeM2,
                        EffectiveFrom: new DateOnly(2025, 7, 1),
                        DisputeExpiry: null)
                    : null,
                DataSource = $"{Source} and the {parcel.DataSource}",
                Provenance = entry is null
                    ? parcel.Provenance
                    : [.. parcel.Provenance, new Provenance("municipalValuation", TshwaneRollClient.SearchUrl, now)],
            };
        }
    }
}
