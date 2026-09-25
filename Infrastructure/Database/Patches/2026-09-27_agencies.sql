-- Agencies: the white-label brands an agent can pick, served by GET /api/agencies,
-- so agencies can be added or restyled without an app release, and agencies agents
-- add themselves ("Other") are shared across devices and agents.
-- Not applied automatically -- run it once in SSMS or sqlcmd against the deployed
-- database, BEFORE deploying the API build that uses it. Safe to re-run: the table
-- is only created when missing, and seed rows are only inserted for slugs that are
-- not there yet (so later restyles in the database are never overwritten).
--
-- Colours are "#RRGGBB". NULL colours mean "use the RealWorth house palette" (agent-
-- added agencies have no brand colours). BannerColor NULL means "same as primary".
-- LogoUrl points at the logo in R2 (agencies/<slug>.png); the seeded rows get theirs
-- from tools/SeedAgencyLogos, which uploads the logos the app used to bundle.
-- IsCustom = 1 for agencies agents added; CreatedByUserId is who added them.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.Agencies', 'U') IS NULL
    CREATE TABLE [dbo].[Agencies] (
        [Id]              INT            IDENTITY (1, 1) NOT NULL,
        [Slug]            NVARCHAR (80)  NOT NULL,
        [Name]            NVARCHAR (150) NOT NULL,
        [Monogram]        NVARCHAR (4)   NOT NULL,
        [PrimaryColor]    CHAR (7)       NULL,
        [SecondaryColor]  CHAR (7)       NULL,
        [OnPrimaryColor]  CHAR (7)       NULL,
        [BannerColor]     CHAR (7)       NULL,
        [LogoUrl]         NVARCHAR (500) NULL,
        [SortOrder]       INT            CONSTRAINT [DF_Agencies_SortOrder] DEFAULT ((1000)) NOT NULL,
        [IsCustom]        BIT            CONSTRAINT [DF_Agencies_IsCustom] DEFAULT ((0)) NOT NULL,
        [IsActive]        BIT            CONSTRAINT [DF_Agencies_IsActive] DEFAULT ((1)) NOT NULL,
        [CreatedByUserId] INT            NULL,
        [CreatedAt]       DATETIME       CONSTRAINT [DF_Agencies_CreatedAt] DEFAULT (GETUTCDATE()) NOT NULL,
        [UpdatedAt]       DATETIME       NULL,
        CONSTRAINT [PK_Agencies] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Agencies_Slug' AND object_id = OBJECT_ID('dbo.Agencies'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_Agencies_Slug] ON [dbo].[Agencies] ([Slug] ASC);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Agencies_Users' AND parent_object_id = OBJECT_ID('dbo.Agencies'))
    ALTER TABLE [dbo].[Agencies] WITH CHECK
        ADD CONSTRAINT [FK_Agencies_Users] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[Users] ([Id]);

-- The brands the app shipped with, in the app's order (house brand first).
INSERT INTO [dbo].[Agencies] ([Slug], [Name], [Monogram], [PrimaryColor], [SecondaryColor], [OnPrimaryColor], [BannerColor], [SortOrder])
SELECT v.[Slug], v.[Name], v.[Monogram], v.[PrimaryColor], v.[SecondaryColor], v.[OnPrimaryColor], v.[BannerColor], v.[SortOrder]
FROM (VALUES
        (N'realworth', N'RealWorth', N'RW', N'#1B365D', N'#1E1E1E', N'#FFFFFF', N'#F7F4E5', 0),
        (N'acutts', N'Acutts Real Estate', N'AC', N'#00447C', N'#E30613', N'#FFFFFF', NULL, 1),
        (N'century-21', N'Century 21', N'C21', N'#1C1C1C', N'#B0985E', N'#FFFFFF', N'#000000', 2),
        (N'chas-everitt', N'Chas Everitt', N'CE', N'#245490', N'#F5A623', N'#FFFFFF', N'#FFFFFF', 3),
        (N'engel-volkers', N'Engel & Völkers', N'EV', N'#E40000', N'#242424', N'#FFFFFF', N'#FFFFFF', 4),
        (N'harcourts', N'Harcourts', N'HC', N'#002049', N'#00AAE6', N'#FFFFFF', NULL, 5),
        (N'jawitz', N'Jawitz Properties', N'JP', N'#0033A0', N'#E4002B', N'#FFFFFF', N'#FFFFFF', 6),
        (N'just-property', N'Just Property', N'JP', N'#093C71', N'#F0CC3C', N'#FFFFFF', NULL, 7),
        (N'keller-williams', N'Keller Williams', N'KW', N'#B41F25', N'#1E1E1E', N'#FFFFFF', NULL, 8),
        (N'leapfrog', N'Leapfrog Property Group', N'LF', N'#C2D83E', N'#303030', N'#1E1E1E', NULL, 9),
        (N'lew-geffen', N'Lew Geffen Sotheby''s International Realty', N'LG', N'#0C183C', N'#B0985E', N'#FFFFFF', N'#112347', 10),
        (N'meridian', N'Meridian Realty', N'MR', N'#123368', N'#F5A623', N'#FFFFFF', NULL, 11),
        (N'pam-golding', N'Pam Golding Properties', N'PG', N'#014423', N'#B8975A', N'#FFFFFF', NULL, 12),
        (N'property-coza', N'Property.CoZa', N'PC', N'#991B1E', N'#1E1E1E', N'#FFFFFF', NULL, 13),
        (N'quay-1', N'Quay 1 International Realty', N'Q1', N'#3C5AA5', N'#FCC000', N'#FFFFFF', NULL, 14),
        (N'rawson', N'Rawson Property Group', N'RP', N'#FED404', N'#E6281E', N'#181818', NULL, 15),
        (N'realtors-international', N'Realtors International', N'RI', N'#071F45', N'#B0985E', N'#FFFFFF', NULL, 16),
        (N'remax', N'RE/MAX', N'RM', N'#003DA5', N'#D81824', N'#FFFFFF', N'#FFFFFF', 17),
        (N'seeff', N'Seeff Property Group', N'SF', N'#17214B', N'#B41824', N'#FFFFFF', NULL, 18),
        (N'sothebys', N'Sotheby''s International Realty', N'SIR', N'#002454', N'#B0985E', N'#FFFFFF', N'#FFFFFF', 19),
        (N'tyson', N'Tyson Properties', N'TP', N'#002E2E', N'#C9A227', N'#FFFFFF', NULL, 20)
    ) AS v ([Slug], [Name], [Monogram], [PrimaryColor], [SecondaryColor], [OnPrimaryColor], [BannerColor], [SortOrder])
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Agencies] a WHERE a.[Slug] = v.[Slug]);

COMMIT TRANSACTION;
