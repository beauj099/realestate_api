// Builds Infrastructure/PropertyData/Data/crime-stats.json: per police precinct, the last two
// 12-month periods of the crimes a property report shows, with the precinct's Census 2022
// population, so the API needs no database for the "Crime" part of the area details.
//
//   dotnet run --project tools/BuildCrimeStats -- [--api-dir .]
//
// Source: SAPS crime statistics as cleaned and published by Adrian Frith (github.com/afrith/
// crime-stats; public domain, PDDL): crime-stats.csv (monthly counts per station and crime code,
// ~212 MB, streamed) and police_stations.csv (Stats SA 2025 police districts, Census 2022
// population). Re-run each quarter after the repository updates.

using System.Globalization;
using System.Text.Json;

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
var apiDir = Path.GetFullPath(Arg("--api-dir") ?? Directory.GetCurrentDirectory());
var output = Path.Combine(apiDir, "Infrastructure", "PropertyData", "Data", "crime-stats.json");

const string Repo = "https://media.githubusercontent.com/media/afrith/crime-stats/main";
const string RawRepo = "https://raw.githubusercontent.com/afrith/crime-stats/main";

// The crimes a property report shows, and the SAPS total of 17 community-reported serious crimes.
var shown = new Dictionary<string, string>
{
    ["21"] = "Residential burglary",
    ["20"] = "Non-residential burglary",
    ["38"] = "Residential robbery",
    ["34"] = "Carjacking",
    ["24"] = "Theft of motor vehicles",
    ["25"] = "Theft out of or from vehicles",
    ["Full 17"] = "Community-reported serious crime (total)",
};

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-BuildCrimeStats/1.0");

// Stations: code, name, municipality, population, area.
var stations = new Dictionary<string, (string Name, string Muni, string Prov, int Population, double AreaKm2)>();
var stationCsv = await http.GetStringAsync($"{RawRepo}/police_stations.csv");
foreach (var line in stationCsv.Split('\n').Skip(1).Where(l => l.Trim().Length > 0))
{
    var f = SplitCsv(line.TrimEnd('\r'));
    stations[f[0]] = (f[1], f[2], f[4], int.TryParse(f[5], out var p) ? p : 0,
        double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 0);
}
Console.WriteLine($"{stations.Count} police stations");

// Monthly counts, streamed.
var counts = new Dictionary<(string Station, string Code, int Month), int>();
var latest = 0;
using (var response = await http.GetAsync($"{Repo}/crime-stats.csv", HttpCompletionOption.ResponseHeadersRead))
{
    response.EnsureSuccessStatusCode();
    using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
    await reader.ReadLineAsync();   // header: province,station_code,station_name,crime_code,crime_name,year,month,crime_count
    string? line;
    long rows = 0;
    while ((line = await reader.ReadLineAsync()) is not null)
    {
        rows++;
        var f = SplitCsv(line);
        if (f.Count < 8 || !shown.ContainsKey(f[3])) continue;
        if (!int.TryParse(f[5], out var year) || !int.TryParse(f[6], out var month) || !int.TryParse(f[7], out var n)) continue;
        var key = year * 12 + (month - 1);
        latest = Math.Max(latest, key);
        counts[(f[1], f[3], key)] = n;
    }
    Console.WriteLine($"{rows:N0} rows read");
}

// Two 12-month windows ending at the latest month in the data.
int From(int k) => k - 11;
var lastEnd = latest;
var prevEnd = latest - 12;
string Label(int end) =>
    $"{new DateOnly(From(end) / 12, From(end) % 12 + 1, 1):MMM yyyy} – {new DateOnly(end / 12, end % 12 + 1, 1):MMM yyyy}";
int Sum(string station, string code, int end)
{
    var total = 0;
    for (var k = From(end); k <= end; k++) total += counts.GetValueOrDefault((station, code, k));
    return total;
}

var stationRows = stations.Select(s =>
{
    var crimes = shown.Keys.ToDictionary(code => code, code => new[] { Sum(s.Key, code, lastEnd), Sum(s.Key, code, prevEnd) });
    return new
    {
        code = s.Key,
        name = s.Value.Name,
        muni = s.Value.Muni,
        province = s.Value.Prov,
        population = s.Value.Population,
        areaKm2 = s.Value.AreaKm2,
        crimes,
    };
}).ToList();

// National bands for the total per 100 000 people (precincts of 1 000 people or more).
var rates = stationRows.Where(s => s.population >= 1000)
    .Select(s => s.crimes["Full 17"][0] * 100_000.0 / s.population)
    .Order().ToList();
double Q(double q) => rates[(int)Math.Round(q * (rates.Count - 1))];
var bands = new[] { Q(0.2), Q(0.4), Q(0.6), Q(0.8) }.Select(v => Math.Round(v)).ToArray();

var json = new
{
    source = "SAPS crime statistics (cleaned by Adrian Frith, github.com/afrith/crime-stats); Stats SA police districts, Census 2022 population",
    period = Label(lastEnd),
    previousPeriod = Label(prevEnd),
    crimeNames = shown,
    totalCode = "Full 17",
    bandThresholdsPer100k = bands,   // Very low < b0 ≤ Low < b1 ≤ Moderate < b2 ≤ High < b3 ≤ Very high
    stations = stationRows,
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(json));
Console.WriteLine($"Wrote {output}: {stationRows.Count} precincts, {Label(lastEnd)}; bands per 100k {string.Join(" / ", bands)}");
return 0;

static List<string> SplitCsv(string line)
{
    var fields = new List<string>();
    var current = new System.Text.StringBuilder();
    var quoted = false;
    foreach (var ch in line)
    {
        if (ch == '"') quoted = !quoted;
        else if (ch == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
        else current.Append(ch);
    }
    fields.Add(current.ToString());
    return fields;
}
