# ImportLoadShedding

Loads past load-shedding into `dbo.LoadSheddingStagePeriods`, `dbo.LoadSheddingAreaSlots` and
`dbo.LoadSheddingSuburbAreas` (patch `2026-09-29_load_shedding.sql`) from three CSV files. The
report's load-shedding section appears only once these tables hold data.

**The three CSVs are the contract.** Where their rows come from is a separate job and not this
tool's business: build them from primary sources you are licensed to use (Eskom's and each
municipality's published schedules, and Eskom's stage announcements). Do not build them from the
archived eskom-calendar repository: its data is CC BY-NC-SA (non-commercial), and this is a paid
product.

```
dotnet run --project tools/ImportLoadShedding -- --dir <folder with the three CSVs> [--api-dir <api>] [--apply]
```

Without `--apply` it only reads and checks the files. With `--apply` it replaces all three tables in
one transaction (the files are the whole truth, not an update).

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

`LoadSheddingService` (API) finds the area for the property's sub place (then main place), reads
that area's slots and the stage periods (dropping `coct` periods for Cape Town areas), and
`LoadSheddingCalculator` works out hours per year: every period's slots for a day are gathered
first and merged once, so overlapping announcements are never counted twice.
