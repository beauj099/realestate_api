using Microsoft.Extensions.Caching.Memory;
using PropertyData.Geocoding;
using RealEstateApi.Application.DTOs;
using CitySuggestion = PropertyData.CapeTown.Clients.AddressSuggestion;

namespace RealEstateApi.Application.Services;

/// <summary>
/// The address search on the Address screen: one search over all of South Africa, in two parts
/// the app asks for at once and merges as they arrive.
///
/// <list type="bullet">
/// <item><see cref="CityAsync"/>: Cape Town's and Johannesburg's parcel records. Fast (well under
/// a second); every numbered result is a real erf with its location.</item>
/// <item><see cref="NationalAsync"/>: Photon (OpenStreetMap) for everywhere, streets, numbered
/// houses where OSM has them, and suburbs and towns. Slower (three to seven seconds).</item>
/// </list>
///
/// Both read a typed unit ("Unit 5, 12 Main Road", "5/12 Main Road") off the front and hand it
/// back on every suggestion: no free source knows the units in a complex.
/// </summary>
public class AddressSearchService(
    PropertyData.CapeTown.Clients.CapeTownSpatialClient capeTown,
    PropertyData.Johannesburg.JohannesburgPropertyProvider johannesburg,
    PhotonClient photon,
    IMemoryCache cache,
    ILogger<AddressSearchService> log)
{
    public const int Limit = 8;
    private const string CitySource = "City records";
    private const string OsmSource = "OpenStreetMap";

    public async Task<IReadOnlyList<AddressSuggestionDto>> CityAsync(string query, double? lat, double? lng,
        CancellationToken ct)
    {
        var typed = AddressSearch.Parse(query);
        if (typed.Rest.Length < 3) return [];
        var key = $"suggest:city:{typed.Lookup.ToUpperInvariant()}";
        if (!cache.TryGetValue(key, out List<AddressSuggestionDto>? found) || found is null)
        {
            // Every source at once; one that is down just contributes nothing.
            async Task<List<T>> Safe<T>(Func<Task<List<T>>> call)
            {
                try { return await call(); }
                catch (HttpRequestException ex) { log.LogWarning(ex, "Suggestion source did not answer"); return []; }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return []; }
            }
            var ctStreets = Safe(() => capeTown.SuggestAsync(typed.Lookup, Limit, ct));
            var jhbStreets = Safe(() => johannesburg.SuggestAsync(typed.Lookup, Limit, ct));
            var ctAreas = Safe(() => typed.Number is null ? capeTown.SuggestSuburbsAsync(typed.Rest, 3, ct) : Task.FromResult(new List<string>()));
            var jhbAreas = Safe(() => typed.Number is null ? johannesburg.SuggestSuburbsAsync(typed.Rest, 3, ct) : Task.FromResult(new List<string>()));
            await Task.WhenAll(ctStreets, jhbStreets, ctAreas, jhbAreas);

            found =
            [
                .. ctStreets.Result.Select(s => FromCity(s, PropertyReportService.CapeTown, "Cape Town", "Western Cape")),
                .. jhbStreets.Result.Select(s => FromCity(s, PropertyReportService.Johannesburg, "Johannesburg", "Gauteng")),
                .. ctAreas.Result.Select(a => CityArea(a, PropertyReportService.CapeTown, "Cape Town", "Western Cape")),
                .. jhbAreas.Result.Select(a => CityArea(a, PropertyReportService.Johannesburg, "Johannesburg", "Gauteng")),
            ];
            cache.Set(key, found, TimeSpan.FromHours(1));
        }
        return Finish(typed, found, lat, lng);
    }

    public async Task<IReadOnlyList<AddressSuggestionDto>> NationalAsync(string query, double? lat, double? lng,
        CancellationToken ct)
    {
        var typed = AddressSearch.Parse(query);
        if (typed.Rest.Length < 3) return [];
        // Near the same spot counts as the same search (about 10 km).
        var near = lat is null || lng is null ? "" : $"{Math.Round(lat.Value, 1)},{Math.Round(lng.Value, 1)}";
        var key = $"suggest:osm:{typed.Lookup.ToUpperInvariant()}:{near}";
        if (!cache.TryGetValue(key, out List<AddressSuggestionDto>? found) || found is null)
        {
            try
            {
                var places = await photon.SearchAsync(AddressSearch.Expanded(typed), lat, lng, 12, ct);
                found = places.Select(p => FromOsm(p, typed.Number)).ToList();
                cache.Set(key, found, TimeSpan.FromHours(24));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Photon did not answer");
                return [];
            }
        }
        return Finish(typed, found, lat, lng);
    }

    /// <summary>The unit and number typed go onto each suggestion; then rank, dedupe and trim.</summary>
    private static List<AddressSuggestionDto> Finish(TypedAddress typed, IEnumerable<AddressSuggestionDto> found,
        double? lat, double? lng)
    {
        var withUnit = found.Select(s =>
        {
            if (AddressSearch.IsArea(s)) return s;
            var title = AddressSearch.AddressTitle(typed.Unit, s.StreetNumber, s.StreetName);
            return s with { Unit = typed.Unit, Title = title, Label = $"{title}, {s.Suburb}" };
        });
        return AddressSearch.Order(withUnit.Select(s => s with
        {
            Rank = AddressSearch.Rank(typed, s, lat, lng),
            Key = AddressSearch.DedupeKey(s),
        }), Limit);
    }

    private static AddressSuggestionDto FromCity(CitySuggestion s, string municipality, string city, string province)
    {
        var street = AddressSearch.TitleCase(string.Join(' ',
            new[] { s.StreetName, s.StreetType }.Where(p => !string.IsNullOrWhiteSpace(p))));
        var number = s.StreetNumber is null ? null : $"{s.StreetNumber}{s.StreetNumberSuffix}";
        var suburb = AddressSearch.TitleCase(s.Suburb);
        var numbered = number is not null && s.Erf is not null;
        return new AddressSuggestionDto(
            Label: "", StreetNumber: number, StreetName: street, Suburb: suburb,
            City: city, Province: province, Country: "South Africa",
            Erf: s.Erf, Sg26: s.Sg26, Lat: s.Location?.Lat, Lng: s.Location?.Lng,
            Municipality: municipality,
            Kind: numbered ? AddressSearch.KindProperty : AddressSearch.KindStreet,
            Subtitle: Places(suburb, city),
            NumberVerified: numbered,
            Source: CitySource);
    }

    private static AddressSuggestionDto CityArea(string name, string municipality, string city, string province)
    {
        var suburb = AddressSearch.TitleCase(name);
        return new AddressSuggestionDto(
            Label: $"{suburb}, {city}", StreetNumber: null, StreetName: "", Suburb: suburb,
            City: city, Province: province, Country: "South Africa",
            Erf: null, Sg26: null, Lat: null, Lng: null, Municipality: municipality,
            Kind: AddressSearch.KindArea, Title: suburb, Subtitle: Places(city, province), Source: CitySource);
    }

    /// <summary>
    /// A street carries the number the agent typed (not on record, so no location is given for
    /// it: a point on the street is not the property). A numbered house has its own location.
    /// </summary>
    private static AddressSuggestionDto FromOsm(PhotonPlace p, string? typedNumber)
    {
        var city = p.CityName;
        if (p.Kind == PhotonKind.Estate)
        {
            // A complex or estate: the agent adds the street address and unit.
            var around = p.Suburb;
            return new AddressSuggestionDto(
                Label: $"{p.Name}, {around}", StreetNumber: null, StreetName: "",
                Suburb: around, City: city, Province: p.State ?? "", Country: "South Africa",
                Erf: null, Sg26: null, Lat: p.Lat, Lng: p.Lng, Municipality: "",
                Kind: AddressSearch.KindEstate, Title: p.Name,
                Subtitle: "Complex or estate · " + Places(around, city),
                PostalCode: p.Postcode, Source: OsmSource);
        }
        if (p.Kind == PhotonKind.Area)
        {
            var isTown = string.Equals(p.Name, city, StringComparison.OrdinalIgnoreCase) || p.City is null;
            return new AddressSuggestionDto(
                Label: $"{p.Name}, {city}", StreetNumber: null, StreetName: "",
                Suburb: isTown ? "" : p.Name, City: isTown ? p.Name : city,
                Province: p.State ?? "", Country: "South Africa",
                Erf: null, Sg26: null, Lat: p.Lat, Lng: p.Lng, Municipality: "",
                Kind: AddressSearch.KindArea, Title: p.Name,
                Subtitle: Places(isTown ? "" : city, p.State ?? ""),
                PostalCode: p.Postcode, Source: OsmSource);
        }

        var house = p.Kind == PhotonKind.House;
        var suburb = p.Suburb;
        return new AddressSuggestionDto(
            Label: "", StreetNumber: house ? p.HouseNumber : typedNumber, StreetName: p.Street ?? p.Name,
            Suburb: suburb, City: city, Province: p.State ?? "", Country: "South Africa",
            Erf: null, Sg26: null,
            Lat: house ? p.Lat : null, Lng: house ? p.Lng : null, Municipality: "",
            Kind: house ? AddressSearch.KindAddress : AddressSearch.KindStreet,
            Subtitle: Places(suburb, city),
            NumberVerified: house,
            PostalCode: p.Postcode, Source: OsmSource);
    }

    /// <summary>"Strand, Cape Town"; a place named twice ("Sandton, Sandton") once.</summary>
    private static string Places(params string[] names) =>
        string.Join(", ", names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase));
}
