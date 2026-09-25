-- Listing archive: ArchivedAt is set (UTC) when the agent archives a listing via
-- PUT /api/listings/{id}/archive and cleared when it is restored. Returned on the
-- listing summary and detail; GET /api/listings still returns archived listings and
-- the app splits them into tabs.
-- Not applied automatically -- run it once in SSMS or sqlcmd against the deployed
-- database, BEFORE deploying the API build that uses it (every listing query selects
-- this column). Safe to re-run: the column is only added when missing.
--
-- ArchivedAt is nullable: NULL means the listing is active.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Listings', 'ArchivedAt') IS NULL
    ALTER TABLE [dbo].[Listings] ADD [ArchivedAt] DATETIME NULL;

COMMIT TRANSACTION;
