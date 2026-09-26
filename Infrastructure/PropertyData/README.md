# Property data (Cape Town, Johannesburg, Tshwane, national)

Address → erf → property record → valuation report, from public City of Cape Town data. No
credentials needed. Background, endpoint reference and compliance rules:
`docs/property-data/integration-brief.md` (read it before changing this folder).

| File | What it is |
|---|---|
| `CapeTownPropertyData.cs` | The adapter: models, address normaliser, geodesy, ArcGIS client, valuation-roll scraper, comparable analyzer, SVG site plan, provider, DI (`AddCapeTownPropertyData`) |
| `JohannesburgPropertyData.cs` | City of Johannesburg (values, zoning, last registered sales) and the national cadastre fallback (erf and boundary from a GPS pin) |
| `TshwanePropertyData.cs` | City of Tshwane: the national cadastre's parcel plus the City's GV2025 roll (value, category, registered size). Pins only; no sales |
| `Application/Services/DataSourceHealthService.cs` | One known property per source; `GET /api/admin/data-sources/health` (Admin) and a monthly run that emails on failure |
| `PropertyImagery.cs` | Google imagery links and the print rule (satellite printable with attribution, Street View screen-only) |
| `Application/Services/PropertyReportService.cs` | Caching (12 h in memory: one report = one fetch) and the DTO the app reads |
| `Controllers/PropertyReportController.cs` | `POST /api/property/resolve`, `GET /api/property/{municipality}/{erf}`, `…/site-plan.svg`, `GET /api/property/imagery/{kind}` — agents only |
| `tests/PropertyData.Tests` | Offline unit tests + a live golden fixture (17 Pine Road, Claremont) |

```bash
dotnet test tests/PropertyData.Tests --filter "Category!=Integration"   # offline
dotnet test tests/PropertyData.Tests --filter "Category=Integration"    # hits the live City services
```

Settings (`appsettings.Local.json` or environment variables):
- `PropertyData:UserAgent` — who the City sees calling. Put a contact they can reach.
- `Imagery:GoogleMapsApiKey` — optional. Without it the report simply has no imagery.
- `DataSourceChecks:AlertEmail` — who is emailed when the monthly source check fails (needs
  `Smtp:Host`); empty means the failure is only logged. `DataSourceChecks:DayOfMonth` (default 3,
  0 turns it off) runs it at 01:40.

## Things that will bite you

- **`Shape__Area` is not the erf size** (Web Mercator, ~45% high here). `Geo.RingAreaM2` projects
  on the WGS84 ellipsoid: 1 076 m² for a 1 085 m² deed. Prefer the roll's extent.
- **Street names have no type** in the parcels layer (`STR_NAME = 'PINE'`, type separate); the
  `AddressNormalizer` strips it, with or without a comma ("17 Pine Rd Claremont").
- **Search the valuation roll by erf, not address.** `ADD,17,PINE` matches every street starting
  "PINE" and the roll pages them 10 at a time in no fixed order, so the subject can be missing
  from the first page.
- **Postback links are HTML-encoded** (`__doPostBack(&#39;…&#39;)`); decode before reading the
  target, or the dwelling-extent step silently returns nothing.
- **Footprints need the parcel polygon** plus the centroid filter; a bounding box pulls in the
  neighbours' buildings. Erf 53927 has a 258 m² main roof (6.5 m) and a 68 m² outbuilding — the
  345 m² first recorded was a neighbour's.
- **Footprints are from 2013**; show the capture date and read them with plan approvals.
- **The sales list arrives whole** (2 237 rows in one GET) and includes R0 transfers; the
  `ComparableAnalyzer` filters and reports the counts.
- **The City's terms of use apply**: cache (done), keep the UserAgent honest, don't hammer.
- **Tshwane's roll search matches erf AND township as substrings.** erf "1" + "WATERKLOOF"
  returns 5 767 rows across 34 townships. Always send both, then filter exactly
  (`TshwaneRollClient.Matches`): base township or its "Xnn" extensions, same stand and portion.
- **Tshwane: the cadastre's whole erf is the roll's remainder** ("00062/ R"); sectional-title units
  are "00062/ 1 - UNIT 0002", each valued, with the erf's own row at "R 1" — the placeholder for no
  value, never a price. The roll has no addresses (resolve by pin) and no sales.
- **Tshwane parcels are recognised by the cadastre key prefix `GTSH`** (Johannesburg's is `GJHB`).
  The roll is GV2025: valued as at 1 July 2024, in effect 1 July 2025 – 30 June 2029.
- **Ekurhuleni is not scraped**: its portal states it is for property owners viewing their own
  values. Ask the City for an extract instead.

## Not done yet

1. Persisting to the database (`docs/property-data/schema-not-applied.sql`) — the in-memory cache
   is lost on restart.
2. AfriGIS (ownership, transfers, annual trend) — needs their trial key; the owner-data endpoint,
   POPIA audit and retention in the brief come with it.
3. Other metros — further `IPropertyDataProvider`s (eThekwini, Nelson Mandela Bay…).
4. Suburb benchmarks for Tshwane (the median roll value of similar erven) — a township query
   returns thousands of rows (about 9 MB), so it would need its own long cache.
