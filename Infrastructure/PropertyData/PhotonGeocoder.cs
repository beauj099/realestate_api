using System.Globalization;
using System.Text.Json;

namespace PropertyData.Geocoding;

/// <summary>
/// A place from Photon (photon.komoot.io): OpenStreetMap search built for type-ahead, so unlike
/// Nominatim it may be asked on every (debounced) keystroke and matches half-typed words.
/// </summary>
public sealed record PhotonPlace(
    PhotonKind Kind,
    string Name,
    string? HouseNumber,
    string? Street,
    string? Locality,
    string? District,
    string? City,
    string? County,
    string? State,
    string? Postcode,
    double Lat,
    double Lng)
{
    /// <summary>
    /// The suburb as an agent writes it: the OSM locality, else a district that is not a
    /// municipal ward ("Cape Town Ward 83" names nothing a buyer knows), else the town.
    /// </summary>
    public string Suburb => Locality ?? (IsWard(District) ? null : District) ?? TownName ?? "";

    /// <summary>
    /// The town or city: the metro's everyday name ("City of Cape Town" → "Cape Town"), else
    /// Photon's city with "Local Municipality" dropped.
    /// </summary>
    public string CityName => MetroName(County) ?? TownName ?? Trim(County) ?? "";

    private string? TownName => Trim(City);

    private static bool IsWard(string? district) =>
        district is not null && district.Contains(" Ward ", StringComparison.OrdinalIgnoreCase);

    private static string? Trim(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var tail in new[] { " Local Municipality", " Metropolitan Municipality", " District Municipality" })
            if (name.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) name = name[..^tail.Length];
        return name.StartsWith("City of ", StringComparison.OrdinalIgnoreCase) ? name[8..] : name;
    }

    private static readonly Dictionary<string, string> Metros = new(StringComparer.OrdinalIgnoreCase)
    {
        ["City of Cape Town"] = "Cape Town",
        ["City of Johannesburg Metropolitan Municipality"] = "Johannesburg",
        ["City of Tshwane Metropolitan Municipality"] = "Pretoria",
        ["eThekwini Metropolitan Municipality"] = "Durban",
        ["Nelson Mandela Bay Metropolitan Municipality"] = "Gqeberha",
        ["Buffalo City Metropolitan Municipality"] = "East London",
        ["City of Ekurhuleni Metropolitan Municipality"] = "Ekurhuleni",
        ["Mangaung Metropolitan Municipality"] = "Bloemfontein",
    };

    private static string? MetroName(string? county) =>
        county is not null && Metros.TryGetValue(county, out var name) ? name : null;
}

/// <summary>An <see cref="Estate"/> is a named residential area: how OSM maps complexes and estates.</summary>
public enum PhotonKind { House, Street, Area, Estate }

/// <summary>
/// Asks the public Photon instance. Its terms ask for fair use: callers debounce, cache, and
/// only send queries of three or more characters. Results are limited to South Africa.
/// </summary>
public sealed class PhotonClient(HttpClient http)
{
    public const string BaseUrl = "https://photon.komoot.io/api/";

    /// <summary>South Africa's bounding box (west, south, east, north).</summary>
    private const string SouthAfrica = "16.3,-35.0,33.0,-22.1";

    private static readonly string[] Layers = ["house", "street", "locality", "district", "city"];

    /// <summary>Places that are an area a property is in. Farms, peaks and landmarks are not.</summary>
    private static readonly HashSet<string> AreaValues =
        ["city", "town", "village", "hamlet", "suburb", "neighbourhood", "quarter", "borough"];

    public async Task<List<PhotonPlace>> SearchAsync(string text, double? nearLat, double? nearLng,
        int limit, CancellationToken ct)
    {
        var query = new List<string>
        {
            "q=" + Uri.EscapeDataString(text),
            "limit=" + limit.ToString(CultureInfo.InvariantCulture),
            "bbox=" + SouthAfrica,
            "lang=en",
        };
        query.AddRange(Layers.Select(l => "layer=" + l));
        if (nearLat is not null && nearLng is not null)
        {
            query.Add("lat=" + nearLat.Value.ToString(CultureInfo.InvariantCulture));
            query.Add("lon=" + nearLng.Value.ToString(CultureInfo.InvariantCulture));
        }

        using var response = await http.GetAsync(BaseUrl + "?" + string.Join('&', query), ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Reads Photon's GeoJSON. Keeps South African streets, numbered houses and areas; a "house"
    /// without a number is a school, shop or landmark, not an address, and is dropped.
    /// </summary>
    public static List<PhotonPlace> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var places = new List<PhotonPlace>();
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = f.GetProperty("properties");
            string? S(string name) =>
                p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
                    ? s.Trim() : null;
            if (!string.Equals(S("countrycode"), "ZA", StringComparison.OrdinalIgnoreCase)) continue;

            var kind = S("type") switch
            {
                "house" when S("housenumber") is not null && S("street") is not null => PhotonKind.House,
                "street" => PhotonKind.Street,
                "locality" or "district" or "city" when S("osm_key") == "landuse" && S("osm_value") == "residential"
                    => PhotonKind.Estate,
                "locality" or "district" or "city" when AreaValues.Contains(S("osm_value") ?? "") => PhotonKind.Area,
                _ => (PhotonKind?)null,
            };
            if (kind is null) continue;

            var coords = f.GetProperty("geometry").GetProperty("coordinates");
            places.Add(new PhotonPlace(
                kind.Value,
                S("name") ?? S("street") ?? "",
                S("housenumber"),
                kind == PhotonKind.Street ? S("name") : S("street"),
                S("locality"), S("district"), S("city"), S("county"), S("state"), S("postcode"),
                Lat: coords[1].GetDouble(), Lng: coords[0].GetDouble()));
        }
        return places;
    }
}
