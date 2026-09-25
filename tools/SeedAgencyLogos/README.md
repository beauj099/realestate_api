# Agency logos into R2

Uploads agency logos into the R2 bucket and sets `Agencies.LogoUrl`, which the
app reads from `GET /api/agencies`. The first run moves the logos the app used
to bundle (`realestate_app/assets/images/agencies/*.png` plus the house
`logo.jpg`).

Needs the `Agencies` table
(`Infrastructure/Database/Patches/2026-09-27_agencies.sql`), and the same R2 and
database settings as the API (`appsettings.Local.json` or environment
variables).

```bash
# From the API repo, with the app repo checked out next to it:
dotnet run --project tools/SeedAgencyLogos -- --api-dir .
dotnet run --project tools/SeedAgencyLogos -- --api-dir . --apply
```

- **Dry run by default**, which changes nothing.
- The tool looks for `<slug>.png` (also `.jpg`, `.jpeg`, `.webp`) in `--logos <dir>`, which defaults to `../realestate_app/assets/images/agencies`. For `realworth` it uses `--house-logo`.
- An agency that already has a logo is skipped. Add `--force` to replace it; the new URL then gets a `?v=` marker, so phones do not keep showing the old logo.

## Adding an agency without an app release

1. Insert a row into `dbo.Agencies`. Set `Slug` (lowercase, dashes), `Name`, `Monogram` (up to 4 letters), the colours as `#RRGGBB` and `SortOrder`.
2. Put `<slug>.png` in a folder and run this tool with `--logos <that folder> --apply`.

To restyle an agency, an Admin calls `PUT /api/agencies/{id}` (to change colours) or `PUT /api/agencies/{id}/logo` (to change the logo). You can also edit the row directly.
