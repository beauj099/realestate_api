// A property in a municipality whose roll we import from its published PDF books
// (RollBookCatalogue, tools/ImportRollBooks): identity and boundary from the national cadastre
// (found by GPS pin), value, category, street address and extent from the imported roll.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PropertyData.Core;
using PropertyData.Core.Models;
using PropertyData.National;
using RealEstateApi.Infrastructure.Repositories;

namespace PropertyData.RollBooks
{
    /// <summary>What the roll says about one erf, once groups and schemes are accounted for.</summary>
    public sealed record RollBookFinding(
        decimal? ValueZar,
        string? Category,
        string? Address,
        double? ExtentM2,
        string? Note,
        RollBookEntryRow Source);

    public static class RollBookLookup
    {
        /// <summary>
        /// The roll's answer for erf <paramref name="erf"/>/<paramref name="portion"/> among
        /// <paramref name="rows"/> (which must include any group head it points to):
        /// <list type="bullet">
        /// <item>a group head ("5*") is valued for the whole group, and says which erven;</item>
        /// <item>a member ("See :- Paarl 5*") has no value of its own: the note gives the group's;</item>
        /// <item>an erf under a sectional-title scheme has no value of its own: its units do.</item>
        /// </list>
        /// </summary>
        public static RollBookFinding? Find(IReadOnlyList<RollBookEntryRow> rows, int erf, int portion)
        {
            var own = rows.Where(r => r.Erf == erf && r.Portion == portion).ToList();
            if (own.Count == 0) return null;
            var head = own.FirstOrDefault(r => r.IsGroupHead);
            var plain = own.FirstOrDefault(r => !r.IsGroupHead);

            if (head is not null)
            {
                var members = Regex.Matches(head.Particulars ?? "", @"\b(\d+)\b").Select(m => m.Groups[1].Value).Distinct().ToList();
                return new(Positive(head.ValueZar), head.Category, head.Address ?? plain?.Address,
                    (double?)(plain?.ExtentM2 ?? head.ExtentM2),
                    members.Count > 1
                        ? $"Valued together with erven {string.Join(", ", members)} as one property ({(double?)head.ExtentM2:N0} m² in all)"
                        : null,
                    head);
            }

            var row = plain!;
            if (row.GroupHeadErf is { } headErf)
            {
                var groupHead = rows.FirstOrDefault(r => r.Erf == headErf && r.IsGroupHead);
                var groupValue = groupHead?.ValueZar is > 0
                    ? $": R {groupHead.ValueZar.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} for the group"
                    : "";
                return new(null, row.Category, row.Address, (double?)row.ExtentM2,
                    $"Valued together with erf {headErf} and others as one property{groupValue}", row);
            }

            if (row.ValuedUnder is { } scheme)
                return new(null, row.Category, row.Address, (double?)row.ExtentM2,
                    $"Valued under {scheme} (sectional title or share block: its units are valued separately)", row);

            return new(Positive(row.ValueZar), row.Category, row.Address, (double?)row.ExtentM2, null, row);
        }

        private static decimal? Positive(decimal? v) => v is > 0 ? v : null;
    }

    public sealed class RollBookPropertyProvider(
        NationalCadastreProvider national,
        RollBookRepository repository,
        ILogger<RollBookPropertyProvider> log) : IPropertyDataProvider
    {
        public string Name => "Published roll books";
        public bool Handles(string municipality) => RollBookCatalogue.ForMunicipality(municipality) is not null;

        public async Task<IReadOnlyList<PropertyRef>> ResolveAsync(ResolveQuery q, CancellationToken ct = default) =>
            (await national.ResolveAsync(q, ct))
                .Select(r => (Ref: r, Book: RollBookCatalogue.ForParcel(r.Sg26)))
                .Where(x => x.Book is not null)
                .Select(x => x.Ref with { Municipality = x.Book!.Municipality })
                .ToList();

        public async Task<PropertyRecord> FetchRecordAsync(PropertyRef @ref, RecordOptions? opts = null, CancellationToken ct = default)
        {
            var book = RollBookCatalogue.ForMunicipality(@ref.Municipality)
                       ?? throw new KeyNotFoundException($"No roll books for '{@ref.Municipality}'.");
            var parcel = await national.FetchRecordAsync(@ref with { Municipality = NationalCadastreProvider.Municipality }, opts, ct);
            var relabelled = parcel with { Ref = parcel.Ref with { Municipality = book.Municipality } };

            var parts = parcel.Ref.Erf.Split('/');
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var erf)) return relabelled;
            var portion = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0;

            RollBookFinding? found;
            try
            {
                var rows = await repository.GetAsync(book.Municipality, parcel.Ref.Township, [erf], ct);
                // A member's value is on its group head: fetch that too.
                var heads = rows.Where(r => r.Erf == erf && r.GroupHeadErf is not null).Select(r => r.GroupHeadErf!.Value).ToList();
                if (heads.Count > 0) rows = [.. rows, .. await repository.GetAsync(book.Municipality, parcel.Ref.Township, heads, ct)];
                found = RollBookLookup.Find(rows, erf, portion);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tables not there yet (patch not applied) or the database is down: the cadastre
                // part of the report is still worth showing.
                log.LogWarning(ex, "Roll book lookup failed for {Municipality} erf {Erf} {Township}",
                    book.Municipality, parcel.Ref.Erf, parcel.Ref.Township);
                return relabelled;
            }
            if (found is null) return relabelled;

            var source = found.Source;
            var now = DateTimeOffset.UtcNow;
            var roll = $"{book.Name} {source.RollVersion} valuation roll ({book.PeriodLabel})";
            return relabelled with
            {
                FormattedAddress = found.Address is { } address ? $"{address} {parcel.Ref.Township}" : parcel.FormattedAddress,
                ExtentM2Deed = found.ExtentM2,
                LegalStatus = found.Note ?? parcel.LegalStatus,
                Valuation = found.ValueZar is { } value
                    ? new MunicipalValuation(
                        ValueZar: value,
                        AsAt: DateOnly.FromDateTime(source.DateOfValuation),
                        Category: RollBookCatalogue.DescribeCategory(found.Category),
                        RollVersion: source.RollVersion,
                        RegisteredDescription: $"{parcel.Ref.Erf} {parcel.Ref.Township}",
                        ExtentM2: found.ExtentM2,
                        EffectiveFrom: source.EffectiveFrom is { } from ? DateOnly.FromDateTime(from) : null,
                        DisputeExpiry: null)
                    : null,
                DataSource = $"{roll} and the {parcel.DataSource}",
                Provenance = [.. parcel.Provenance, new Provenance("municipalValuation", source.SourceUrl, now)],
            };
        }
    }
}
