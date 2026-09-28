using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace RealEstateApi.Application.Services;

public record ClimateDto(double MeanC, double AvgMaxC, double AvgMinC, double AnnualRainMm, string HottestMonth,
    double HottestAvgMaxC, string ColdestMonth, double ColdestAvgMinC, string Years, string Source)
{
    public double? HumidityPct { get; init; }

    /// <summary>Month by month (January first): average high and low, and rain in an average year.</summary>
    public IReadOnlyList<ClimateMonthDto>? Months { get; init; }

    /// <summary>Days a year with at least 1 mm of rain, and with a high over 30 °C.</summary>
    public int? RainDaysPerYear { get; init; }
    public int? HotDaysPerYear { get; init; }

    /// <summary>Sunshine on flat ground, kWh/m² a day (what a solar panel has to work with).</summary>
    public double? SolarKwhM2Day { get; init; }

    /// <summary>Average wind at 2 m, m/s.</summary>
    public double? WindMs { get; init; }
}

public record ClimateMonthDto(int Month, double AvgMaxC, double AvgMinC, double RainMm);

public record PopulationDto(string? MainPlace, string? SubPlace, int? Population, int? Households, double? AreaKm2,
    double? PeoplePerKm2, string Municipality, int? MunicipalityPopulation, string Year, string Source)
{
    /// <summary>A more recent estimate for the same sub place (WorldPop), when available.</summary>
    public int? EstimatedPopulation { get; init; }
    public string? EstimateYear { get; init; }
    public string? EstimateSource { get; init; }
}

public record IncomeBandDto(string Label, double Percent);

public record IncomeDto(string Municipality, IReadOnlyList<IncomeBandDto> Bands, string MedianBand, string Year, string Source);

public record CrimeCountDto(string Crime, int Count, int PreviousCount);

public record CrimeDto(string Precinct, int? Population, string Period, string PreviousPeriod,
    IReadOnlyList<CrimeCountDto> Crimes, int Total, int PreviousTotal, double? TotalPer100k, string? Band, string Source);

/// <summary>
/// "Area details" for a report, each from its own free public source and each optional: a
/// source being down leaves its block out, never the report.
/// </summary>
public record AreaDetailsDto(ClimateDto? Climate, PopulationDto? Population, IncomeDto? Income, CrimeDto? Crime);

/// <summary>
/// Climate (NASA POWER, public domain), population (Census 2011 by sub place, via Adrian Frith's
/// census API; Stats SA), household income (Census 2011 by municipality, Stats SA's dissemination
/// API) and crime (SAPS figures per police precinct, <c>Data/crime-stats.json</c>, built by
/// tools/BuildCrimeStats). Results are cached for a month per ~100 m.
/// </summary>
public class AreaDetailsService(IHttpClientFactory httpFactory, IMemoryCache cache, IWebHostEnvironment env,
    ILogger<AreaDetailsService> log)
{
    public const string HttpClientName = "area";
    private const string NasaPower = "https://power.larc.nasa.gov/api/temporal/daily/point";
    private const string CensusApi = "https://census-api.frith.dev/graphql";
    private const string StatsSaApi = "https://disseminationapi-a2f6fff8f7a3f3ff.z01.azurefd.net/api";
    private const string SubPlaces =
        "https://services3.arcgis.com/GMycIhSIBQnnjV35/arcgis/rest/services/Census_2011_Sub_Places_of_South_Africa/FeatureServer/0/query";
    private const string WorldPop = "https://api.worldpop.org/v1/services/stats";
    private const int WorldPopYear = 2020;
    private const string PoliceBoundaries =
        "https://services8.arcgis.com/oTalEaSXAuyNT7xf/arcgis/rest/services/SA_Police_Boundaries/FeatureServer/0/query";

    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    public async Task<AreaDetailsDto> GetAsync(double lat, double lng, CancellationToken ct)
    {
        var key = $"{Math.Round(lat, 3).ToString(CultureInfo.InvariantCulture)},{Math.Round(lng, 3).ToString(CultureInfo.InvariantCulture)}";
        var climate = Cached($"area:climate:{Math.Round(lat, 1)},{Math.Round(lng, 1)}", () => ClimateAsync(lat, lng, ct));
        var census = Cached($"area:census:{key}", () => CensusAsync(lat, lng, ct));
        var crime = Cached($"area:crime:{key}", () => CrimeAsync(lat, lng, ct));
        await Task.WhenAll(climate, census, crime);

        var population = census.Result;
        var income = population?.MunicipalityCode is { } muni
            ? await Cached($"area:income:{muni}", () => IncomeAsync(muni, population.Dto.Municipality, ct))
            : null;
        return new AreaDetailsDto(climate.Result, population?.Dto, income, crime.Result);
    }

    private async Task<T?> Cached<T>(string key, Func<Task<T?>> load) where T : class
    {
        if (cache.TryGetValue(key, out T? hit)) return hit;
        try
        {
            var value = await load();
            if (value is not null) cache.Set(key, value, Keep);
            return value;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException
                                       or InvalidOperationException or FormatException)
        {
            log.LogWarning(ex, "Area details: {Key} unavailable", key);
            return null;
        }
    }

    private HttpClient Http() => httpFactory.CreateClient(HttpClientName);

    // ---- climate: NASA POWER daily, last ten full years --------------------------------------

    private async Task<ClimateDto?> ClimateAsync(double lat, double lng, CancellationToken ct)
    {
        var endYear = DateTime.UtcNow.Year - 1;
        var startYear = endYear - 9;
        var url = $"{NasaPower}?parameters=T2M,T2M_MAX,T2M_MIN,PRECTOTCORR,RH2M,ALLSKY_SFC_SW_DWN,WS2M&community=RE" +
                  $"&longitude={lng.ToString(CultureInfo.InvariantCulture)}&latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                  $"&start={startYear}0101&end={endYear}1231&format=JSON";
        using var doc = JsonDocument.Parse(await Http().GetStringAsync(url, ct));
        var p = doc.RootElement.GetProperty("properties").GetProperty("parameter");
        var humidity = Series(p, "RH2M");
        var sun = Series(p, "ALLSKY_SFC_SW_DWN");
        var wind = Series(p, "WS2M");
        return SummariseClimate(Series(p, "T2M"), Series(p, "T2M_MAX"), Series(p, "T2M_MIN"), Series(p, "PRECTOTCORR"),
            $"{startYear}–{endYear}") is { } climate
            ? climate with
            {
                HumidityPct = humidity.Count > 0 ? Math.Round(humidity.Values.Average()) : null,
                SolarKwhM2Day = sun.Count > 300 ? Math.Round(sun.Values.Average(), 1) : null,
                WindMs = wind.Count > 300 ? Math.Round(wind.Values.Average(), 1) : null,
            }
            : null;
    }

    private static Dictionary<DateOnly, double> Series(JsonElement parameters, string name) =>
        parameters.GetProperty(name).EnumerateObject()
            .Where(d => d.Value.GetDouble() > -900)   // -999 is POWER's "no data"
            .ToDictionary(d => DateOnly.ParseExact(d.Name, "yyyyMMdd", CultureInfo.InvariantCulture), d => d.Value.GetDouble());

    public static ClimateDto? SummariseClimate(Dictionary<DateOnly, double> mean, Dictionary<DateOnly, double> max,
        Dictionary<DateOnly, double> min, Dictionary<DateOnly, double> rain, string years)
    {
        if (mean.Count < 300 || rain.Count < 300) return null;
        var yearsCount = rain.Keys.Select(d => d.Year).Distinct().Count();
        var byMonthMax = max.GroupBy(d => d.Key.Month).ToDictionary(g => g.Key, g => g.Average(d => d.Value));
        var byMonthMin = min.GroupBy(d => d.Key.Month).ToDictionary(g => g.Key, g => g.Average(d => d.Value));
        var hottest = byMonthMax.MaxBy(m => m.Value);
        var coldest = byMonthMin.MinBy(m => m.Value);
        string Month(int m) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m);
        var rainByMonth = rain.GroupBy(d => d.Key.Month).ToDictionary(g => g.Key, g => g.Sum(d => d.Value) / yearsCount);
        return new ClimateDto(
            Math.Round(mean.Values.Average(), 1), Math.Round(max.Values.Average(), 1), Math.Round(min.Values.Average(), 1),
            Math.Round(rain.Values.Sum() / yearsCount), Month(hottest.Key), Math.Round(hottest.Value, 1),
            Month(coldest.Key), Math.Round(coldest.Value, 1), years,
            "NASA POWER daily climate data (MERRA-2)")
        {
            Months = Enumerable.Range(1, 12)
                .Where(m => byMonthMax.ContainsKey(m) && byMonthMin.ContainsKey(m))
                .Select(m => new ClimateMonthDto(m, Math.Round(byMonthMax[m], 1), Math.Round(byMonthMin[m], 1),
                    Math.Round(rainByMonth.GetValueOrDefault(m))))
                .ToList(),
            RainDaysPerYear = (int)Math.Round(rain.Count(d => d.Value >= 1) / (double)yearsCount),
            HotDaysPerYear = (int)Math.Round(max.Count(d => d.Value > 30) / (double)yearsCount),
        };
    }

    // ---- population: Census 2011 sub place and main place ------------------------------------

    private sealed record CensusResult(PopulationDto Dto, string? MunicipalityCode);

    private async Task<CensusResult?> CensusAsync(double lat, double lng, CancellationToken ct)
    {
        var query = new
        {
            query = "query($lat:Float!,$lng:Float!){places(coordinates:{latitude:$lat,longitude:$lng})" +
                    "{code name type{name} population households area}}",
            variables = new { lat, lng },
        };
        using var response = await Http().PostAsJsonAsync(CensusApi, query, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var places = doc.RootElement.GetProperty("data").GetProperty("places").EnumerateArray()
            .Select(p => (Type: p.GetProperty("type").GetProperty("name").GetString() ?? "",
                          Code: p.GetProperty("code").GetString() ?? "", Name: p.GetProperty("name").GetString() ?? "",
                          Population: p.TryGetProperty("population", out var pop) && pop.ValueKind == JsonValueKind.Number ? pop.GetInt32() : (int?)null,
                          Households: p.TryGetProperty("households", out var hh) && hh.ValueKind == JsonValueKind.Number ? hh.GetInt32() : (int?)null,
                          Area: p.TryGetProperty("area", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : (double?)null))
            .ToList();
        if (places.Count == 0) return null;

        var sub = places.FirstOrDefault(p => p.Type == "subplace");
        var main = places.FirstOrDefault(p => p.Type == "mainplace");
        var muni = places.FirstOrDefault(p => p.Type is "metro" or "local");
        var dto = new PopulationDto(
            main.Name, sub.Name, sub.Population, sub.Households,
            sub.Area is null ? null : Math.Round(sub.Area.Value, 2),
            sub.Population is { } people && sub.Area is > 0 ? Math.Round(people / sub.Area.Value) : null,
            muni.Name ?? "", muni.Population, "2011",
            "Statistics South Africa, Census 2011 (via census-api.frith.dev)");
        var estimate = await WorldPopEstimateAsync(lat, lng, ct);
        if (estimate is { } estimated)
            dto = dto with
            {
                EstimatedPopulation = estimated,
                EstimateYear = WorldPopYear.ToString(CultureInfo.InvariantCulture),
                EstimateSource = "WorldPop (CC BY 4.0), for the same sub place",
            };
        return new CensusResult(dto, string.IsNullOrEmpty(muni.Code) ? null : muni.Code);
    }

    /// <summary>
    /// WorldPop's estimate for the Census sub place containing the point: the sub place outline
    /// (Census 2011, UCT Libraries) sent to WorldPop's zonal statistics. Null on any failure.
    /// </summary>
    private async Task<int?> WorldPopEstimateAsync(double lat, double lng, CancellationToken ct)
    {
        try
        {
            var outline = await Http().GetStringAsync(
                $"{SubPlaces}?geometry={lng.ToString(CultureInfo.InvariantCulture)},{lat.ToString(CultureInfo.InvariantCulture)}" +
                "&geometryType=esriGeometryPoint&inSR=4326&spatialRel=esriSpatialRelIntersects&outFields=SP_CODE" +
                "&returnGeometry=true&outSR=4326&geometryPrecision=6&f=geojson", ct);
            using var geo = JsonDocument.Parse(outline);
            var features = geo.RootElement.GetProperty("features");
            if (features.GetArrayLength() == 0) return null;
            var collection = JsonSerializer.Serialize(new
            {
                type = "FeatureCollection",
                features = new[] { new { type = "Feature", properties = new { }, geometry = features[0].GetProperty("geometry") } },
            });
            var url = $"{WorldPop}?dataset=wpgppop&year={WorldPopYear}&runasync=false&geojson={Uri.EscapeDataString(collection)}";
            using var doc = JsonDocument.Parse(await Http().GetStringAsync(url, ct));
            return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                   && data.TryGetProperty("total_population", out var total) && total.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(total.GetDouble())
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException)
        {
            log.LogInformation(ex, "WorldPop estimate unavailable");
            return null;
        }
    }

    // ---- income: Census 2011 household income bands by municipality --------------------------

    private async Task<IncomeDto?> IncomeAsync(string municipalityCode, string municipality, CancellationToken ct)
    {
        var url = $"{StatsSaApi}/DsIncomeCategories/getDsIncomeCategoriesReport/2/3/{municipalityCode}.00000000";
        using var doc = JsonDocument.Parse(await Http().GetStringAsync(url, ct));
        var bands = doc.RootElement.EnumerateArray()
            .Select(b => new IncomeBandDto(Regex.Replace(b.GetProperty("label").GetString() ?? "", @"\s+", " ").Trim(),
                b.GetProperty("countsPercentage").GetDouble()))
            .Where(b => b.Label.Length > 0 && !b.Label.Contains("Unspecified", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (bands.Count == 0) return null;
        return new IncomeDto(municipality, bands, MedianBand(bands), "2011",
            "Statistics South Africa, Census 2011 annual household income");
    }

    /// <summary>The band that holds the middle household.</summary>
    public static string MedianBand(IReadOnlyList<IncomeBandDto> bands)
    {
        var total = bands.Sum(b => b.Percent);
        var running = 0.0;
        foreach (var b in bands)
        {
            running += b.Percent;
            if (running >= total / 2) return b.Label;
        }
        return bands[^1].Label;
    }

    // ---- crime: the precinct's SAPS figures ---------------------------------------------------

    private async Task<CrimeDto?> CrimeAsync(double lat, double lng, CancellationToken ct)
    {
        var url = $"{PoliceBoundaries}?geometry={lng.ToString(CultureInfo.InvariantCulture)},{lat.ToString(CultureInfo.InvariantCulture)}" +
                  "&geometryType=esriGeometryPoint&inSR=4326&spatialRel=esriSpatialRelIntersects&outFields=COMPNT_NM" +
                  "&returnGeometry=false&f=json";
        using var doc = JsonDocument.Parse(await Http().GetStringAsync(url, ct));
        var name = doc.RootElement.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("attributes").GetProperty("COMPNT_NM").GetString())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return name is null ? null : CrimeFor(name, CrimeData.Load(env.ContentRootPath));
    }

    public static CrimeDto? CrimeFor(string precinct, CrimeData data)
    {
        var station = data.Find(precinct);
        if (station is null) return null;
        var crimes = data.CrimeNames.Where(c => c.Key != data.TotalCode)
            .Select(c => station.Crimes.TryGetValue(c.Key, out var n) ? new CrimeCountDto(c.Value, n[0], n[1]) : null)
            .OfType<CrimeCountDto>()
            .ToList();
        var total = station.Crimes.TryGetValue(data.TotalCode, out var t) ? t : [0, 0];
        double? rate = station.Population >= 1000 ? Math.Round(total[0] * 100_000.0 / station.Population) : null;
        return new CrimeDto(station.Name, station.Population, data.Period, data.PreviousPeriod, crimes, total[0], total[1],
            rate, rate is { } r ? data.Band(r) : null, data.Source);
    }
}

/// <summary>Data/crime-stats.json, loaded once.</summary>
public sealed class CrimeData
{
    public required string Source { get; init; }
    public required string Period { get; init; }
    public required string PreviousPeriod { get; init; }
    public required Dictionary<string, string> CrimeNames { get; init; }
    public required string TotalCode { get; init; }
    public required double[] BandThresholdsPer100k { get; init; }
    public required List<Station> Stations { get; init; }

    public sealed class Station
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public int Population { get; init; }
        public double AreaKm2 { get; init; }
        public required Dictionary<string, int[]> Crimes { get; init; }
    }

    private static CrimeData? _loaded;

    public static CrimeData Load(string contentRoot) =>
        _loaded ??= JsonSerializer.Deserialize<CrimeData>(
            File.ReadAllText(Path.Combine(contentRoot, "Infrastructure", "PropertyData", "Data", "crime-stats.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("crime-stats.json is empty");

    /// <summary>The precinct by name: the boundaries say "STRAND", the figures "Strand".</summary>
    public Station? Find(string precinct)
    {
        static string Norm(string s) => Regex.Replace(s.ToUpperInvariant(), @"[^A-Z0-9]", "");
        var key = Norm(precinct);
        return Stations.FirstOrDefault(s => Norm(s.Name) == key)
               ?? Stations.FirstOrDefault(s => Norm(s.Name).StartsWith(key, StringComparison.Ordinal) || key.StartsWith(Norm(s.Name), StringComparison.Ordinal));
    }

    /// <summary>Against every precinct nationally: the lowest fifth is "Very low".</summary>
    public string Band(double per100k) => per100k switch
    {
        _ when per100k < BandThresholdsPer100k[0] => "Very low",
        _ when per100k < BandThresholdsPer100k[1] => "Low",
        _ when per100k < BandThresholdsPer100k[2] => "Moderate",
        _ when per100k < BandThresholdsPer100k[3] => "High",
        _ => "Very high",
    };
}
