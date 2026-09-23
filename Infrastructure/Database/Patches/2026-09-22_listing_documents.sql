-- Listing documents (utility bills etc. on a listing's Expenses section), used by
-- GET/POST/DELETE /api/listings/{id}/documents.
-- Not applied automatically -- run it once in SSMS or sqlcmd against the deployed
-- database. Safe to re-run: every step checks first.
--
-- StorageKey is the server-generated IImageStorage key ("listings/{id}/documents/{guid}.ext");
-- Url is whatever the storage returned (relative "/uploads/..." locally, absolute on R2),
-- exactly like ListingPhoto.Url. FileName is the sanitised original name, display only.
-- Rows go with their listing via ON DELETE CASCADE; the API deletes the stored objects.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.ListingDocuments', 'U') IS NULL
    CREATE TABLE [dbo].[ListingDocuments] (
        [Id]          INT            IDENTITY (1, 1) NOT NULL,
        [ListingId]   INT            NOT NULL,
        [Category]    NVARCHAR (20)  NOT NULL,
        [FileName]    NVARCHAR (200) NOT NULL,
        [StorageKey]  NVARCHAR (512) NOT NULL,
        [Url]         NVARCHAR (512) NOT NULL,
        [ContentType] NVARCHAR (100) NOT NULL,
        [SizeBytes]   BIGINT         NOT NULL,
        [CreatedAt]   DATETIME       CONSTRAINT [DF_ListingDocuments_CreatedAt] DEFAULT (GETUTCDATE()) NOT NULL,
        CONSTRAINT [PK_ListingDocuments] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ListingDocuments_Listings' AND parent_object_id = OBJECT_ID('dbo.ListingDocuments'))
    ALTER TABLE [dbo].[ListingDocuments] WITH CHECK
        ADD CONSTRAINT [FK_ListingDocuments_Listings] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ListingDocuments_ListingId' AND object_id = OBJECT_ID('dbo.ListingDocuments'))
    CREATE NONCLUSTERED INDEX [IX_ListingDocuments_ListingId] ON [dbo].[ListingDocuments] ([ListingId] ASC);

COMMIT TRANSACTION;
