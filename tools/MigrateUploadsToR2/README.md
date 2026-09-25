# Move local uploads to R2

One-off tool for switching `Storage:Provider` from `Local` to `R2`. Photos and
documents uploaded while the API stored files on its own disk live in
`wwwroot/uploads`, and the database links to them as `/uploads/...`. This copies
each file into the R2 bucket under the same key and repoints the links at the
bucket's public URL.

Run it **on the server that has the files**, after the R2 settings are in place
(environment variables or `appsettings.Local.json`) and **before** removing the
old `wwwroot/uploads` folder.

```bash
# From the API repo, pointing at the deployed API folder:
dotnet run --project tools/MigrateUploadsToR2 -- --api-dir "C:\path\to\deployed\api"

# Looks right? Apply it:
dotnet run --project tools/MigrateUploadsToR2 -- --api-dir "C:\path\to\deployed\api" --apply
```

- **Dry run by default** — reports what it would do, changes nothing.
- Reads the same settings as the API from `--api-dir` (`appsettings*.json`,
  `appsettings.Local.json`, environment variables). `--uploads <path>`
  overrides the uploads folder if it is not under the API folder.
- Safe to re-run: files already in R2 are not uploaded again, and only links
  still starting `/uploads/` are changed. Database updates happen in one
  transaction.
- Local files are **never deleted** — keep them until the app shows the photos
  from R2, then remove `wwwroot/uploads` yourself.
- A link whose file is missing is reported and left unchanged (exit code 2).
