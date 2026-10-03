// Loads past load-shedding into dbo.LoadShedding* (see README.md). Two sources:
//
//   dotnet run --project tools/ImportLoadShedding -- --coct [--from 2020-01-01] [--at lat,lng] [--api-dir <path>] [--apply]
//       the City of Cape Town's record of load-shedding per area and the areas' outlines, fetched
//       from its open data into LoadSheddingOutages and LoadSheddingAreaShapes
//   dotnet run --project tools/ImportLoadShedding -- --dir <folder> [--api-dir <path>] [--apply]
//       the three schedule CSVs into LoadSheddingStagePeriods, -AreaSlots and -SuburbAreas
//
// Without --apply (or with --dry-run) nothing is written: the data is fetched or read, checked
// and summarised.

using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

if (args.Contains("--apply") && args.Contains("--dry-run"))
{
    Console.WriteLine("Give --apply or --dry-run, not both.");
    return 2;
}
var apply = args.Contains("--apply");
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var apiDir = Path.GetFullPath(Arg("--api-dir") ?? Directory.GetCurrentDirectory());
Console.WriteLine($"Mode: {(apply ? "APPLY" : "DRY RUN (nothing is written; add --apply)")}");

if (args.Contains("--coct"))
{
    var from = Arg("--from") is { } f
        ? DateTime.ParseExact(f, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : CapeTownImport.DefaultFrom;
    (double, double)? at = Arg("--at") is { } point && point.Split(',') is [var lat, var lng]
        ? (double.Parse(lat, CultureInfo.InvariantCulture), double.Parse(lng, CultureInfo.InvariantCulture))
        : null;
    return await CapeTownImport.RunAsync(apply, apiDir, from, at);
}

var dir = Arg("--dir") ?? throw new ArgumentException("Give --coct, or --dir <folder with the three CSVs>.");

var inv = CultureInfo.InvariantCulture;
var errors = new List<string>();

var periods = new DataTable();
periods.Columns.Add("StartLocal", typeof(DateTime));
periods.Columns.Add("FinshLocal", typeof(DateTime));
periods.Columns.Add("Stage", typeof(byte));
periods.Columns.Add("ExcludeTag", typeof(string));
periods.Columns.Add("Source", typeof(string));
foreach (var (line, row) in Read(Path.Combine(dir, "stage_periods.csv"), ["start", "finsh", "stage"]))
{
    if (!DateTime.TryParse(row["start"], inv, DateTimeStyles.None, out var start)
        || !DateTime.TryParse(row["finsh"], inv, DateTimeStyles.None, out var end) || end <= start
        || !byte.TryParse(row["stage"], out var stage) || stage is < 1 or > 8)
    {
        errors.Add($"stage_periods.csv line {line}: needs start before finsh and a stage 1-8");
        continue;
    }
    periods.Rows.Add(start, end, stage, Opt(row, "exclude"), Opt(row, "source"));
}

var slots = new DataTable();
slots.Columns.Add("Area", typeof(string));
slots.Columns.Add("DateOfMonth", typeof(byte));
slots.Columns.Add("Stage", typeof(byte));
slots.Columns.Add("StartTime", typeof(TimeSpan));
slots.Columns.Add("FinshTime", typeof(TimeSpan));
var seenSlots = new HashSet<string>();
foreach (var (line, row) in Read(Path.Combine(dir, "area_slots.csv"), ["area", "date_of_month", "stage", "start", "finsh"]))
{
    if (!byte.TryParse(row["date_of_month"], out var day) || day is < 1 or > 31
        || !byte.TryParse(row["stage"], out var stage) || stage is < 1 or > 8
        || !TimeSpan.TryParse(row["start"], inv, out var start) || !TimeSpan.TryParse(row["finsh"], inv, out var end)
        || row["area"].Length == 0)
    {
        errors.Add($"area_slots.csv line {line}: needs an area, a day 1-31, a stage 1-8 and two times");
        continue;
    }
    var area = Slug(row["area"]);
    if (!seenSlots.Add($"{area}|{day}|{stage}|{start}")) continue;   // the key allows one of each
    slots.Rows.Add(area, day, stage, start, end);
}

var suburbs = new DataTable();
suburbs.Columns.Add("Province", typeof(string));
suburbs.Columns.Add("Municipality", typeof(string));
suburbs.Columns.Add("Suburb", typeof(string));
suburbs.Columns.Add("Area", typeof(string));
suburbs.Columns.Add("Provider", typeof(string));
suburbs.Columns.Add("Source", typeof(string));
var seenSuburbs = new HashSet<string>();
foreach (var (line, row) in Read(Path.Combine(dir, "suburb_areas.csv"), ["suburb", "area"]))
{
    if (row["suburb"].Length == 0 || row["area"].Length == 0)
    {
        errors.Add($"suburb_areas.csv line {line}: needs a suburb and an area");
        continue;
    }
    var (suburb, area) = (Slug(row["suburb"]), Slug(row["area"]));
    if (!seenSuburbs.Add($"{suburb}|{area}")) continue;
    suburbs.Rows.Add(Opt(row, "province"), Opt(row, "municipality") is { } m ? Slug(m) : null,
        suburb, area, Opt(row, "provider"), Opt(row, "source"));
}

var areas = slots.AsEnumerable().Select(r => r.Field<string>("Area")!).ToHashSet();
var orphans = suburbs.AsEnumerable().Select(r => r.Field<string>("Area")!).Where(a => !areas.Contains(a)).Distinct().ToList();
Console.WriteLine($"stage_periods.csv: {periods.Rows.Count} periods");
Console.WriteLine($"area_slots.csv:    {slots.Rows.Count} slots in {areas.Count} areas");
Console.WriteLine($"suburb_areas.csv:  {suburbs.Rows.Count} suburbs");
if (orphans.Count > 0)
    Console.WriteLine($"Note: {orphans.Count} areas in suburb_areas.csv have no slots, e.g. {string.Join(", ", orphans.Take(5))}");
foreach (var e in errors.Take(20)) Console.WriteLine("  " + e);
if (errors.Count > 0)
{
    Console.WriteLine($"{errors.Count} rows rejected. Fix the files; nothing was written.");
    return 1;
}
if (!apply) return 0;

await using var db = await Db.OpenAsync(apiDir);
await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
foreach (var table in new[] { "LoadSheddingStagePeriods", "LoadSheddingAreaSlots", "LoadSheddingSuburbAreas" })
    await new SqlCommand($"DELETE FROM dbo.{table}", db, tx).ExecuteNonQueryAsync();
await Db.CopyAsync(db, tx, "LoadSheddingStagePeriods", periods);
await Db.CopyAsync(db, tx, "LoadSheddingAreaSlots", slots);
await Db.CopyAsync(db, tx, "LoadSheddingSuburbAreas", suburbs);
await tx.CommitAsync();
Console.WriteLine("Imported. The API picks the new figures up within a week, or at once after a restart.");
return 0;

static string Slug(string s) =>
    string.Join('-', s.Trim().ToLowerInvariant().Split([' ', '\t', '_', '-'], StringSplitOptions.RemoveEmptyEntries));

static string? Opt(Dictionary<string, string> row, string key) =>
    row.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

/// <summary>Rows of a CSV as header -> value, with 1-based line numbers; quoted fields may hold commas.</summary>
static IEnumerable<(int Line, Dictionary<string, string> Row)> Read(string path, string[] required)
{
    if (!File.Exists(path)) throw new FileNotFoundException($"Missing {Path.GetFileName(path)} in the folder.", path);
    using var reader = new StreamReader(path, Encoding.UTF8);
    var header = Split(reader.ReadLine() ?? "").Select(h => h.Trim().ToLowerInvariant()).ToList();
    var missing = required.Where(r => !header.Contains(r)).ToList();
    if (missing.Count > 0)
        throw new InvalidDataException($"{Path.GetFileName(path)} lacks column(s): {string.Join(", ", missing)}");
    var line = 1;
    while (reader.ReadLine() is { } text)
    {
        line++;
        if (text.Trim().Length == 0) continue;
        var cells = Split(text);
        yield return (line, header.Select((h, i) => (h, v: i < cells.Count ? cells[i].Trim() : ""))
            .ToDictionary(x => x.h, x => x.v));
    }
}

static List<string> Split(string line)
{
    var cells = new List<string>();
    var sb = new StringBuilder();
    var quoted = false;
    for (var i = 0; i < line.Length; i++)
    {
        var c = line[i];
        if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
        else if (c == '"') quoted = !quoted;
        else if (c == ',' && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
        else sb.Append(c);
    }
    cells.Add(sb.ToString());
    return cells;
}
