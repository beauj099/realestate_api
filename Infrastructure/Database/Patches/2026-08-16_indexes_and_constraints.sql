-- Schema hardening carried over from b037479 after the PropertyListingsDB project was
-- removed from the repo (ec80f68). Not applied automatically -- run it once in SSMS or
-- sqlcmd against the deployed database. Safe to re-run: every step checks first.
--
-- Before running, check for rows that would violate the two unique constraints:
--   SELECT ListingRoomId, COUNT(*) FROM Condition            GROUP BY ListingRoomId HAVING COUNT(*) > 1;
--   SELECT ListingId,     COUNT(*) FROM PropertyRunningCosts GROUP BY ListingId     HAVING COUNT(*) > 1;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Indexes on the FK columns used by every per-listing and per-room lookup.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Contact_ListingId' AND object_id = OBJECT_ID('dbo.Contact'))
    CREATE NONCLUSTERED INDEX [IX_Contact_ListingId] ON [dbo].[Contact] ([ListingId] ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ListingParking_ListingId' AND object_id = OBJECT_ID('dbo.ListingParking'))
    CREATE NONCLUSTERED INDEX [IX_ListingParking_ListingId] ON [dbo].[ListingParking] ([ListingId] ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ListingRoom_ListingId' AND object_id = OBJECT_ID('dbo.ListingRoom'))
    CREATE NONCLUSTERED INDEX [IX_ListingRoom_ListingId] ON [dbo].[ListingRoom] ([ListingId] ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ListingRoomCustomFeature_ListingRoomId' AND object_id = OBJECT_ID('dbo.ListingRoomCustomFeature'))
    CREATE NONCLUSTERED INDEX [IX_ListingRoomCustomFeature_ListingRoomId] ON [dbo].[ListingRoomCustomFeature] ([ListingRoomId] ASC);

-- The condition and running-costs upserts assume at most one row per room / listing.
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'UX_Condition_ListingRoomId' AND type = 'UQ')
    ALTER TABLE [dbo].[Condition] ADD CONSTRAINT [UX_Condition_ListingRoomId] UNIQUE NONCLUSTERED ([ListingRoomId] ASC);

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'UX_PropertyRunningCosts_ListingId' AND type = 'UQ')
    ALTER TABLE [dbo].[PropertyRunningCosts] ADD CONSTRAINT [UX_PropertyRunningCosts_ListingId] UNIQUE NONCLUSTERED ([ListingId] ASC);

-- ListingRoom's foreign keys were created NOCHECK, so they are neither enforced nor
-- trusted by the optimiser. One of them has a system-generated name that differs per
-- database, so re-check whichever ones are untrusted rather than naming them.
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE [dbo].[ListingRoom] WITH CHECK CHECK CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.foreign_keys
WHERE parent_object_id = OBJECT_ID('dbo.ListingRoom')
  AND (is_disabled = 1 OR is_not_trusted = 1);
EXEC sp_executesql @sql;

COMMIT TRANSACTION;
