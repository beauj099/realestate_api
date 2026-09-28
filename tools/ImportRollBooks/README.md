# Roll books into the database

Most municipalities have no online roll search; they publish the roll as PDF "books", one per
town. This tool reads the books listed in `Infrastructure/PropertyData/RollBookCatalogue.cs`
(today: Drakenstein GV2024 — Paarl, Wellington, Mbekweni, Gouda, Saron, Bainskloof Pass) into
`dbo.RollBookImports` / `dbo.RollBookEntries`, which the property reports read for pins in those
towns. The books carry no owner names.

Needs the tables (`Infrastructure/Database/Patches/2026-09-28_roll_books.sql`) and the API's
database setting (`appsettings.Local.json` or environment variables).

```bash
# From the API repo:
dotnet run --project tools/ImportRollBooks -- --api-dir .            # dry run: download, parse, report
dotnet run --project tools/ImportRollBooks -- --api-dir . --apply    # store the books that pass
dotnet run --project tools/ImportRollBooks -- --api-dir . --municipality drakenstein --apply
```

- **Dry run by default**: it downloads and parses every book and prints the sanity report (rows,
  rejected lines, rows without a value, median residential erf and value) without writing.
- A book is **refused** when the parse fails its checks, or when the date of valuation printed on
  its cover differs from the catalogue's (a new roll: update the catalogue first). `--force`
  imports despite failed checks; read the warnings before using it.
- A book already imported (same file, by SHA-256) is skipped, so re-running is safe. A changed
  file for the same town becomes the current import; the older one stays in the table.

## Adding a municipality

1. Download one of its books and check the footer says **PenSoft** and the columns match
   (Erf No, Portion, Category, Address, Extent, Value, Other Particulars).
2. Find its demarcation code as the national cadastre writes it: the first four characters of a
   parcel key there (Drakenstein `W023`).
3. Add it to `RollBookCatalogue` with its roll version, date of valuation (from the book's cover),
   first day in force (from the council's notice) and the book URLs, then do a dry run.
