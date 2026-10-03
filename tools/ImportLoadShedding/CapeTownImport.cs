// --coct: load-shedding as carried out per area, and the areas' outlines, straight from the City
// of Cape Town's open data, into dbo.LoadSheddingOutages and dbo.LoadSheddingAreaShapes
// (patch 2026-10-03_load_shedding_outages.sql). See README.md.

using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RealEstateApi.Application.Services;

static partial class CapeTownImport
{
    /// <summary>The City's record of load-shedding per area (ArcGIS Online, City of Cape Town).</summary>
    public const string OutagesLayer =
        "https://services6.arcgis.com/nyYfO9SxHU2ChQd9/arcgis/rest/services/LoadShed_gdb/FeatureServer/0";

    /// <summary>The City's "Load Shedding Blocks" outlines (BlockID = the area number).</summary>
    public const string BlocksLayer =
        "https://esapqa.capetown.gov.za/agsext/rest/services/Theme_Based/ODP_SPLIT_7/FeatureServer/13";

    /// <summary>Rows written by this mode carry this tag; a re-run replaces exactly these.</summary>
    public const string Source = "coct-open-data";
    public const string Municipality = "city-of-cape-town";

    /// <summary>
    /// Before 2020 the City mostly logged feeders by name (Durbanville, Steenberg 1, ...), not
    /// areas, so the years before would read far too low.
    /// </summary>
    public static readonly DateTime DefaultFrom = new(2020, 1, 1);

    public sealed record OutageRow(string Area, DateTime StartLocal, int Minutes, byte? Stage);
    public sealed record ShapeRow(string Area, double MinLat, double MinLng, double MaxLat, double MaxLng,
        List<List<double[]>> Rings);

    public static async Task<int> RunAsync(bool apply, string apiDir, DateTime from, (double Lat, double Lng)? at)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-ImportLoadShedding/1.0 (+https://realworth.co.za)");

        Console.WriteLine($"Fetching outages from {OutagesLayer} ...");
        var (outages, skipped) = await FetchOutagesAsync(http, from);
        Console.WriteLine($"Fetching area outlines from {BlocksLayer} ...");
        var shapes = await FetchShapesAsync(http);

        Console.WriteLine();
        Console.WriteLine($"Outages kept: {outages.Count} rows in {outages.Select(o => o.Area).Distinct().Count()} areas, " +
                          $"{outages.Min(o => o.StartLocal):yyyy-MM-dd} to {outages.Max(o => o.StartLocal):yyyy-MM-dd}");
        foreach (var (why, n) in skipped.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  skipped {n,5}: {why}");
        Console.WriteLine($"Outlines: {shapes.Count} areas ({string.Join(", ", shapes.Select(s => s.Area.Split('-')[^1]).OrderBy(n => int.TryParse(n, out var i) ? i : 999))})");
        var noShape = outages.Select(o => o.Area).Distinct().Except(shapes.Select(s => s.Area)).ToList();
        var noOutages = shapes.Select(s => s.Area).Except(outages.Select(o => o.Area)).ToList();
        if (noShape.Count > 0) Console.WriteLine($"  areas with outages but no outline: {string.Join(", ", noShape)}");
        if (noOutages.Count > 0) Console.WriteLine($"  outlines with no outages (the report falls back to the suburb): {string.Join(", ", noOutages)}");

        PrintHoursPerYear(outages);

        if (at is { } p)
        {
            var hit = shapes.Where(s => Inside(s, p.Lat, p.Lng)).Select(s => s.Area).FirstOrDefault();
            Console.WriteLine($"Point {p.Lat.ToString(CultureInfo.InvariantCulture)},{p.Lng.ToString(CultureInfo.InvariantCulture)}: {hit ?? "in no area"}");
        }

        if (outages.Count == 0 || shapes.Count == 0)
        {
            Console.WriteLine("Nothing fetched for one of the two sources; nothing was written.");
            return 1;
        }
        if (!apply) return 0;

        var outageTable = new DataTable();
        outageTable.Columns.Add("Area", typeof(string));
        outageTable.Columns.Add("StartLocal", typeof(DateTime));
        outageTable.Columns.Add("Minutes", typeof(int));
        outageTable.Columns.Add("Stage", typeof(byte));
        outageTable.Columns.Add("Source", typeof(string));
        foreach (var o in outages)
            outageTable.Rows.Add(o.Area, o.StartLocal, o.Minutes, o.Stage is { } s ? s : DBNull.Value, Source);

        var shapeTable = new DataTable();
        shapeTable.Columns.Add("Area", typeof(string));
        shapeTable.Columns.Add("Municipality", typeof(string));
        shapeTable.Columns.Add("MinLat", typeof(double));
        shapeTable.Columns.Add("MinLng", typeof(double));
        shapeTable.Columns.Add("MaxLat", typeof(double));
        shapeTable.Columns.Add("MaxLng", typeof(double));
        shapeTable.Columns.Add("RingsJson", typeof(string));
        shapeTable.Columns.Add("Source", typeof(string));
        foreach (var s in shapes)
            shapeTable.Rows.Add(s.Area, Municipality, s.MinLat, s.MinLng, s.MaxLat, s.MaxLng,
                JsonSerializer.Serialize(s.Rings), Source);

        await using var db = await Db.OpenAsync(apiDir);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        foreach (var table in new[] { "LoadSheddingOutages", "LoadSheddingAreaShapes" })
        {
            var delete = new SqlCommand($"DELETE FROM dbo.{table} WHERE Source = @source", db, tx);
            delete.Parameters.AddWithValue("@source", Source);
            await delete.ExecuteNonQueryAsync();
        }
        await Db.CopyAsync(db, tx, "LoadSheddingOutages", outageTable);
        await Db.CopyAsync(db, tx, "LoadSheddingAreaShapes", shapeTable);
        await tx.CommitAsync();
        Console.WriteLine($"Imported {outageTable.Rows.Count} outages and {shapeTable.Rows.Count} outlines " +
                          "(replacing this source's rows). The API picks them up within a week, or at once after a restart.");
        return 0;
    }

    // ---- outages ---------------------------------------------------------------------------------

    private static async Task<(List<OutageRow> Rows, Dictionary<string, int> Skipped)> FetchOutagesAsync(
        HttpClient http, DateTime from)
    {
        var skipped = new Dictionary<string, int>();
        void Skip(string why) => skipped[why] = skipped.GetValueOrDefault(why) + 1;
        var byKey = new Dictionary<(string, DateTime), OutageRow>();
        var total = 0;

        await foreach (var a in PagesAsync(http, OutagesLayer, "outFields=*&returnGeometry=false"))
        {
            total++;
            var circuit = Str(a, "Circuit");
            var areas = AreaNumbers(circuit);
            if (areas.Count == 0) { Skip("circuit is a feeder name, not an area (mostly 2018-2019)"); continue; }
            if (Start(a) is not { } start) { Skip("no date"); continue; }
            if (start < from) { Skip($"before {from:yyyy-MM-dd}"); continue; }
            var minutes = a.TryGetProperty("Minutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0;
            if (minutes <= 0 || minutes > LoadSheddingCalculator.MaxOutageMinutes)
            {
                Skip($"minutes missing, not positive or over {LoadSheddingCalculator.MaxOutageMinutes}");
                continue;
            }
            var stage = StageOf(Str(a, "Stage"));
            foreach (var n in areas)
            {
                var row = new OutageRow(LoadSheddingCalculator.CapeTownArea(n), start, minutes, stage);
                var key = (row.Area, start);
                if (byKey.TryGetValue(key, out var seen))
                {
                    Skip("same area and start recorded twice (longer kept)");
                    if (seen.Minutes >= minutes) continue;
                }
                byKey[key] = row;
            }
        }
        Console.WriteLine($"  {total} records read");
        return ([.. byKey.Values.OrderBy(o => o.Area).ThenBy(o => o.StartLocal)], skipped);
    }

    /// <summary>"Area 9" → [9]; "Area 4 & 12", "Area 4 and 8" → both; "Area 13.1" → [13]; feeders → [].</summary>
    public static List<int> AreaNumbers(string? circuit)
    {
        if (circuit is null || AreaPrefix().Match(circuit) is not { Success: true } match) return [];
        var rest = SubBlock().Replace(match.Groups[1].Value, "");
        return [.. Regex.Matches(rest, @"\d+").Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture))
            .Where(n => n is >= 1 and <= 99).Distinct()];
    }

    /// <summary>"Stage 4", "Stage 4, area 6", "Satge 4" → 4.</summary>
    public static byte? StageOf(string? text) =>
        text is not null && Regex.Match(text, @"\d+") is { Success: true } d
        && byte.TryParse(d.Value, CultureInfo.InvariantCulture, out var s) && s is >= 1 and <= 8 ? s : null;

    /// <summary>
    /// The local start. DateNew and TimeNew are SAST wall-clock; the DateTime field holds the same
    /// wall-clock time labelled UTC (it is not UTC), so it is only the fallback, read as written.
    /// </summary>
    private static DateTime? Start(JsonElement a)
    {
        if (DateOnly.TryParseExact(Str(a, "DateNew"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            var t = TimeOnly.TryParse(Str(a, "TimeNew"), CultureInfo.InvariantCulture, out var time) ? time : TimeOnly.MinValue;
            return d.ToDateTime(t);
        }
        return a.TryGetProperty("DateTime", out var ms) && ms.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms.GetInt64()).UtcDateTime
            : null;
    }

    private static void PrintHoursPerYear(List<OutageRow> outages)
    {
        var perArea = outages.GroupBy(o => o.Area)
            .OrderBy(g => int.Parse(g.Key.Split('-')[^1], CultureInfo.InvariantCulture))
            .Select(g => (Area: g.Key, Years: LoadSheddingCalculator.ComputeFromOutages(
                [.. g.Select(o => new Outage(o.StartLocal, o.Minutes, o.Stage))]).Years))
            .ToList();
        var years = perArea.SelectMany(a => a.Years.Select(y => y.Year)).Distinct().Order().ToList();
        Console.WriteLine();
        Console.WriteLine("Hours of load-shedding per year (merged; what the report shows):");
        Console.WriteLine("area     " + string.Concat(years.Select(y => $"{y,8}")) + "   worst month (worst year)");
        foreach (var (area, ys) in perArea)
        {
            var cells = years.Select(y => ys.FirstOrDefault(x => x.Year == y) is { } r ? $"{r.Hours,8:0}" : $"{"-",8}");
            var worst = ys.MaxBy(y => y.Hours);
            Console.WriteLine($"{"Area " + area.Split('-')[^1],-9}{string.Concat(cells)}   {worst?.WorstMonth} ({worst?.WorstMonthHours:0} h)");
        }
        Console.WriteLine();
    }

    // ---- outlines --------------------------------------------------------------------------------

    private static async Task<List<ShapeRow>> FetchShapesAsync(HttpClient http)
    {
        var shapes = new Dictionary<int, ShapeRow>();
        await foreach (var f in PagesAsync(http, BlocksLayer, "outFields=BlockID&returnGeometry=true&outSR=4326", features: true))
        {
            if (!f.TryGetProperty("attributes", out var a) || !a.TryGetProperty("BlockID", out var id)
                || id.ValueKind != JsonValueKind.Number || !f.TryGetProperty("geometry", out var g)
                || !g.TryGetProperty("rings", out var rings)) continue;
            var list = rings.EnumerateArray()
                .Select(r => r.EnumerateArray()
                    .Select(p => new[] { Math.Round(p[0].GetDouble(), 6), Math.Round(p[1].GetDouble(), 6) }).ToList())
                .Where(r => r.Count >= 4).ToList();
            if (list.Count == 0) continue;
            var block = id.GetInt32();
            if (shapes.TryGetValue(block, out var existing)) list = [.. existing.Rings, .. list];   // one area, two features
            var pts = list.SelectMany(r => r).ToList();
            shapes[block] = new ShapeRow(LoadSheddingCalculator.CapeTownArea(block),
                pts.Min(p => p[1]), pts.Min(p => p[0]), pts.Max(p => p[1]), pts.Max(p => p[0]), list);
        }
        return [.. shapes.OrderBy(kv => kv.Key).Select(kv => kv.Value)];
    }

    /// <summary>Even-odd over every ring (parts and holes), as the API does.</summary>
    private static bool Inside(ShapeRow s, double lat, double lng)
    {
        if (lat < s.MinLat || lat > s.MaxLat || lng < s.MinLng || lng > s.MaxLng) return false;
        var inside = false;
        foreach (var p in s.Rings)
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                if ((p[i][1] > lat) != (p[j][1] > lat)
                    && lng < (p[j][0] - p[i][0]) * (lat - p[i][1]) / (p[j][1] - p[i][1]) + p[i][0])
                    inside = !inside;
        return inside;
    }

    // ---- ArcGIS paging ---------------------------------------------------------------------------

    /// <summary>Every feature's attributes (or the whole feature), page by page in OBJECTID order.</summary>
    private static async IAsyncEnumerable<JsonElement> PagesAsync(HttpClient http, string layer, string query,
        bool features = false)
    {
        const int page = 1000;
        for (var offset = 0; ; offset += page)
        {
            var url = $"{layer}/query?where=1%3D1&{query}&orderByFields=OBJECTID&resultOffset={offset}" +
                      $"&resultRecordCount={page}&f=json";
            using var doc = await GetJsonAsync(http, url);
            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException($"ArcGIS error from {layer}: {error}");
            var list = doc.RootElement.GetProperty("features");
            foreach (var f in list.EnumerateArray())
                yield return features ? f.Clone() : f.GetProperty("attributes").Clone();
            var more = doc.RootElement.TryGetProperty("exceededTransferLimit", out var x) && x.ValueKind == JsonValueKind.True;
            if (list.GetArrayLength() == 0 || !more) yield break;
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = await http.GetStreamAsync(url);
                return await JsonDocument.ParseAsync(stream);
            }
            catch (Exception ex) when (attempt < 4 && ex is HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"  retrying after: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
            }
        }
    }

    private static string? Str(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

    [GeneratedRegex(@"^\s*areas?\b(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AreaPrefix();

    [GeneratedRegex(@"\.\d+")]
    private static partial Regex SubBlock();
}

static class Db
{
    public static async Task<SqlConnection> OpenAsync(string apiDir)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var config = new ConfigurationBuilder().SetBasePath(apiDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables().Build();
        var db = new SqlConnection(config["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("Missing setting 'ConnectionStrings:DefaultConnection'."));
        await db.OpenAsync();
        return db;
    }

    public static async Task CopyAsync(SqlConnection db, SqlTransaction tx, string table, DataTable rows)
    {
        using var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "dbo." + table, BatchSize = 5000 };
        foreach (DataColumn c in rows.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(rows);
    }
}
