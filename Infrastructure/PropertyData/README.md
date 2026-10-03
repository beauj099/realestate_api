# Property data (Cape Town, Johannesburg, Tshwane, Mossel Bay, Drakenstein, national)

Address → erf → property record → valuation report, from public City of Cape Town data. No
credentials needed. Background, endpoint reference and compliance rules:
`docs/property-data/integration-brief.md` (read it before changing this folder).

| File | What it is |
|---|---|
| `CapeTownPropertyData.cs` | The adapter: models, address normaliser, geodesy, ArcGIS client, valuation-roll scraper, comparable analyzer, SVG site plan, provider, DI (`AddCapeTownPropertyData`) |
| `JohannesburgPropertyData.cs` | City of Johannesburg (values, zoning, last registered sales) and the national cadastre fallback (erf and boundary from a GPS pin) |
| `TshwanePropertyData.cs` | City of Tshwane: the national cadastre's parcel plus the City's GV2025 roll (value, category, registered size). Pins only; no sales |
| `MosselBayPropertyData.cs` | Mossel Bay: the national cadastre's parcel plus the municipality's NDK online roll (street address, value, category, size). Pins only; no sales |
| `RollBookParser.cs`, `RollBookCatalogue.cs`, `RollBookPropertyData.cs` | Rolls published only as PDF books (PenSoft layout; Drakenstein GV2024), imported by `tools/ImportRollBooks` into `dbo.RollBookEntries` and read for pins in those towns |
| `PhotonGeocoder.cs`, `Application/Services/AddressSearch*.cs` | Address type-ahead for all of South Africa: City records (`GET /api/property/suggest`) and Photon/OpenStreetMap (`…/suggest/national`), ranked on one scale (`AddressSearch.Rank`/`Order`) so the app can merge them; `…/suggest/at?lat&lng` gives the City's address for the erf under a GPS pin (Cape Town, Johannesburg) |
| `Property24Listings.cs`, `Application/Services/ForSaleListingsService.cs` | Homes for sale like the subject from Property24 (robots.txt-allowed pages only, credited and linked); `GET /api/property/{municipality}/{erf}/for-sale` |
| `Application/Services/AreaDetailsService.cs`, `Data/crime-stats.json` | Area details: NASA POWER climate, Census 2011 + WorldPop population, Census 2011 income, SAPS crime per precinct (`tools/BuildCrimeStats`, quarterly); `GET /api/property/area` |
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
- **Mossel Bay's roll** (ndkonlineroll.co.za, roll 7) is a plain GET: township id from
  `GetTownships`, then `SearchFT` by erf. Roll 2022–2026, valued 1 July 2021. "R 0.00" is a
  placeholder. Read columns by header and never the Owner column. Parcels: key prefix `W043`.
- **The same NDK host's other rolls are stale**: Metsimaholo (roll 6) serves 2019–2024 and
  Emfuleni (roll 1) 2017–2019, and their erven sit in "EXT nn" townships the cadastre does not
  name. Not used. Metsimaholo's results include a column in SA ID number format.
- **Roll books (PDF) put a space between thousands** ("2 793.2484 Ha", "30 575 000") and the header
  row does not bound the data (digits sit left of "Extent"; "Including :- …" starts 120 pt left of
  its header). `RollBookParser` anchors each number on its unit and joins only digit groups a
  normal space apart. Checked on all of Paarl (24 585 rows) against an independent extraction:
  no differences. Plain text extraction also runs a street number into the extent ("Bainskloof
  33 … 803 m²" reads as "33 803 m²").
- **Consolidated erven**: "5*" is valued for the group ("Including :- Paarl 5, Paarl 7, Paarl 9");
  members show 0 and "See :- Paarl 5*". An erf "valued under" a sectional scheme ("Note :- See SS
  The Mews") shows 0 too. Neither 0 is a value; the report says what it is instead.
- **Other small towns**: George, Knysna, Overstrand … also publish PDFs; add them to
  `RollBookCatalogue` once their footer says PenSoft (see `tools/ImportRollBooks/README.md`).
- **Ekurhuleni is not scraped**: its portal states it is for property owners viewing their own
  values. Ask the City for an extract instead.

- **Comparables are nearest first.** The City's area-sales list covers a whole neighbourhood
  (all of Strand: 2 278 sales), so each sale is placed on the parcel map and kept within 500 m
  (1 km when fewer than six are that close). Several erven transferred on one day for one price
  are one bulk deal, not a price. Scaling price per m² straight up overvalues bigger homes;
  sales are carried to the subject's size with an elasticity of 0.6.
- **Suburb names differ between sources** (the City's "Lynn's View" is partly Property24's
  "Steynsrust"; OSM's "Die Vlakte" is the City's "Strand"). So Property24 homes are searched in the
  suburb and Property24's own "surrounding suburbs", each listing's map position is read from its
  page, and the nearest alike are kept (2.5 km, then 5 km); agency listings and agent-reported
  sales also match by distance when the property's location is known.
- **Property24** answers 503 when hit repeatedly; the suburb sitemap (3.6 MB) is kept on disk a
  week, and a known suburb id needs no sitemap at all.
- **Load-shedding** (past only): for Cape Town, the City's own record of load-shedding carried out
  per area and the areas' outlines, fetched from its open data by `tools/ImportLoadShedding --coct`
  into `dbo.LoadSheddingOutages` / `dbo.LoadSheddingAreaShapes` (patch
  `2026-10-03_load_shedding_outages.sql`); the property's area is the outline holding its point.
  Elsewhere, three schedule CSVs into the tables of `2026-09-29_load_shedding.sql`, found by suburb
  name (not from the archived, non-commercial eskom-calendar repo, EskomSePush or Eskom's site).
  `LoadSheddingCalculator` merges overlaps once (per day for schedules, per area for outages).

## Not done yet

1. Persisting to the database (`docs/property-data/schema-not-applied.sql`) — the in-memory cache
   is lost on restart.
2. AfriGIS (ownership, transfers, annual trend) — needs their trial key; the owner-data endpoint,
   POPIA audit and retention in the brief come with it.
3. Other metros — further `IPropertyDataProvider`s (eThekwini, Nelson Mandela Bay…).
4. Suburb benchmarks for Tshwane (the median roll value of similar erven) — a township query
   returns thousands of rows (about 9 MB), so it would need its own long cache.
