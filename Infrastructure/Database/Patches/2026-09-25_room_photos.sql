-- Room photos: up to 20 photos per room, used by
-- GET/POST /api/listings/{listingId}/rooms/{roomId}/photos and
-- DELETE /api/listings/{listingId}/rooms/{roomId}/photos/{photoId}.
-- Not applied automatically -- run it once in SSMS or sqlcmd against the deployed
-- database, BEFORE deploying the API build that uses it. Safe to re-run: every step
-- checks first, and the backfill skips rooms that already have photo rows.
--
-- Url is whatever IImageStorage returned (relative "/uploads/..." locally, absolute on R2),
-- exactly like ListingPhoto.Url. StorageKey is the server-generated key
-- ("rooms/{listingId}/{roomId}/{guid}.ext"); it is NULL only for backfilled rows whose
-- legacy URL does not follow that layout, and the API never deletes those objects.
-- ListingRoom.PhotoUrl stays as the room's cover (lowest SortOrder) for older app builds.
-- Rows go with their room via ON DELETE CASCADE; the API deletes the stored objects.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.ListingRoomPhotos', 'U') IS NULL
    CREATE TABLE [dbo].[ListingRoomPhotos] (
        [Id]            INT            IDENTITY (1, 1) NOT NULL,
        [ListingRoomId] INT            NOT NULL,
        [Url]           NVARCHAR (512) NOT NULL,
        [StorageKey]    NVARCHAR (512) NULL,
        [SortOrder]     INT            CONSTRAINT [DF_ListingRoomPhotos_SortOrder] DEFAULT (0) NOT NULL,
        [CreatedAt]     DATETIME       CONSTRAINT [DF_ListingRoomPhotos_CreatedAt] DEFAULT (GETUTCDATE()) NOT NULL,
        CONSTRAINT [PK_ListingRoomPhotos] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ListingRoomPhotos_ListingRoom' AND parent_object_id = OBJECT_ID('dbo.ListingRoomPhotos'))
    ALTER TABLE [dbo].[ListingRoomPhotos] WITH CHECK
        ADD CONSTRAINT [FK_ListingRoomPhotos_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id]) ON DELETE CASCADE;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ListingRoomPhotos_ListingRoomId' AND object_id = OBJECT_ID('dbo.ListingRoomPhotos'))
    CREATE NONCLUSTERED INDEX [IX_ListingRoomPhotos_ListingRoomId] ON [dbo].[ListingRoomPhotos] ([ListingRoomId] ASC, [SortOrder] ASC);

-- Backfill: each room's existing single photo becomes its first (SortOrder 0) photo.
-- The key is the part of the URL from "rooms/{listingId}/{roomId}/" on, which is how the
-- API builds it for both storage providers; any other URL gets a NULL key. URLs longer
-- than the new column (never produced by the API) are left as cover-only.
INSERT INTO [dbo].[ListingRoomPhotos] ([ListingRoomId], [Url], [StorageKey], [SortOrder], [CreatedAt])
SELECT r.[Id],
       r.[PhotoUrl],
       CASE WHEN CHARINDEX(m.[Marker], r.[PhotoUrl]) > 0
            THEN SUBSTRING(r.[PhotoUrl], CHARINDEX(m.[Marker], r.[PhotoUrl]) + 1, 512)
       END,
       0,
       GETUTCDATE()
FROM [dbo].[ListingRoom] r
CROSS APPLY (SELECT CONCAT(N'/rooms/', r.[ListingId], N'/', r.[Id], N'/') AS [Marker]) m
WHERE r.[PhotoUrl] IS NOT NULL
  AND LEN(r.[PhotoUrl]) <= 512
  AND NOT EXISTS (SELECT 1 FROM [dbo].[ListingRoomPhotos] p WHERE p.[ListingRoomId] = r.[Id]);

COMMIT TRANSACTION;
