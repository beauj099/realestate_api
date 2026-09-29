using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RealEstateApi.Application.DTOs;

namespace RealEstateApi.Application.Services;

/// <summary>
/// What the agent typed in the address search: an optional unit ("Unit 5", "5/12"), an optional
/// street number and the rest (street, then perhaps the suburb or town).
/// </summary>
public sealed record TypedAddress(string? Unit, string? Number, string Rest, IReadOnlyList<string> Words)
{
    /// <summary>The text to look up, without the unit (no address source knows units).</summary>
    public string Lookup => Number is null ? Rest : $"{Number} {Rest}";
}

/// <summary>Reading and ranking address suggestions. Pure, so it is tested offline.</summary>
public static partial class AddressSearch
{
    public const string KindProperty = "property";   // a numbered erf from City records
    public const string KindAddress = "address";     // a numbered house from OpenStreetMap
    public const string KindStreet = "street";
    public const string KindArea = "area";
    public const string KindEstate = "estate";       // a complex or estate from OpenStreetMap

    /// <summary>A suburb, town, complex or estate: somewhere a property is, not an address.</summary>
    public static bool IsArea(AddressSuggestionDto s) => s.Kind is KindArea or KindEstate;

    /// <summary>At most this many suggestions are areas, so streets are never pushed off the list.</summary>
    public const int MaxAreas = 2;

    [GeneratedRegex(@"^(?:unit|flat|apartment|apt|door|section|no\.?|nr\.?|u)\s*#?\s*(\d+[a-z]?)\b[\s,]*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex UnitWord();

    [GeneratedRegex(@"^(\d+[a-z]?)\s*/\s*(\d+[a-z]?\b.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex UnitSlash();

    [GeneratedRegex(@"^(\d+[a-z]?)\b[\s,]*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingNumber();

    public static TypedAddress Parse(string text)
    {
        var rest = Regex.Replace(text.Trim(), @"\s+", " ");
        string? unit = null, number = null;

        if (UnitWord().Match(rest) is { Success: true } u)
        {
            unit = u.Groups[1].Value.ToUpperInvariant();
            rest = u.Groups[2].Value;
        }
        else if (UnitSlash().Match(rest) is { Success: true } s)
        {
            unit = s.Groups[1].Value.ToUpperInvariant();
            rest = s.Groups[2].Value;
        }
        if (LeadingNumber().Match(rest) is { Success: true } n && n.Groups[2].Value.Length > 0)
        {
            number = n.Groups[1].Value.ToUpperInvariant();
            rest = n.Groups[2].Value;
        }
        rest = rest.Trim(' ', ',');
        return new TypedAddress(unit, number, rest, Words(rest));
    }

    /// <summary>Lower-case words with accents and punctuation dropped: "Voëlklip Rd." → voelklip, rd.</summary>
    public static IReadOnlyList<string> Words(string text)
    {
        var plain = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Common street-type abbreviations, so "bosman str" matches "Bosman Street".</summary>
    private static readonly Dictionary<string, string> Abbreviations = new()
    {
        ["st"] = "street", ["str"] = "street", ["rd"] = "road", ["ave"] = "avenue", ["av"] = "avenue",
        ["dr"] = "drive", ["cres"] = "crescent", ["cr"] = "crescent", ["ln"] = "lane", ["cl"] = "close",
        ["pl"] = "place", ["blvd"] = "boulevard", ["hwy"] = "highway", ["ct"] = "court", ["sq"] = "square",
        ["wy"] = "way", ["ext"] = "extension", ["mt"] = "mount",
    };

    private static bool WordMatches(string typed, string word) =>
        word.StartsWith(typed, StringComparison.Ordinal)
        || (Abbreviations.TryGetValue(typed, out var full) && word.StartsWith(full, StringComparison.Ordinal));

    /// <summary>
    /// How well a suggestion fits what was typed; higher first. Streets and areas score on the
    /// same scale, so "bosm" lists Bosman Street and Bosmont together, by how well each matches,
    /// rather than all areas first. A typed street number pushes areas down: the agent is
    /// typing an address.
    /// </summary>
    public static double Rank(TypedAddress typed, AddressSuggestionDto s, double? nearLat, double? nearLng)
    {
        double score = s.Kind switch
        {
            KindProperty => 100,
            KindAddress => 96,
            _ => 80,   // streets and areas alike: Order alternates them
        };
        if (typed.Number is not null)
        {
            if (IsArea(s)) score -= 40;
            // The number typed is on record for this address.
            if (s.NumberVerified && string.Equals(s.StreetNumber, typed.Number, StringComparison.OrdinalIgnoreCase))
                score += 6;
        }

        // The name itself: "bosm" should start "Bosman Street", not only appear somewhere.
        var name = Words(IsArea(s) ? s.Title : s.StreetName);
        var all = Words($"{s.Title} {s.Subtitle}");
        if (typed.Words.Count > 0)
        {
            var first = typed.Words[0];
            if (name.Count > 0 && WordMatches(first, name[0])) score += 12;
            else if (name.Any(w => WordMatches(first, w))) score += 2;
            else score -= 20;
            // Every word typed ("bosman str strand") should be found somewhere in the suggestion:
            // a town typed at the end ("main road herm") is meant, not noise.
            score -= 30 * typed.Words.Count(t => !all.Any(w => WordMatches(t, w)));
            // The whole name typed ("heldervue") is exactly this place.
            if (typed.Words.SequenceEqual(name)) score += 4;
        }

        if (nearLat is not null && nearLng is not null && s.Lat is not null && s.Lng is not null)
        {
            var km = DistanceKm(nearLat.Value, nearLng.Value, s.Lat.Value, s.Lng.Value);
            score += km < 30 ? 8 : km < 150 ? 3 : 0;
        }
        return score;
    }

    /// <summary>Suggestions this far below the best are only there to fill the list; they are dropped.</summary>
    public const double MaxBehindBest = 30;

    /// <summary>
    /// Best first; one of each address; no more than <see cref="MaxAreas"/> areas. Streets and areas
    /// that match about as well alternate (street, area, street, …) instead of one kind crowding
    /// out the other; an area only leads when it is clearly the better match.
    /// </summary>
    public static List<AddressSuggestionDto> Order(IEnumerable<AddressSuggestionDto> suggestions, int limit)
    {
        var seen = new HashSet<string>();
        var unique = suggestions
            .OrderByDescending(s => s.Rank).ThenBy(s => s.Title, StringComparer.Ordinal)
            .Where(s => seen.Add(DedupeKey(s)))
            .ToList();
        if (unique.Count == 0) return [];
        var best = unique[0].Rank;
        var places = new Queue<AddressSuggestionDto>(unique.Where(s => !IsArea(s) && s.Rank >= best - MaxBehindBest));
        var areas = new Queue<AddressSuggestionDto>(unique.Where(s => IsArea(s) && s.Rank >= best - MaxBehindBest)
            .Take(MaxAreas));

        const double Clearly = 3;
        var list = new List<AddressSuggestionDto>();
        var lastWasArea = true;   // so a tie at the top goes to the street
        while (list.Count < limit && (places.Count > 0 || areas.Count > 0))
        {
            bool takeArea;
            if (places.Count == 0) takeArea = true;
            else if (areas.Count == 0) takeArea = false;
            else
            {
                var gap = areas.Peek().Rank - places.Peek().Rank;
                takeArea = gap > Clearly || (gap > -Clearly && !lastWasArea);
            }
            list.Add(takeArea ? areas.Dequeue() : places.Dequeue());
            lastWasArea = takeArea;
        }
        return list;
    }

    /// <summary>
    /// The same address or place, whichever source it came from: "10 Bosman Street, Strand" from
    /// City records and from OpenStreetMap is one suggestion (the better ranked one is kept).
    /// </summary>
    public static string DedupeKey(AddressSuggestionDto s) =>
        string.Join(' ', Words(IsArea(s)
            ? $"area {s.Title} {s.Province}"      // Sandton the town and Sandton the suburb
            : $"place {s.Title} {s.Suburb}"));

    /// <summary>
    /// The typed text with street-type abbreviations written out ("florida rd" → "florida road"),
    /// for OpenStreetMap search, which matches whole names. A half-typed last word is left alone.
    /// </summary>
    public static string Expanded(TypedAddress typed)
    {
        var words = typed.Rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Abbreviations.TryGetValue(w.TrimEnd('.', ',').ToLowerInvariant(), out var full) ? full : w);
        var rest = string.Join(' ', words);
        return typed.Number is null ? rest : $"{typed.Number} {rest}";
    }

    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    public static string TitleCase(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    /// <summary>"Unit 5, 12 Main Road" / "12 Main Road" / "Main Road".</summary>
    public static string AddressTitle(string? unit, string? number, string street) =>
        (unit is null ? "" : $"Unit {unit}, ") + (number is null ? "" : number + " ") + street;
}
