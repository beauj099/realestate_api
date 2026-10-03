# ImportLoadShedding

Loads past load-shedding into the `dbo.LoadShedding*` tables. The report's load-shedding section
appears only once they hold data for the property's area. Two sources, two sets of tables:

| mode | source | tables | patch |
|---|---|---|---|
| `--coct` | City of Cape Town open data, fetched directly | `LoadSheddingOutages`, `LoadSheddingAreaShapes` | `2026-10-03_load_shedding_outages.sql` |
| `--dir` | three schedule CSVs | `LoadSheddingStagePeriods`, `LoadSheddingAreaSlots`, `LoadSheddingSuburbAreas` | `2026-09-29_load_shedding.sql` |

Never use the archived eskom-calendar repository (CC BY-NC-SA, non-commercial), EskomSePush data,
or Eskom's website data (its terms forbid commercial use): this is a paid product.

Without `--apply` (or with `--dry-run`) nothing is written: the data is fetched or read, checked and
summarised. The connection string comes from the API's settings (`appsettings*.json`, including
`appsettings.Local.json`, then environment variables) in `--api-dir` (default: the current folder).

## Cape Town: `--coct`

```
dotnet run --project tools/ImportLoadShedding -- --coct --dry-run [--from 2020-01-01] [--at -33.9827,18.4656]
dotnet run --project tools/ImportLoadShedding -- --coct --api-dir . --apply
```

Sources (City of Cape Town open data; free to use, attribute "City of Cape Town"):

- **Load-shedding per area**, the City's record of what it carried out:
  `https://services6.arcgis.com/nyYfO9SxHU2ChQd9/arcgis/rest/services/LoadShed_gdb/FeatureServer/0`
  (~19,500 rows, 2018-06 to 2025-04). Fields: `Stage` ("Stage 4", sometimes "Stage 4, area 6"),
  `Circuit` ("Area 9"; sometimes "Area 4 & 12"; in 2018-2019 mostly feeder names such as
  "Durbanville"), `Minutes` (how long that area was off), `DateNew` + `TimeNew` (SAST wall clock).
  `DateTime` holds the same wall-clock time labelled UTC; it is not UTC and is only a fallback.
  A CSV copy (2020-01 to 2025-04) is at
  `https://www.arcgis.com/sharing/rest/content/items/baa72a23f28d4a6e91d9b7f13874a7f3/data`.
- **Load Shedding Blocks** (the areas' outlines, `BlockID` = the area number):
  `https://esapqa.capetown.gov.za/agsext/rest/services/Theme_Based/ODP_SPLIT_7/FeatureServer/13`.

What the import does:

- Each row becomes one outage for area `city-of-cape-town-area-<n>` (a row naming two areas becomes
  two). Rows naming a feeder instead of an area, rows before `--from` (default 2020-01-01: before
  2020 the City mostly logged feeders, so those years would read far too low), rows with no
  minutes or more than 480 (typing slips such as 734), and repeats of the same area and start are
  skipped; the dry run counts each.
- Outlines are stored as WGS84 rings (`[[[lng,lat],...],...]`, parts and holes, even-odd) with a
  bounding box. The API finds the property's area as the outline holding its point; an area whose
  outline has no outages (blocks 17, 21 and 23) falls back to the suburb lookup.
- `--apply` replaces this source's rows (`Source = 'coct-open-data'`) in both tables in one
  transaction. Re-running is safe: the City's layer is the whole truth each time.
- The dry run prints hours per area per year as the report will show them (overlapping records
  merged once, `LoadSheddingCalculator.ComputeFromOutages`), and with `--at lat,lng` the area for
  that point.

Run it monthly (or whenever the City updates the layer). There has been no load-shedding since
May 2025, so the figures have not changed since; a re-run then only rewrites the same rows.

What the figures are: the City's record of load-shedding implemented per area, at the City's own
stage (often lower than Eskom's). Pockets of Cape Town supplied directly by
Eskom followed Eskom's schedule and are not in this record; unplanned outages and faults are not
included. The report's caveat says so.

## Schedules: `--dir`

```
dotnet run --project tools/ImportLoadShedding -- --dir <folder with the three CSVs> [--api-dir <api>] [--apply]
```

**The three CSVs are the contract.** Where their rows come from is a separate job and not this
tool's business: build them from primary sources you are licensed to use (each municipality's
published schedules and stage announcements).

With `--apply` it replaces all three tables in one transaction (the files are the whole truth, not
an update).

All files: UTF-8, comma-separated, a header row; a field with a comma goes in double quotes.

## `stage_periods.csv`: the announced stages

| column | example | meaning |
|---|---|---|
| `start` | `2023-05-14 05:00` | SAST, when the stage began |
| `finsh` | `2023-05-14 22:00` | SAST, when it ended (after `start`) |
| `stage` | `4` | 1 to 8 |
| `exclude` | `coct` | optional: `coct` means it did not apply to Cape Town, which ran its own stage |
| `source` | `https://...` | optional: the announcement |

## `area_slots.csv`: each area's recurring monthly schedule

| column | example | meaning |
|---|---|---|
| `area` | `city-of-cape-town-area-9` | lowercase, hyphenated; Cape Town areas start `city-of-cape-town` |
| `date_of_month` | `14` | 1 to 31 |
| `stage` | `2` | the stage the slot belongs to (cumulative or per-stage lists both work) |
| `start` | `20:00` | local time |
| `finsh` | `00:30` | local time; at or before `start` means it runs past midnight |

## `suburb_areas.csv`: which area a suburb is in

| column | example | meaning |
|---|---|---|
| `province` | `Western Cape` | optional |
| `municipality` | `city-of-cape-town` | optional, slug; picks the right suburb when names repeat |
| `suburb` | `strand` | slug, as Stats SA names the sub place or main place |
| `area` | `city-of-cape-town-area-9` | as in `area_slots.csv` |
| `provider` | `City of Cape Town` | optional |
| `source` | `https://...` | optional |

## How the report uses them

`LoadSheddingService` (API) finds the area by the property's point in `LoadSheddingAreaShapes`, else
by its sub place (then main place) in `LoadSheddingSuburbAreas`. When the area has outages, the
history comes from them; otherwise it reads
that area's slots and the stage periods (dropping `coct` periods for Cape Town areas), and
`LoadSheddingCalculator` works out hours per year: every period's slots for a day are gathered
first and merged once, so overlapping announcements are never counted twice.
