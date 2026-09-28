// Reads published PDF valuation-roll books into dbo.RollBookImports / dbo.RollBookEntries.
//
//   dotnet run --project tools/ImportRollBooks -- [--api-dir <path>] [--municipality drakenstein] [--apply] [--force]
//
// Downloads each book in RollBookCatalogue, parses it (RollBookParser) and prints the sanity report.
// Without --apply it is a dry run: nothing is written. With --apply, a book that passes its sanity
// checks is stored and becomes the current import for its area; one that fails is refused unless
// --force is given. A book whose file (SHA-256) was imported before is skipped. Safe to re-run.

using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PropertyData.RollBooks;

var apply = args.Contains("--apply");
var force = args.Contains("--force");
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var apiDir = Path.GetFullPath(Arg("--api-dir") ?? Directory.GetCurrentDirectory());
var only = Arg("--municipality");
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
var config = new ConfigurationBuilder()
    .SetBasePath(apiDir)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

Console.WriteLine($"Mode: {(apply ? "APPLY" : "DRY RUN (nothing is written; add --apply)")}{(force ? ", importing books that fail their checks" : "")}");
Console.WriteLine();

SqlConnection? db = null;
if (apply)
{
    db = new SqlConnection(config["ConnectionStrings:DefaultConnection"]
        ?? throw new InvalidOperationException("Missing setting 'ConnectionStrings:DefaultConnection'."));
    await db.OpenAsync();
    if (await db.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID('dbo.RollBookEntries', 'U') IS NULL THEN 0 ELSE 1 END") == 0)
    {
        Console.WriteLine("The roll book tables do not exist yet: apply Infrastructure/Database/Patches/2026-09-28_roll_books.sql first.");
        return 1;
    }
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.UserAgent.ParseAdd(config["PropertyData:UserAgent"] ?? "RealWorth/1.0 (+https://api.realworth.co.za)");

var failures = 0;
foreach (var municipality in RollBookCatalogue.All.Where(m => only is null || m.Municipality == only))
{
    foreach (var url in municipality.BookUrls)
    {
        Console.WriteLine($"{municipality.Name}: {Uri.UnescapeDataString(url[(url.LastIndexOf('/') + 1)..])}");
        byte[] bytes;
        try
        {
            bytes = await http.GetByteArrayAsync(url);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  download failed: {ex.Message}");
            failures++;
            continue;
        }
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (db is not null && await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM dbo.RollBookImports WHERE FileSha256 = @sha", new { sha }) is { } existing)
        {
            Console.WriteLine($"  already imported (import {existing}); the file has not changed");
            continue;
        }

        var result = RollBookParser.Parse(new MemoryStream(bytes));
        var s = result.Sanity;
        Console.WriteLine($"  area {result.Area}, valued {result.DateOfValuation:yyyy-MM-dd}, {result.Pages} pages");
        Console.WriteLine($"  {s.Rows} rows, {s.Rejected} rejected, {s.WithValue} with a value, {s.GroupMembers} valued with a group or scheme");
        Console.WriteLine($"  median residential: {s.MedianResidentialExtent} m², R {s.MedianResidentialValue:N0}");
        foreach (var w in s.Warnings) Console.WriteLine($"  WARNING: {w}");
        foreach (var r in result.Rejected.Take(5)) Console.WriteLine($"  rejected: {r}");

        if (result.Area is null || result.DateOfValuation is null)
        {
            Console.WriteLine("  REFUSED: the book's area or date of valuation could not be read");
            failures++;
            continue;
        }
        if (result.DateOfValuation != municipality.DateOfValuation)
        {
            Console.WriteLine($"  REFUSED: the book says valued {result.DateOfValuation:yyyy-MM-dd}, the catalogue says " +
                              $"{municipality.DateOfValuation:yyyy-MM-dd}: a different roll? Check and update RollBookCatalogue");
            failures++;
            continue;
        }
        if (!s.LooksSane && !force)
        {
            Console.WriteLine("  REFUSED: the parse failed its sanity checks (use --force only after reading the warnings)");
            failures++;
            continue;
        }
        if (db is null) continue;

        var area = result.Area.Trim().ToUpperInvariant();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        await db.ExecuteAsync(
            "UPDATE dbo.RollBookImports SET IsCurrent = 0 WHERE Municipality = @m AND Area = @area AND RollVersion = @v",
            new { m = municipality.Municipality, area, v = municipality.RollVersion }, tx);
        var importId = await db.ExecuteScalarAsync<int>("""
            INSERT INTO dbo.RollBookImports
                (Municipality, Area, RollVersion, DateOfValuation, EffectiveFrom, SourceUrl, FileSha256, Pages, RowsRead, RowsRejected, SanityJson)
            OUTPUT INSERTED.Id
            VALUES (@Municipality, @Area, @RollVersion, @DateOfValuation, @EffectiveFrom, @SourceUrl, @FileSha256, @Pages, @RowsRead, @RowsRejected, @SanityJson)
            """, new
        {
            municipality.Municipality, Area = area, municipality.RollVersion,
            DateOfValuation = result.DateOfValuation.Value.ToDateTime(TimeOnly.MinValue),
            EffectiveFrom = municipality.EffectiveFrom.ToDateTime(TimeOnly.MinValue),
            SourceUrl = url, FileSha256 = sha, result.Pages, RowsRead = s.Rows, RowsRejected = s.Rejected,
            SanityJson = JsonSerializer.Serialize(s),
        }, tx);

        // Bulk copy: a town is tens of thousands of rows.
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
                 {
                     ("ImportId", typeof(int)), ("Municipality", typeof(string)), ("Area", typeof(string)), ("Erf", typeof(int)),
                     ("Portion", typeof(int)), ("IsGroupHead", typeof(bool)), ("GroupHeadErf", typeof(int)), ("ValuedUnder", typeof(string)),
                     ("Category", typeof(string)), ("Address", typeof(string)), ("ExtentM2", typeof(decimal)), ("ValueZar", typeof(decimal)),
                     ("Particulars", typeof(string)), ("Page", typeof(int)),
                 })
            table.Columns.Add(name, type);
        foreach (var r in result.Rows)
            table.Rows.Add(importId, municipality.Municipality, area, r.Erf, r.Portion, r.IsGroupHead,
                (object?)r.GroupHeadErf ?? DBNull.Value, Cap(r.ValuedUnder, 120), Cap(r.Category, 20), Cap(r.Address, 200),
                r.ExtentM2 is null ? DBNull.Value : (decimal)r.ExtentM2.Value, (object?)r.ValueZar ?? DBNull.Value,
                Cap(r.Particulars, 300), r.Page);

        using (var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.CheckConstraints, tx)
               { DestinationTableName = "dbo.RollBookEntries", BatchSize = 5000, BulkCopyTimeout = 600 })
        {
            foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await bulk.WriteToServerAsync(table);
        }
        await tx.CommitAsync();
        Console.WriteLine($"  imported as {importId}: {result.Rows.Count} rows");
    }
}

if (db is not null) await db.DisposeAsync();
Console.WriteLine();
Console.WriteLine(failures == 0 ? "Done." : $"Done, {failures} book(s) not imported.");
return failures == 0 ? 0 : 2;

static object Cap(string? s, int max) => s is null ? DBNull.Value : s.Length <= max ? s : s[..max];
