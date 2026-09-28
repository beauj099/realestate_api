// ---------------------------------------------------------------------------------------------
// PropertyData.MosselBay — Mossel Bay municipal values, on top of the national cadastre.
//
// Checked against the live portal on 2026-09-26. Mossel Bay's roll is online at
// ndkonlineroll.co.za (NDK Valuers / Planner Municipal Software), roll id 7:
//
//   GET /en/7/Home/GetTownships → JSON, 349 townships: {"Id":4297,"TownshipName":"HARTENBOS",…}
//   GET /en/7/Home/SearchFT?FullTitle.AreaID=4297&FullTitle.Parcel=207&FullTitle.Portion=
//       → an HTML table: Township / Farm Name | Erf No/ Farm No | Portion | Remainder | Owner |
//         Address | Extents | Market Value | Category. Exact on erf number. No session, no cookie.
//
//   * The roll is the 2022–2026 general roll: valued as at 1 July 2021, in effect 1 July 2022
//     (Mossel Bay's roll notice; the portal's own export is titled "Valuation Roll 2022-2026").
//   * "R 0.00" is a placeholder (erf 6474 Mossel Bay, a residential erf), not a price.
//   * The Owner column is never read (POPIA). Columns are read by header name, so a column the
//     vendor adds is ignored rather than shifting the others.
//   * No address search: the pin finds the erf in the national cadastre, whose township name
//     ("MOSSEL BAY", "HARTENBOS") is the portal's township name. Mossel Bay has no "EXT"
//     townships, so the cadastre name is enough.
//   * No sales, so there are no municipal comparables; agent-reported sales fill that role.
//
// The same vendor hosts Metsimaholo (roll 6) and Emfuleni (roll 1), but their portals serve the
// 2019–2024 and 2017–2019 rolls, and erven sit in "EXT nn" townships the cadastre does not name
// (Sasolburg erf 5123 was in SASOLBURG EXT 05, one of 60 candidates). Not used for that reason.
// Metsimaholo's results also carry a 13-digit number in an SA ID number's format: another
// reason to read columns by name only.
//
// Mossel Bay parcels: the cadastre key starts with "W043" (demarcation code WC043).
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PropertyData.Core;
using PropertyData.Core.Models;
using PropertyData.National;

namespace PropertyData.MosselBay
{
    public sealed record NdkRollEntry(
        string Township,
        int Erf,
        int Portion,
        string? Address,
        double? ExtentM2,
        decimal? MarketValueZar,
        string? Category);

    /// <summary>An NDK online roll (one municipality per roll id).</summary>
    public sealed class NdkRollClient(HttpClient http, IMemoryCache cache, ILogger<NdkRollClient> log)
    {
        public const string Root = "https://www.ndkonlineroll.co.za/en";

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTimeOffset _lastCall;
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(400);

        private sealed record Township(int Id, string TownshipName);

        /// <summary>The roll entry for exactly this erf, or null when the roll has none.</summary>
        public async Task<NdkRollEntry?> LookUpAsync(int rollId, string township, string erf, CancellationToken ct = default)
        {
            var (stand, portion) = SplitErf(erf);
            if (stand is null) return null;

            var areaId = (await TownshipsAsync(rollId, ct))
                .FirstOrDefault(t => string.Equals(t.TownshipName.Trim(), township.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
            if (areaId is null)
            {
                log.LogInformation("NDK roll {Roll}: no township named {Township}", rollId, township);
                return null;
            }

            var html = await GetAsync(
                $"{Root}/{rollId}/Home/SearchFT?FullTitle.AreaID={areaId}&FullTitle.Parcel={stand}&FullTitle.Portion=", ct);
            return Parse(html).FirstOrDefault(e => e.Erf == stand && e.Portion == portion);
        }

        private async Task<IReadOnlyList<Township>> TownshipsAsync(int rollId, CancellationToken ct) =>
            (await cache.GetOrCreateAsync($"ndk:townships:{rollId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
                var json = await GetAsync($"{Root}/{rollId}/Home/GetTownships", ct);
                return JsonSerializer.Deserialize<List<Township>>(json) ?? [];
            }))!;

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

        /// <summary>The results table, read by header name. The Owner column is never read.</summary>
        public static IReadOnlyList<NdkRollEntry> Parse(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var rows = doc.DocumentNode.SelectNodes("//table")?
                .FirstOrDefault(t => t.InnerText.Contains("Market Value", StringComparison.OrdinalIgnoreCase))?
                .SelectNodes(".//tr");
            if (rows is null || rows.Count < 2) return [];

            var headers = rows[0].SelectNodes("./th|./td")?.Select(h => Clean(h.InnerText).ToUpperInvariant()).ToList() ?? [];
            int Col(string prefix) => headers.FindIndex(h => h.StartsWith(prefix, StringComparison.Ordinal));
            int iTown = Col("TOWNSHIP"), iErf = Col("ERF NO"), iPortion = Col("PORTION"), iAddress = Col("ADDRESS"),
                iExtent = Col("EXTENT"), iValue = Col("MARKET VALUE"), iCategory = Col("CATEGORY");
            if (iTown < 0 || iErf < 0 || iValue < 0)
                throw new HttpRequestException("NDK roll results have changed shape: " + string.Join(", ", headers));

            var result = new List<NdkRollEntry>();
            foreach (var tr in rows.Skip(1))
            {
                var cells = tr.SelectNodes("./td")?.Select(td => Clean(td.InnerText)).ToList();
                if (cells is null || cells.Count < headers.Count) continue;   // "No records found that match"
                string? At(int i) => i >= 0 && cells[i].Length > 0 ? cells[i] : null;
                if (!int.TryParse(At(iErf), NumberStyles.None, CultureInfo.InvariantCulture, out var erf)) continue;

                result.Add(new NdkRollEntry(
                    Township: At(iTown) ?? "",
                    Erf: erf,
                    Portion: int.TryParse(At(iPortion), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0,
                    Address: At(iAddress),
                    ExtentM2: Extent(At(iExtent)),
                    MarketValueZar: Money(At(iValue)),
                    Category: At(iCategory)));
            }
            return result;
        }

        /// <summary>"R 21,175,000.00" → 21 175 000; "R 0.00" is a placeholder, not a price.</summary>
        public static decimal? Money(string? s)
        {
            var digits = Regex.Replace(s ?? "", @"[^\d.]", "");
            return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 1 ? v : null;
        }

        /// <summary>"5,567 m²" → 5 567; "12.5 ha" → 125 000.</summary>
        public static double? Extent(string? s)
        {
            if (s is null) return null;
            var number = Regex.Match(s.Replace(",", ""), @"[\d.]+");
            if (!double.TryParse(number.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v <= 0) return null;
            return s.Contains("ha", StringComparison.OrdinalIgnoreCase) ? v * 10_000 : v;
        }

        /// <summary>"6474" → (6474, 0); "217/4" → (217, 4).</summary>
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
    /// A Mossel Bay property: identity and boundary from the national cadastre (found by GPS pin),
    /// street address, value, category and registered size from the municipality's roll.
    /// </summary>
    public sealed class MosselBayPropertyProvider(
        NationalCadastreProvider national,
        NdkRollClient roll,
        ILogger<MosselBayPropertyProvider> log) : IPropertyDataProvider
    {
        public const string Municipality = "mosselbay";
        public const string ParcelKeyPrefix = "W043";
        public const int RollId = 7;
        public const string Source = "Mossel Bay Municipality valuation roll 2022–2026";

        public string Name => "Mossel Bay valuation roll";
        public bool Handles(string municipality) => municipality == Municipality;

        public static bool IsMosselBayParcel(string? sg26) =>
            sg26 is not null && sg26.StartsWith(ParcelKeyPrefix, StringComparison.OrdinalIgnoreCase);

        public async Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default) =>
            (await national.ResolveAsync(q, ct))
                .Where(r => IsMosselBayParcel(r.Sg26))
                .Select(r => r with { Municipality = Municipality })
                .ToList();

        public async Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default)
        {
            var parcel = await national.FetchRecordAsync(@ref with { Municipality = NationalCadastreProvider.Municipality }, opts, ct);
            var township = parcel.Ref.Township;

            NdkRollEntry? entry;
            try
            {
                entry = await roll.LookUpAsync(RollId, township, parcel.Ref.Erf, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Mossel Bay roll unavailable for erf {Erf} {Township}", parcel.Ref.Erf, township);
                return parcel with { Ref = parcel.Ref with { Municipality = Municipality } };
            }

            var now = DateTimeOffset.UtcNow;
            return parcel with
            {
                Ref = parcel.Ref with { Municipality = Municipality },
                FormattedAddress = entry?.Address is { } address ? $"{address} {township}" : parcel.FormattedAddress,
                ExtentM2Deed = entry?.ExtentM2,
                Valuation = entry?.MarketValueZar is { } value
                    ? new MunicipalValuation(
                        ValueZar: value,
                        AsAt: new DateOnly(2021, 7, 1),
                        Category: entry.Category ?? "",
                        RollVersion: "GV2022",
                        RegisteredDescription: $"{parcel.Ref.Erf} {township}",
                        ExtentM2: entry.ExtentM2,
                        EffectiveFrom: new DateOnly(2022, 7, 1),
                        DisputeExpiry: null)
                    : null,
                DataSource = $"{Source} and the {parcel.DataSource}",
                Provenance = entry is null
                    ? parcel.Provenance
                    : [.. parcel.Provenance, new Provenance("municipalValuation", $"{NdkRollClient.Root}/{RollId}/Home/SearchFT", now)],
            };
        }
    }
}
