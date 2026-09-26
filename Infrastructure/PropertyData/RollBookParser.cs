// ---------------------------------------------------------------------------------------------
// RollBookParser — a published PDF valuation roll ("roll book") into rows.
//
// Most municipalities have no roll search; they publish the roll as PDF books, one per town. A
// book is the whole roll: every erf, its category, street address, extent and market value, and
// no owner names.
//
// Written against Drakenstein's GV2024 books (Paarl: 700 pages) on 2026-09-26. The footer reads
// "© 2010 PenSoft CC (Mass Appraisal Software Solution)", so the layout is the vendor's and other
// PenSoft councils' books should read the same; check a new council's footer and header positions
// first (the parser refuses pages whose header is not where it expects).
//
// What the pages actually look like (A4 landscape, points from the left):
//
//   Erf No ~30 | Portion ~72 | Category ~108 | Address ~160 (header 227) | Extent (numbers right-
//   aligned to ~421, header 363) | Value (right-aligned to ~495, header 446) | Other Particulars
//   (text from ~499, header 622)
//
//   * Thousands are separated by a SPACE: "2 793.2484 Ha", "30 575 000". Split on whitespace and
//     every number over 999 breaks.
//   * Header positions do NOT bound the data. The first digit group of "2 793.2484" sits LEFT of
//     the Extent header, and "Including :- Paarl 5, …" starts 120 pt left of its header. So numbers
//     are found by their unit ("m²" / "Ha") and joined only while the digit groups sit a normal
//     space apart; the header row is used only to check the layout and correct a sideways shift.
//   * Extents are in m² or hectares ("146.5753 Ha").
//   * Consolidated properties: "5*" carries the value of the group ("Including :- Paarl 5, Paarl 7,
//     Paarl 9"); the members appear again with value 0 and "See :- Paarl 5*". A member's value is
//     the head's, not zero, and the same erf number legitimately appears twice.
//   * The address is "street number" last, 0 when there is none: "Irene 0", "Retiefstraat 150".
//
// The date of valuation is printed on each book's cover ("Date of valuation : 2024/07/01") and is
// read from there, never assumed.
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PropertyData.RollBooks
{
    public sealed record RollBookRow
    {
        public required int Erf { get; init; }
        public int Portion { get; init; }

        /// <summary>"5*": this row carries the value of a consolidated group.</summary>
        public bool IsGroupHead { get; init; }

        /// <summary>For a group member ("See :- Paarl 5*"), the head's erf number.</summary>
        public int? GroupHeadErf { get; init; }

        /// <summary>
        /// "Note :- See SS La Domaine Two", "See S.O. Hof", "See Lemoenkloof Body Corporate": the erf
        /// is valued under a sectional-title scheme or share block, whose units are in the separate
        /// sectional book, so the erf's own value is 0 by design. Holds the scheme's name.
        /// </summary>
        public string? ValuedUnder { get; init; }

        public string? Category { get; init; }
        public string? Address { get; init; }
        public double? ExtentM2 { get; init; }
        public decimal? ValueZar { get; init; }
        public string? Particulars { get; init; }
        public int Page { get; init; }
    }

    public sealed record RollBookResult(
        string? Area,
        DateOnly? DateOfValuation,
        string? Heading,
        int Pages,
        IReadOnlyList<RollBookRow> Rows,
        IReadOnlyList<string> Rejected,
        RollBookSanity Sanity);

    /// <summary>What says a parse is right, not just that it returned rows.</summary>
    public sealed record RollBookSanity(
        int Rows,
        int Rejected,
        int WithValue,
        int GroupMembers,
        int SuspiciousExtents,
        int SuspiciousValues,
        int Duplicates,
        decimal? MedianResidentialValue,
        double? MedianResidentialExtent,
        IReadOnlyList<string> Warnings)
    {
        public bool LooksSane => Warnings.Count == 0;
    }

    public static class RollBookParser
    {
        // Where the PenSoft layout puts things, relative to the "Category" header (x ≈ 108).
        private const double CategoryHeaderX = 108;
        private const double PortionFrom = 66, CategoryFrom = 98, AddressFrom = 155;
        private const double ExtentFrom = 300, ValueFrom = 425, ValueTo = 500;

        /// <summary>Digit groups of one number are a normal space apart (≈3 pt); columns are further.</summary>
        private const double MaxGroupGap = 6;

        private static readonly Regex DigitGroup = new(@"^\d{1,3}$");
        private static readonly Regex Decimal = new(@"^[\d.]+$");
        private static readonly Regex ErfToken = new(@"^(\d+)(\*)?$");
        private static readonly Regex SeeHead = new(@"See\s*:-\s*.*?\b(\d+)\*", RegexOptions.IgnoreCase);
        private static readonly Regex Scheme = new(@"Note\s*:-\s*(?:Se+\s+)?(?:SS\s+|S\.O\.\s+)?(.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex ValuationDate = new(@"Date of valuation\s*:\s*(\d{4})/(\d{2})/(\d{2})", RegexOptions.IgnoreCase);
        private static readonly Regex AreaLine = new(@"Geographical Area\s*:\s*(.+)$", RegexOptions.IgnoreCase);

        public static RollBookResult Parse(Stream pdf)
        {
            using var doc = PdfDocument.Open(pdf);
            var rows = new List<RollBookRow>();
            var rejected = new List<string>();
            string? area = null, heading = null;
            DateOnly? valuationDate = null;

            foreach (var page in doc.GetPages())
            {
                var lines = Lines(page.GetWords());
                if (page.Number == 1)
                {
                    var text = string.Join("\n", lines.Select(l => string.Join(' ', l.Select(w => w.Text))));
                    heading = lines.Count == 0 ? null : string.Join(' ', lines.Take(4).SelectMany(l => l).Select(w => w.Text));
                    if (ValuationDate.Match(text) is { Success: true } m)
                        valuationDate = new DateOnly(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                }

                // A roll page has the column header; the cover, the category key and the valuer's
                // certificate do not.
                var header = lines.FirstOrDefault(l => l.Any(w => w.Text == "Category") && l.Any(w => w.Text == "Extent"));
                if (header is null) continue;
                var dx = header.First(w => w.Text == "Category").BoundingBox.Left - CategoryHeaderX;

                foreach (var line in lines)
                {
                    var joined = string.Join(' ', line.Select(w => w.Text));
                    if (AreaLine.Match(joined) is { Success: true } a) { area ??= a.Groups[1].Value.Trim(); continue; }
                    if (line.Count == 0 || line[0].BoundingBox.Left - dx >= PortionFrom) continue;   // header, footer, wrapped text
                    if (!ErfToken.IsMatch(line[0].Text)) continue;                                     // "Erf No", page furniture

                    var row = ParseRow(line, dx, page.Number);
                    if (row is null) rejected.Add($"p{page.Number}: {joined}");
                    else rows.Add(row);
                }
            }

            return new RollBookResult(area, valuationDate, heading, doc.NumberOfPages, rows, rejected, Check(rows, rejected));
        }

        /// <summary>
        /// Words on one row, left to right; rows top to bottom. The particulars are set in another
        /// font whose baseline sits a fraction off the row's, so rows are clustered with a
        /// tolerance rather than bucketed (bucketing split "Including :- …" onto a row of its own).
        /// </summary>
        private static List<List<Word>> Lines(IEnumerable<Word> words)
        {
            var lines = new List<List<Word>>();
            double? current = null;
            foreach (var w in words.OrderByDescending(w => w.BoundingBox.Bottom))
            {
                if (current is null || current.Value - w.BoundingBox.Bottom > 3.0)
                {
                    lines.Add([]);
                    current = w.BoundingBox.Bottom;
                }
                lines[^1].Add(w);
            }
            return lines.Select(l => l.OrderBy(w => w.BoundingBox.Left).ToList()).ToList();
        }

        public static RollBookRow? ParseRow(IReadOnlyList<Word> line, double dx, int page)
        {
            double X(Word w) => w.BoundingBox.Left - dx;

            var erf = ErfToken.Match(line[0].Text);
            if (!erf.Success) return null;

            // The unit anchors the extent; everything else is placed relative to it.
            var unit = -1;
            for (var i = 1; i < line.Count; i++)
            {
                var t = line[i].Text;
                if ((t is "m²" or "m2" || t.Equals("Ha", StringComparison.OrdinalIgnoreCase)) && X(line[i]) >= ExtentFrom)
                {
                    unit = i;
                    break;
                }
            }
            if (unit < 2) return null;

            // Extent: the digit groups straight before the unit, a normal space apart.
            var extentStart = unit;
            while (extentStart - 1 >= 1 && Decimal.IsMatch(line[extentStart - 1].Text) && X(line[extentStart - 1]) >= ExtentFrom - 60
                   && Gap(line[extentStart - 1], line[extentStart]) <= MaxGroupGap)
                extentStart--;
            if (extentStart == unit) return null;
            var extent = Number(line.Skip(extentStart).Take(unit - extentStart).Select(w => w.Text));
            if (extent is null) return null;
            if (line[unit].Text.Equals("Ha", StringComparison.OrdinalIgnoreCase)) extent *= 10_000;

            // Value: the digit groups after the unit, within the value column, a normal space apart.
            var valueEnd = unit + 1;
            while (valueEnd < line.Count && DigitGroup.IsMatch(line[valueEnd].Text)
                   && X(line[valueEnd]) >= ValueFrom && line[valueEnd].BoundingBox.Right - dx <= ValueTo
                   && (valueEnd == unit + 1 || Gap(line[valueEnd - 1], line[valueEnd]) <= MaxGroupGap))
                valueEnd++;
            decimal? value = valueEnd > unit + 1
                ? (decimal?)Number(line.Skip(unit + 1).Take(valueEnd - unit - 1).Select(w => w.Text))
                : null;

            string? Join(IEnumerable<Word> ws)
            {
                var s = string.Join(' ', ws.Select(w => w.Text)).Trim();
                return s.Length == 0 ? null : s;
            }

            var middle = line.Skip(1).Take(extentStart - 1).ToList();
            var portion = middle.Where(w => X(w) < CategoryFrom).ToList();
            var category = middle.Where(w => X(w) >= CategoryFrom && X(w) < AddressFrom).ToList();
            var address = middle.Where(w => X(w) >= AddressFrom).ToList();
            var particulars = Join(line.Skip(valueEnd));

            var head = particulars is null ? null : SeeHead.Match(particulars);
            var scheme = particulars is null ? null : Scheme.Match(particulars);
            return new RollBookRow
            {
                Erf = int.Parse(erf.Groups[1].Value, CultureInfo.InvariantCulture),
                IsGroupHead = erf.Groups[2].Success,
                Portion = int.TryParse(Join(portion), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0,
                GroupHeadErf = head is { Success: true } ? int.Parse(head.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                ValuedUnder = scheme is { Success: true } ? scheme.Groups[1].Value.Trim() : null,
                Category = Join(category),
                Address = TidyAddress(Join(address)),
                ExtentM2 = Math.Round(extent.Value, 1),
                ValueZar = value,
                Particulars = particulars,
                Page = page,
            };
        }

        private static double Gap(Word left, Word right) => right.BoundingBox.Left - left.BoundingBox.Right;

        private static double? Number(IEnumerable<string> groups) =>
            double.TryParse(string.Concat(groups), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;

        /// <summary>"Retiefstraat 150" → "150 Retiefstraat"; "Irene 0" → "Irene" (no street number).</summary>
        public static string? TidyAddress(string? address)
        {
            if (address is null) return null;
            var m = Regex.Match(address, @"^(.*?)\s+(\d+[A-Za-z]?(?:-\d+[A-Za-z]?)?)$");
            if (!m.Success) return address;
            return m.Groups[2].Value == "0" ? m.Groups[1].Value : $"{m.Groups[2].Value} {m.Groups[1].Value}";
        }

        private static RollBookSanity Check(IReadOnlyList<RollBookRow> rows, IReadOnlyList<string> rejected)
        {
            var warnings = new List<string>();
            var members = rows.Count(r => r.GroupHeadErf is not null || r.ValuedUnder is not null);
            var withValue = rows.Count(r => r.ValueZar is > 0);
            // A group member carries 0 by design; anything else under R1 000 is odd (R1 000 itself is
            // the usual nominal value for open space).
            var suspiciousValues = rows.Count(r => r.GroupHeadErf is null && r.ValuedUnder is null && r.ValueZar is null or < 1_000m);
            var suspiciousExtents = rows.Count(r => r.ExtentM2 is < 5 or > 50_000_000);
            var duplicates = rows.GroupBy(r => (r.Erf, r.Portion, r.IsGroupHead)).Count(g => g.Count() > 1);

            var residential = rows.Where(r => r.Category == "RES" && r.ValueZar is > 0).ToList();
            decimal? medianValue = residential.Count == 0 ? null : residential.Select(r => r.ValueZar!.Value).Order().ElementAt(residential.Count / 2);
            double? medianExtent = residential.Count == 0 ? null : residential.Select(r => r.ExtentM2 ?? 0).Order().ElementAt(residential.Count / 2);

            if (rows.Count == 0) warnings.Add("no rows: not a PenSoft roll book, or the layout has changed");
            if (rejected.Count > rows.Count * 0.02) warnings.Add($"{rejected.Count} lines rejected against {rows.Count} read");
            if (suspiciousExtents > rows.Count * 0.01) warnings.Add($"{suspiciousExtents} implausible extents (a mis-joined thousands separator?)");
            if (suspiciousValues > rows.Count * 0.05) warnings.Add($"{suspiciousValues} rows without a plausible value");
            if (duplicates > rows.Count * 0.01) warnings.Add($"{duplicates} duplicate erf/portion rows");
            if (medianExtent is < 150 or > 5_000) warnings.Add($"median residential erf {medianExtent} m² is implausible");

            return new RollBookSanity(rows.Count, rejected.Count, withValue, members, suspiciousExtents,
                suspiciousValues, duplicates, medianValue, medianExtent, warnings);
        }
    }
}
